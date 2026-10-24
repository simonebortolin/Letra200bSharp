using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Toasts;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>Indexes of the TabControl's tabs in <c>MainView.axaml</c> - used by <see cref="HistoryTabViewModel"/> to jump to the right tab after "Reprint".</summary>
    public const int ImageTabIndex = 0;
    public const int TextTabIndex = 1;
    public const int BarcodeTabIndex = 2;
    public const int QrTabIndex = 3;
    public const int DrawTabIndex = 4;
    public const int DinRailTabIndex = 5;
    public const int ComposeTabIndex = 6;
    public const int HistoryTabIndex = 7;
    public const int AboutTabIndex = 8;

    public ObservableCollection<PrinterInfo> Devices { get; } = new();

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial PrinterInfo? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    /// <summary>Bound to the <c>SukiToastHost</c> in <c>MainView.axaml</c> so status messages
    /// (see <see cref="ReportStatus"/>) show up as SukiUI toasts instead of a custom control.</summary>
    public ISukiToastManager ToastManager { get; } = new SukiToastManager();

    [ObservableProperty]
    public partial bool IsStatsPanelVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsDisplayText))]
    public partial string? LastPrintKind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsDisplayText))]
    public partial PrintResult? LastPrintResult { get; set; }

    /// <summary>
    /// A YouTube "stats for nerds"-style dump of the last print attempt's protocol-level numbers
    /// (see <see cref="PrintStatistics"/>) - purely for curiosity, not shown anywhere else.
    /// </summary>
    public string? StatsDisplayText
    {
        get
        {
            if (LastPrintResult is not { Statistics: { } stats } result)
            {
                return null;
            }

            var lines = new List<string>
            {
                $"Job:          {LastPrintKind}",
                $"Result:       {(result.Printed ? "printed" : "failed")} (status {result.StatusCode?.ToString() ?? "none"})",
                $"Bytes sent:   {stats.TotalBytes} B across {stats.PacketCount} packet{(stats.PacketCount == 1 ? "" : "s")}",
                $"Elapsed:      {stats.Elapsed.TotalMilliseconds:0} ms",
            };
            lines.AddRange(stats.Details.Select(detail => $"{(detail.Key + ":").PadRight(13)} {detail.Value}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private readonly PrintHistoryService _historyService = new();
    private readonly SymbolLibraryService _symbolLibrary = new();
    private readonly ILabelPrintingService _printing;
    private readonly CompositionService _compositionService;

    public ImageTabViewModel Image { get; }
    public TextTabViewModel Text { get; }
    public BarcodeTabViewModel Barcode { get; }
    public QrTabViewModel Qr { get; }
    public DrawTabViewModel Draw { get; }
    public DinRailTabViewModel DinRail { get; }
    public ComposeTabViewModel Compose { get; }
    public HistoryTabViewModel History { get; }
    public AboutTabViewModel About { get; }

    public MainViewModel()
    {
        // Every supported printer is a driver registered here - see LetraBuilder.AddDriver for
        // adding one that isn't packaged as its own Add...() extension.
        _printing = LabelPrinting.Create(letra => letra.AddLetraTag200b());
        _compositionService = new CompositionService(_printing);

        Image = new ImageTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _compositionService, _printing, result => RecordPrintStats("Image", result));
        Text = new TextTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _compositionService, _printing, result => RecordPrintStats("Text", result));
        Barcode = new BarcodeTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _compositionService, _printing, result => RecordPrintStats("Barcode", result));
        Qr = new QrTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _compositionService, _printing, result => RecordPrintStats("2D Code", result));
        Draw = new DrawTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _symbolLibrary, _compositionService, _printing, result => RecordPrintStats("Draw", result));
        DinRail = new DinRailTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _printing, result => RecordPrintStats("DinRail", result));
        Compose = new ComposeTabViewModel(() => SelectedDevice, ReportStatus, _historyService, _compositionService, _printing, result => RecordPrintStats("Compose", result));
        History = new HistoryTabViewModel(_historyService, Text, Barcode, Qr, Draw, DinRail, Compose, index => SelectedTabIndex = index);
        About = new AboutTabViewModel();

        _ = RefreshDevicesAsync();
    }

    /// <summary>
    /// Updates the "stats for nerds" panel with the outcome of any print attempt that got far
    /// enough to produce <see cref="PrintStatistics"/> - including failed ones (a timeout or a
    /// bad status code is exactly the kind of thing worth seeing the raw numbers for).
    /// </summary>
    private void RecordPrintStats(string kind, PrintResult result)
    {
        if (result.Statistics == null)
        {
            return;
        }

        LastPrintKind = kind;
        LastPrintResult = result;
    }

    /// <summary>Shows <paramref name="message"/> as a SukiUI toast - errors get the Error
    /// styling and stick around a bit longer, since those are worth a little longer to read.</summary>
    private void ReportStatus(string message, bool isError)
    {
        ToastManager.CreateToast()
            .WithTitle(isError ? Strings.Toast_ErrorTitle : Strings.Toast_SuccessTitle)
            .WithContent(message)
            .OfType(isError ? NotificationType.Error : NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(isError ? 7 : 4))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        IsRefreshing = true;
        try
        {
            Devices.Clear();

            // With the radio off (or no adapter at all) a scan either just comes back empty -
            // indistinguishable from "printer not nearby" - or, on Windows, throws a bare
            // "Element not found", so check up front and say what's actually wrong.
            if (!await _printing.IsAnyDriverAvailableAsync())
            {
                ReportStatus(Strings.MainView_BluetoothUnavailable, true);
                return;
            }

            var devices = await _printing.DiscoverAsync();
            foreach (var device in devices)
            {
                if (!Devices.Any(it => it.Id == device.Id))
                {
                    Devices.Add(device);
                }
            }

            if (devices.Count == 0)
            {
                ReportStatus(Strings.MainView_NoPrinterFound, true);
            }
        }
        catch (Exception ex)
        {
            // The radio may have been switched off mid-scan - same story as above.
            bool available;
            try
            {
                available = await _printing.IsAnyDriverAvailableAsync();
            }
            catch
            {
                available = false;
            }

            ReportStatus(available ? ex.Message : Strings.MainView_BluetoothUnavailable, true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
