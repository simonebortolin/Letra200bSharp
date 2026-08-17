using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;
using SkiaSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class TextTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly Action<LetraPrintResult> _recordStats;

    public ObservableCollection<string> FontFamilies { get; }
    public ObservableCollection<string> Sizes { get; } = new(Enum.GetNames<LetraHelper.LabelTextSize>());
    public ObservableCollection<string> Styles { get; } = new(Enum.GetNames<LetraHelper.TextStyle>());
    public ObservableCollection<string> BoxStyles { get; } = new(Enum.GetNames<LetraHelper.TextBoxStyle>());
    public ObservableCollection<string> Aligns { get; } = new(Enum.GetNames<LetraHelper.TextAlign>());

    [ObservableProperty]
    public partial string Line1 { get; set; } = "Hello world";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line2Enabled))]
    public partial string? Line2 { get; set; }

    [ObservableProperty]
    public partial string? SelectedFontFamily { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line2Enabled))]
    [NotifyPropertyChangedFor(nameof(Line1Label))]
    [NotifyPropertyChangedFor(nameof(BoxStyleEnabled))]
    public partial string SelectedSize { get; set; } = "M";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line2Enabled))]
    [NotifyPropertyChangedFor(nameof(Line1Label))]
    public partial string SelectedStyle { get; set; } = nameof(LetraHelper.TextStyle.Normal);

    [ObservableProperty]
    public partial string SelectedBoxStyle { get; set; } = nameof(LetraHelper.TextBoxStyle.None);

    [ObservableProperty]
    public partial string SelectedAlign { get; set; } = nameof(LetraHelper.TextAlign.Left);

    [ObservableProperty]
    public partial bool UpperCase { get; set; }

    [ObservableProperty]
    public partial decimal WidthScale { get; set; } = 1.0m;

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    /// <summary>
    /// A second line doesn't make sense with L/XL (barely any margin left to split between
    /// two lines) or with the Vertical style (already splits into one line per character).
    /// </summary>
    public bool Line2Enabled => SelectedStyle != nameof(LetraHelper.TextStyle.Vertical) && SelectedSize != "L" && SelectedSize != "XL";

    /// <summary>"Text:" on its own when there's only one line, "Text Line 1:" once a second line is available too.</summary>
    public string Line1Label => Line2Enabled ? Strings.TextTab_Line1LabelWithLine2 : Strings.TextTab_Line1LabelSingle;

    /// <summary>
    /// XL fills the entire printable height with no margin around the text (see
    /// <see cref="LetraHelper.LabelTextSize.XL"/>), so there's no room left to draw a border
    /// without it overlapping the text or the printer's unprintable edges.
    /// </summary>
    public bool BoxStyleEnabled => SelectedSize != "XL";

    public TextTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _recordStats = recordStats;

        var fontFamilies = SKFontManager.Default.FontFamilies.OrderBy(f => f).ToArray();
        FontFamilies = new ObservableCollection<string>(fontFamilies);
        SelectedFontFamily = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();
    }

    /// <summary>Restores a previously printed text label (see <see cref="Services.HistoryEntry.TextParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(TextHistoryParams parameters)
    {
        Line1 = parameters.Line1;
        Line2 = parameters.Line2;
        if (parameters.FontFamily != null && FontFamilies.Contains(parameters.FontFamily))
        {
            SelectedFontFamily = parameters.FontFamily;
        }
        SelectedSize = parameters.Size;
        SelectedStyle = parameters.Style;
        WidthScale = parameters.WidthScale;
        SelectedBoxStyle = parameters.BoxStyle;
        UpperCase = parameters.UpperCase;
        SelectedAlign = parameters.Align;

        PreviewCommand.Execute(null);
    }

    partial void OnSelectedSizeChanged(string value)
    {
        if (!BoxStyleEnabled)
        {
            SelectedBoxStyle = nameof(LetraHelper.TextBoxStyle.None);
        }
    }

    /// <summary>Joins the two lines with <see cref="Environment.NewLine"/> so <see cref="LetraHelper"/> renders them stacked.</summary>
    private string ComposedText => Line2Enabled && !string.IsNullOrEmpty(Line2)
        ? Line1 + Environment.NewLine + Line2
        : Line1;

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var text = ComposedText;
        if (string.IsNullOrEmpty(text))
        {
            var previousBitmap = PreviewBitmap;
            PreviewBitmap = null;
            previousBitmap?.Dispose();
            return;
        }

        var fontFamily = SelectedFontFamily ?? "Arial";
        var size = Enum.Parse<LetraHelper.LabelTextSize>(SelectedSize);
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var upperCase = UpperCase;
        var widthScale = (float)WidthScale;
        var boxStyle = Enum.Parse<LetraHelper.TextBoxStyle>(SelectedBoxStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);

        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true);
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _reportStatus(string.Format(Strings.Status_UnableToGeneratePreview, ex.Message), true);
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        var text = ComposedText;
        if (string.IsNullOrEmpty(text))
        {
            _reportStatus(Strings.TextTab_NoTextEntered, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var fontFamily = SelectedFontFamily ?? "Arial";
        var size = Enum.Parse<LetraHelper.LabelTextSize>(SelectedSize);
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var upperCase = UpperCase;
        var widthScale = (float)WidthScale;
        var boxStyle = Enum.Parse<LetraHelper.TextBoxStyle>(SelectedBoxStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                RecordHistory(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align);
            }
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Best-effort: a history-recording failure must never look like the print itself failed,
    /// so this is never allowed to bubble into <see cref="PrintAsync"/>'s own error reporting.
    /// </summary>
    private void RecordHistory(string text, string fontFamily, LetraHelper.LabelTextSize size, LetraHelper.TextStyle style, bool upperCase, float widthScale, LetraHelper.TextBoxStyle boxStyle, LetraHelper.TextAlign align)
    {
        try
        {
            var thumbnail = LetraHelper.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true);
            var parameters = new TextHistoryParams(Line1, Line2, fontFamily, SelectedSize, SelectedStyle, WidthScale, SelectedBoxStyle, upperCase, SelectedAlign);
            _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Text, text, thumbnail, TextParams: parameters));
        }
        catch
        {
            // See summary above.
        }
    }
}
