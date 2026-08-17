using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;
using SukiUI.Toasts;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>Indexes of the TabControl's tabs in <c>MainView.axaml</c> - used by <see cref="HistoryTabViewModel"/> to jump to the right tab after "Reprint".</summary>
    public const int ImageTabIndex = 0;
    public const int TextTabIndex = 1;
    public const int BarcodeTabIndex = 2;
    public const int HistoryTabIndex = 3;
    public const int AboutTabIndex = 4;

    public ObservableCollection<BluetoothDevice> Devices { get; } = new();

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial BluetoothDevice? SelectedDevice { get; set; }

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
    public partial LetraPrintResult? LastPrintResult { get; set; }

    /// <summary>
    /// A YouTube "stats for nerds"-style dump of the last print attempt's protocol-level numbers
    /// (see <see cref="LetraPrintStats"/>) - purely for curiosity, not shown anywhere else.
    /// </summary>
    public string? StatsDisplayText
    {
        get
        {
            if (LastPrintResult is not { Stats: { } stats } result)
            {
                return null;
            }

            return string.Join(Environment.NewLine,
                $"Job:          {LastPrintKind}",
                $"Result:       {(result.Printed ? "printed" : "failed")} (status {result.StatusCode?.ToString() ?? "none"})",
                $"Bytes sent:   {stats.TotalBytes} B across {stats.PacketCount} packet{(stats.PacketCount == 1 ? "" : "s")}",
                $"Elapsed:      {stats.Elapsed.TotalMilliseconds:0} ms",
                $"Service UUID: {stats.ServiceUuid}",
                $"MTU:          {stats.RequestedMtu} requested, {(stats.MtuNegotiated ? "negotiated" : "not confirmed")}");
        }
    }

    private readonly PrintHistoryService _historyService = new();

    public ImageTabViewModel Image { get; }
    public TextTabViewModel Text { get; }
    public BarcodeTabViewModel Barcode { get; }
    public HistoryTabViewModel History { get; }
    public AboutTabViewModel About { get; }

    public MainViewModel()
    {
        Image = new ImageTabViewModel(() => SelectedDevice, ReportStatus, _historyService, result => RecordPrintStats("Image", result));
        Text = new TextTabViewModel(() => SelectedDevice, ReportStatus, _historyService, result => RecordPrintStats("Text", result));
        Barcode = new BarcodeTabViewModel(() => SelectedDevice, ReportStatus, _historyService, result => RecordPrintStats("Barcode", result));
        History = new HistoryTabViewModel(_historyService, Text, Barcode, index => SelectedTabIndex = index);
        About = new AboutTabViewModel();

        _ = RefreshDevicesAsync();
    }

    /// <summary>
    /// Updates the "stats for nerds" panel with the outcome of any print attempt that got far
    /// enough to produce <see cref="LetraPrintStats"/> - including failed ones (a timeout or a
    /// bad status code is exactly the kind of thing worth seeing the raw numbers for).
    /// </summary>
    private void RecordPrintStats(string kind, LetraPrintResult result)
    {
        if (result.Stats == null)
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
            var devices = await LetraPrinter.ScanForDevicesAsync();
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
            ReportStatus(ex.Message, true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
