using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>
/// Encodes text into a 2D matrix code (QR, Micro QR, rMQR or Data Matrix) and prints it. Unlike
/// the Barcode tab, module size isn't a user choice - a matrix code's modules have to stay
/// square, so <see cref="LetraHelper"/> picks the largest module size that fits the printer's
/// head axis and this tab just surfaces what came out (see <see cref="LetraHelper.TwoDPlan"/>),
/// warning via <see cref="MayNotScanWell"/> when the result would be too small to scan reliably.
/// </summary>
public partial class QrTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly Action<LetraPrintResult> _recordStats;

    /// <summary>ComboBox display label paired with the <see cref="LetraHelper.TwoDSymbology"/> it selects.</summary>
    private static readonly (string Label, LetraHelper.TwoDSymbology Value)[] SymbologyChoices =
    {
        ("Auto", LetraHelper.TwoDSymbology.Auto),
        ("QR", LetraHelper.TwoDSymbology.QrCode),
        ("Micro QR", LetraHelper.TwoDSymbology.MicroQrCode),
        ("rMQR (rectangular)", LetraHelper.TwoDSymbology.RectangularMicroQrCode),
        ("Data Matrix", LetraHelper.TwoDSymbology.DataMatrix),
    };

    public ObservableCollection<string> Symbologies { get; } = new(SymbologyChoices.Select(c => c.Label));

    [ObservableProperty]
    public partial string Data { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedSymbology { get; set; } = SymbologyChoices[0].Label;

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    /// <summary>"rMQR R11x27 · 2×4 dots/module · ~0.32 mm" once a preview has resolved, otherwise empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSymbolInfo))]
    public partial string SymbolInfoText { get; set; } = "";

    public bool HasSymbolInfo => !string.IsNullOrEmpty(SymbolInfoText);

    /// <summary>Set when the resolved symbol only gets one printer dot per module (~0.16 mm) - see <see cref="LetraHelper.TwoDPlan.MayNotScanWell"/>.</summary>
    [ObservableProperty]
    public partial bool MayNotScanWell { get; set; }

    public QrTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _recordStats = recordStats;
    }

    private LetraHelper.TwoDSymbology CurrentSymbology =>
        SymbologyChoices.FirstOrDefault(c => c.Label == SelectedSymbology, SymbologyChoices[0]).Value;

    /// <summary>Restores a previously printed 2D code (see <see cref="Services.HistoryEntry.QrParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(QrHistoryParams parameters)
    {
        Data = parameters.Data;
        SelectedSymbology = SymbologyChoices.Any(c => c.Label == parameters.Symbology)
            ? parameters.Symbology
            : SymbologyChoices[0].Label;

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
            SymbolInfoText = "";
            MayNotScanWell = false;
            return;
        }

        var symbology = CurrentSymbology;

        try
        {
            IsPreviewLoading = true;
            var (bitmap, plan) = await Task.Run(() =>
            {
                var resolvedPlan = LetraHelper.PlanTwoDImage(data, symbology);
                var previewBytes = LetraHelper.PreviewImage(data, symbology);
                using var stream = new MemoryStream(previewBytes);
                return (new Bitmap(stream), resolvedPlan);
            });

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();

            SymbolInfoText = string.Format(Strings.QrTab_SymbolInfoFormat, plan.SymbolName, plan.DotsPerModuleShort, plan.DotsPerModuleLong, plan.ModuleSizeMm);
            MayNotScanWell = plan.MayNotScanWell;
        }
        catch (Exception ex)
        {
            SymbolInfoText = "";
            MayNotScanWell = false;
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
            _reportStatus(Strings.QrTab_NoDataEntered, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var symbology = CurrentSymbology;

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(data, symbology));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(data, symbology, printed: true);
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
    /// Saves the current 2D code to history without printing it - lets a design be kept/reused
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
            _reportStatus(Strings.QrTab_NoDataEntered, true);
            return;
        }

        try
        {
            RecordHistory(data, CurrentSymbology, printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    private void RecordHistory(string data, LetraHelper.TwoDSymbology symbology, bool printed)
    {
        var thumbnail = LetraHelper.PreviewImage(data, symbology);
        var plan = LetraHelper.PlanTwoDImage(data, symbology);
        var parameters = new QrHistoryParams(data, SelectedSymbology);
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Qr2D, $"{plan.SymbolName}: {data}", thumbnail, QrParams: parameters, Printed: printed));
    }
}
