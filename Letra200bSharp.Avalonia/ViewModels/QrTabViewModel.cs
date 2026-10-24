using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>
/// Encodes text into a 2D matrix code (QR, Micro QR, rMQR or Data Matrix) and prints it. Unlike
/// the Barcode tab, module size isn't a user choice - a matrix code's modules have to stay
/// square, so the renderer picks the largest module size that fits the printer's
/// head axis and this tab just surfaces what came out (see <see cref="TwoDCodePlan"/>),
/// warning via <see cref="MayNotScanWell"/> when the result would be too small to scan reliably.
/// </summary>
public partial class QrTabViewModel : ViewModelBase
{
    private readonly Func<PrinterInfo?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly CompositionService _composition;
    private readonly ILabelPrintingService _printing;
    private readonly Action<PrintResult> _recordStats;

    private static IReadOnlyList<(string Label, TwoDSymbology Value)> SymbologyChoices => QrHistoryParams.SymbologyChoices;

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

    /// <summary>Set when the resolved symbol only gets one printer dot per module (~0.16 mm) - see <see cref="TwoDCodePlan.MayNotScanWell"/>.</summary>
    [ObservableProperty]
    public partial bool MayNotScanWell { get; set; }

    public QrTabViewModel(Func<PrinterInfo?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, CompositionService composition, ILabelPrintingService printing, Action<PrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _composition = composition;
        _printing = printing;
        _recordStats = recordStats;
    }

    private TwoDSymbology CurrentSymbology =>
        QrHistoryParams.ParseSymbology(SelectedSymbology);

    /// <summary>How the printer's renderer lays the code out, or <c>null</c> if it can't tell (see <see cref="ITwoDCodePlanner"/>).</summary>
    private TwoDCodePlan? Plan(TwoDCodeLabel label, PrinterInfo? printer) =>
        (_printing.GetRenderer(printer) as ITwoDCodePlanner)?.Plan(label);

    private static string Summary(TwoDCodePlan? plan, string data) => plan == null ? data : $"{plan.SymbolName}: {data}";

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

        var label = new TwoDCodeLabel(data, CurrentSymbology);
        var printer = _getSelectedDevice();

        try
        {
            IsPreviewLoading = true;
            var (bitmap, plan) = await Task.Run(() =>
            {
                var resolvedPlan = Plan(label, printer);
                var previewBytes = _printing.RenderPreview(label, printer);
                using var stream = new MemoryStream(previewBytes);
                return (new Bitmap(stream), resolvedPlan);
            });

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();

            SymbolInfoText = plan == null ? "" : string.Format(Strings.QrTab_SymbolInfoFormat, plan.SymbolName, plan.DotsPerModuleShort, plan.DotsPerModuleLong, plan.ModuleSizeMm);
            MayNotScanWell = plan?.MayNotScanWell ?? false;
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
        var parameters = BuildHistoryParams();

        IsBusy = true;
        try
        {
            var result = await _printing.PrintAsync(device, new TwoDCodeLabel(data, symbology));
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(parameters, symbology, printed: true);
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
            RecordHistory(BuildHistoryParams(), CurrentSymbology, printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    /// <summary>Snapshot of the tab's current settings, taken before any <c>await</c> so history always records what was actually printed.</summary>
    private QrHistoryParams BuildHistoryParams() => new(Data, SelectedSymbology);

    private void RecordHistory(QrHistoryParams parameters, TwoDSymbology symbology, bool printed)
    {
        var label = new TwoDCodeLabel(parameters.Data, symbology);
        var printer = _getSelectedDevice();
        var thumbnail = _printing.RenderPreview(label, printer);
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Qr2D, Summary(Plan(label, printer), parameters.Data), thumbnail, QrParams: parameters, Printed: printed));
    }

    /// <summary>Adds the current 2D code to the Compose tab's staging list (see <see cref="CompositionService"/>) - lets it become one part of a longer, multi-element printed strip.</summary>
    [RelayCommand]
    private void Concatenate()
    {
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            _reportStatus(Strings.QrTab_NoDataEntered, true);
            return;
        }

        try
        {
            var parameters = BuildHistoryParams();
            var plan = Plan(parameters.ToLabel(), _getSelectedDevice());
            _composition.Stage(ComposeElementKind.Qr2D, Summary(plan, data), qr: parameters);
            _reportStatus(Strings.Status_AddedToComposition, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }
}
