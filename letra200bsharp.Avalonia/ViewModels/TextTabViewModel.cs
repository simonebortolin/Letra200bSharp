using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using letra200bsharp;
using SkiaSharp;

namespace letra200bsharp.Avalonia.ViewModels;

public partial class TextTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;

    public ObservableCollection<string> FontFamilies { get; }
    public ObservableCollection<string> Sizes { get; } = new(Enum.GetNames<LetraHelper.LabelTextSize>());
    public ObservableCollection<string> Styles { get; } = new(Enum.GetNames<LetraHelper.TextStyle>());
    public ObservableCollection<string> BoxStyles { get; } = new(Enum.GetNames<LetraHelper.TextBoxStyle>());

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
    public partial string SelectedSize { get; set; } = "M";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line2Enabled))]
    [NotifyPropertyChangedFor(nameof(Line1Label))]
    public partial string SelectedStyle { get; set; } = nameof(LetraHelper.TextStyle.Normal);

    [ObservableProperty]
    public partial string SelectedBoxStyle { get; set; } = nameof(LetraHelper.TextBoxStyle.None);

    [ObservableProperty]
    public partial bool UpperCase { get; set; }

    [ObservableProperty]
    public partial decimal WidthScale { get; set; } = 1.0m;

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// A second line doesn't make sense with L/XL (barely any margin left to split between
    /// two lines) or with the Vertical style (already splits into one line per character).
    /// </summary>
    public bool Line2Enabled => SelectedStyle != nameof(LetraHelper.TextStyle.Vertical) && SelectedSize != "L" && SelectedSize != "XL";

    /// <summary>"Text:" on its own when there's only one line, "Text Line 1:" once a second line is available too.</summary>
    public string Line1Label => Line2Enabled ? "Text Line 1:" : "Text:";

    public TextTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;

        var fontFamilies = SKFontManager.Default.FontFamilies.OrderBy(f => f).ToArray();
        FontFamilies = new ObservableCollection<string>(fontFamilies);
        SelectedFontFamily = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();
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

        try
        {
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, true);
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            _reportStatus($"Unable to generate preview: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        var text = ComposedText;
        if (string.IsNullOrEmpty(text))
        {
            _reportStatus("No text entered.", true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus("No device selected.", true);
            return;
        }

        var fontFamily = SelectedFontFamily ?? "Arial";
        var size = Enum.Parse<LetraHelper.LabelTextSize>(SelectedSize);
        var style = Enum.Parse<LetraHelper.TextStyle>(SelectedStyle);
        var upperCase = UpperCase;
        var widthScale = (float)WidthScale;
        var boxStyle = Enum.Parse<LetraHelper.TextBoxStyle>(SelectedBoxStyle);

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(text, fontFamily, size, style, upperCase, widthScale, boxStyle, true));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
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
}
