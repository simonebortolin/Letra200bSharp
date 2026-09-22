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
    private readonly CompositionService _composition;
    private readonly IRenderHelper _render;
    private readonly ILetraHelper _letra;
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
    [NotifyPropertyChangedFor(nameof(FrameSpacingEnabled))]
    public partial string SelectedSize { get; set; } = "M";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line2Enabled))]
    [NotifyPropertyChangedFor(nameof(Line1Label))]
    public partial string SelectedStyle { get; set; } = nameof(LetraHelper.TextStyle.Normal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameSpacingEnabled))]
    public partial string SelectedBoxStyle { get; set; } = nameof(LetraHelper.TextBoxStyle.None);

    [ObservableProperty]
    public partial string SelectedAlign { get; set; } = nameof(LetraHelper.TextAlign.Left);

    [ObservableProperty]
    public partial bool UpperCase { get; set; }

    [ObservableProperty]
    public partial decimal WidthScale { get; set; } = 1.0m;

    /// <summary>Independently toggleable alongside <see cref="Italic"/>/<see cref="Underline"/>/<see cref="Strikethrough"/> and any <see cref="SelectedStyle"/> - unlike SelectedStyle's Outline/Shadow/Vertical, these combine freely (e.g. Bold + Italic + Underline together).</summary>
    [ObservableProperty]
    public partial bool Bold { get; set; }

    [ObservableProperty]
    public partial bool Italic { get; set; }

    [ObservableProperty]
    public partial bool Underline { get; set; }

    [ObservableProperty]
    public partial bool Strikethrough { get; set; }

    /// <summary>Extra gap after every glyph, as a fraction of the em size - independent of <see cref="WidthScale"/>, which stretches glyphs instead of spacing them apart.</summary>
    [ObservableProperty]
    public partial decimal LetterSpacing { get; set; } = 0m;

    // decimal (not int) to match how every other NumericUpDown-bound property in this tab
    // (e.g. WidthScale) is typed - keeps Avalonia's NumericUpDown.Value (decimal?) binding
    // straightforward, converting to int only where LetraHelper.FrameSpacing actually needs it.
    [ObservableProperty]
    public partial decimal FrameTop { get; set; }

    [ObservableProperty]
    public partial decimal FrameBottom { get; set; }

    [ObservableProperty]
    public partial decimal FrameLeft { get; set; }

    [ObservableProperty]
    public partial decimal FrameRight { get; set; }

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

    /// <summary>Per-side frame margin only means anything once a frame is actually drawn.</summary>
    public bool FrameSpacingEnabled => BoxStyleEnabled && SelectedBoxStyle != nameof(LetraHelper.TextBoxStyle.None);

    public TextTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, CompositionService composition, IRenderHelper render, ILetraHelper letra, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _composition = composition;
        _render = render;
        _letra = letra;
        _recordStats = recordStats;

        var fontFamilies = SKFontManager.Default.FontFamilies.OrderBy(f => f).ToArray();
        FontFamilies = new ObservableCollection<string>(fontFamilies);
        SelectedFontFamily = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();
    }

    /// <summary>Restores a previously printed text label (see <see cref="Services.HistoryEntry.TextParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(TextHistoryParams rawParameters)
    {
        // Pre-1.4 entries stored Bold/Italic as SelectedStyle values, which no longer exist on
        // LetraHelper.TextStyle - map them onto the new independent Bold/Italic fields instead.
        var parameters = rawParameters.WithLegacyStyleMigrated();

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
        Bold = parameters.Bold;
        Italic = parameters.Italic;
        Underline = parameters.Underline;
        Strikethrough = parameters.Strikethrough;
        LetterSpacing = parameters.LetterSpacing;
        FrameTop = parameters.FrameTop;
        FrameBottom = parameters.FrameBottom;
        FrameLeft = parameters.FrameLeft;
        FrameRight = parameters.FrameRight;

        PreviewCommand.Execute(null);
    }

    private LetraHelper.TextFormatting BuildFormatting() => new(Bold, Italic, Underline, Strikethrough, (float)LetterSpacing);

    private LetraHelper.FrameSpacing BuildFrameSpacing() => new((int)FrameTop, (int)FrameBottom, (int)FrameLeft, (int)FrameRight);

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
        var formatting = BuildFormatting();
        var frameSpacing = BuildFrameSpacing();

        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = _render.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true, formatting, frameSpacing);
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
        var formatting = BuildFormatting();
        var frameSpacing = BuildFrameSpacing();
        var parameters = BuildHistoryParams();

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => _letra.CreateJob(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true, formatting, frameSpacing));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, formatting, frameSpacing, parameters, printed: true);
                }
                catch
                {
                    // A history-recording failure must never look like the print itself failed.
                }
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
    /// Saves the current text label to history without printing it - lets a design be kept/reused
    /// (see <see cref="Services.HistoryEntry.Printed"/>) even without a printer in reach. Unlike
    /// the best-effort recording after a successful print, a failure here is the whole point of
    /// the action, so it's reported to the user instead of swallowed.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        var text = ComposedText;
        if (string.IsNullOrEmpty(text))
        {
            _reportStatus(Strings.TextTab_NoTextEntered, true);
            return;
        }

        var fontFamily = SelectedFontFamily ?? "Arial";
        var size = Enum.Parse<LetraHelper.LabelTextSize>(SelectedSize);
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var widthScale = (float)WidthScale;
        var boxStyle = Enum.Parse<LetraHelper.TextBoxStyle>(SelectedBoxStyle);
        var align = Enum.Parse<LetraHelper.TextAlign>(SelectedAlign);

        try
        {
            RecordHistory(text, fontFamily, size, style, UpperCase, widthScale, boxStyle, align, BuildFormatting(), BuildFrameSpacing(), BuildHistoryParams(), printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    /// <summary>Snapshot of the tab's current settings, taken before any <c>await</c> so history always records what was actually printed.</summary>
    private TextHistoryParams BuildHistoryParams() =>
        new(Line1, Line2, SelectedFontFamily ?? "Arial", SelectedSize, SelectedStyle, WidthScale, SelectedBoxStyle, UpperCase, SelectedAlign,
            Bold, Italic, Underline, Strikethrough, LetterSpacing, (int)FrameTop, (int)FrameBottom, (int)FrameLeft, (int)FrameRight);

    private void RecordHistory(string text, string fontFamily, LetraHelper.LabelTextSize size, LetraHelper.TextStyle style, bool upperCase, float widthScale, LetraHelper.TextBoxStyle boxStyle, LetraHelper.TextAlign align, LetraHelper.TextFormatting formatting, LetraHelper.FrameSpacing frameSpacing, TextHistoryParams parameters, bool printed)
    {
        var thumbnail = _render.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, true, formatting, frameSpacing);
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Text, text, thumbnail, TextParams: parameters, Printed: printed));
    }

    /// <summary>Adds the current text label to the Compose tab's staging list (see <see cref="CompositionService"/>) - lets it become one part of a longer, multi-element printed strip.</summary>
    [RelayCommand]
    private void Concatenate()
    {
        var text = ComposedText;
        if (string.IsNullOrEmpty(text))
        {
            _reportStatus(Strings.TextTab_NoTextEntered, true);
            return;
        }

        try
        {
            _composition.Stage(ComposeElementKind.Text, text, text: BuildHistoryParams());
            _reportStatus(Strings.Status_AddedToComposition, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }
}
