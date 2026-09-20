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

public partial class BarcodeTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly CompositionService _composition;
    private readonly IRenderHelper _render;
    private readonly ILetraHelper _letra;
    private readonly Action<LetraPrintResult> _recordStats;

    public ObservableCollection<string> Symbologies { get; } = new(Enum.GetNames<LetraHelper.BarcodeSymbology>());
    public ObservableCollection<string> CaptionPositions { get; } = new(Enum.GetNames<LetraHelper.CaptionPosition>());
    public ObservableCollection<string> CaptionSizes { get; } = new(Enum.GetNames<LetraHelper.CaptionSize>());
    public ObservableCollection<string> CaptionAligns { get; } = new(Enum.GetNames<LetraHelper.TextAlign>());
    public ObservableCollection<string> FontFamilies { get; }

    [ObservableProperty]
    public partial string Data { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedSymbology { get; set; } = nameof(LetraHelper.BarcodeSymbology.Auto);

    [ObservableProperty]
    public partial bool NoCut { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptionStyleEnabled))]
    public partial string SelectedCaptionPosition { get; set; } = nameof(LetraHelper.CaptionPosition.None);

    [ObservableProperty]
    public partial string? SelectedCaptionFontFamily { get; set; }

    [ObservableProperty]
    public partial string SelectedCaptionSize { get; set; } = nameof(LetraHelper.CaptionSize.M);

    [ObservableProperty]
    public partial string SelectedCaptionAlign { get; set; } = nameof(LetraHelper.TextAlign.Center);

    /// <summary>Font/size/align only mean anything once a caption position other than <see cref="LetraHelper.CaptionPosition.None"/> is picked - disables (rather than hides) those controls otherwise, same convention as e.g. TextTab's BoxStyleEnabled.</summary>
    public bool CaptionStyleEnabled => SelectedCaptionPosition != nameof(LetraHelper.CaptionPosition.None);

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    public BarcodeTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, CompositionService composition, IRenderHelper render, ILetraHelper letra, Action<LetraPrintResult> recordStats)
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
        SelectedCaptionFontFamily = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();
    }

    private LetraHelper.CaptionOptions BuildCaption() => new(
        Enum.Parse<LetraHelper.CaptionPosition>(SelectedCaptionPosition),
        SelectedCaptionFontFamily ?? "Arial",
        Enum.Parse<LetraHelper.CaptionSize>(SelectedCaptionSize),
        Enum.Parse<LetraHelper.TextAlign>(SelectedCaptionAlign));

    /// <summary>Restores a previously printed barcode (see <see cref="Services.HistoryEntry.BarcodeParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(BarcodeHistoryParams parameters)
    {
        Data = parameters.Data;
        SelectedSymbology = parameters.Symbology;
        NoCut = parameters.NoCut;
        SelectedCaptionPosition = CaptionPositions.Contains(parameters.CaptionPosition) ? parameters.CaptionPosition : nameof(LetraHelper.CaptionPosition.None);
        if (parameters.CaptionFontFamily != null && FontFamilies.Contains(parameters.CaptionFontFamily))
        {
            SelectedCaptionFontFamily = parameters.CaptionFontFamily;
        }
        SelectedCaptionSize = CaptionSizes.Contains(parameters.CaptionSize) ? parameters.CaptionSize : nameof(LetraHelper.CaptionSize.M);
        SelectedCaptionAlign = CaptionAligns.Contains(parameters.CaptionAlign) ? parameters.CaptionAlign : nameof(LetraHelper.TextAlign.Center);

        PreviewCommand.Execute(null);
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            var previousBitmap = PreviewBitmap;
            PreviewBitmap = null;
            previousBitmap?.Dispose();
            return;
        }

        var symbology = Enum.Parse<LetraHelper.BarcodeSymbology>(SelectedSymbology);
        var noCut = NoCut;
        var caption = BuildCaption();

        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = _render.PreviewImage(data, symbology, noCut, caption);
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
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            _reportStatus(Strings.BarcodeTab_NoDataEntered, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var symbology = Enum.Parse<LetraHelper.BarcodeSymbology>(SelectedSymbology);
        var noCut = NoCut;
        var caption = BuildCaption();

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => _letra.CreateJob(data, symbology, noCut, caption));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(data, symbology, noCut, caption, printed: true);
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
    /// Saves the current barcode to history without printing it - lets a design be kept/reused
    /// (see <see cref="Services.HistoryEntry.Printed"/>) even without a printer in reach. Unlike
    /// the best-effort recording after a successful print, a failure here is the whole point of
    /// the action, so it's reported to the user instead of swallowed.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            _reportStatus(Strings.BarcodeTab_NoDataEntered, true);
            return;
        }

        var symbology = Enum.Parse<LetraHelper.BarcodeSymbology>(SelectedSymbology);
        try
        {
            RecordHistory(data, symbology, NoCut, BuildCaption(), printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    private void RecordHistory(string data, LetraHelper.BarcodeSymbology symbology, bool noCut, LetraHelper.CaptionOptions caption, bool printed)
    {
        var thumbnail = _render.PreviewImage(data, symbology, noCut, caption);
        var parameters = new BarcodeHistoryParams(data, SelectedSymbology, noCut, SelectedCaptionPosition, SelectedCaptionFontFamily ?? "Arial", SelectedCaptionSize, SelectedCaptionAlign);
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Barcode, $"{SelectedSymbology}: {data}", thumbnail, BarcodeParams: parameters, Printed: printed));
    }

    /// <summary>Adds the current barcode to the Compose tab's staging list (see <see cref="CompositionService"/>) - lets it become one part of a longer, multi-element printed strip.</summary>
    [RelayCommand]
    private void Concatenate()
    {
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            _reportStatus(Strings.BarcodeTab_NoDataEntered, true);
            return;
        }

        var symbology = Enum.Parse<LetraHelper.BarcodeSymbology>(SelectedSymbology);
        try
        {
            var caption = BuildCaption();
            var thumbnail = _render.PreviewImage(data, symbology, NoCut, caption);
            var parameters = new BarcodeHistoryParams(data, SelectedSymbology, NoCut, SelectedCaptionPosition, SelectedCaptionFontFamily ?? "Arial", SelectedCaptionSize, SelectedCaptionAlign);
            _composition.Add(new ComposeElement(Guid.NewGuid(), ComposeElementKind.Barcode, $"{SelectedSymbology}: {data}", thumbnail, BarcodeParams: parameters));
            _reportStatus(Strings.Status_AddedToComposition, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }
}
