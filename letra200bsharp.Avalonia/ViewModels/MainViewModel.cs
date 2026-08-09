using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
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

    private readonly PrintHistoryService _historyService = new();

    public ImageTabViewModel Image { get; }
    public TextTabViewModel Text { get; }
    public BarcodeTabViewModel Barcode { get; }
    public HistoryTabViewModel History { get; }

    public MainViewModel()
    {
        Image = new ImageTabViewModel(() => SelectedDevice, ReportStatus, _historyService);
        Text = new TextTabViewModel(() => SelectedDevice, ReportStatus, _historyService);
        Barcode = new BarcodeTabViewModel(() => SelectedDevice, ReportStatus, _historyService);
        History = new HistoryTabViewModel(_historyService, Text, Barcode, index => SelectedTabIndex = index);

        _ = RefreshDevicesAsync();
    }

    /// <summary>Shows <paramref name="message"/> as a SukiUI toast - errors get the Error
    /// styling and stick around a bit longer, since those are worth a little longer to read.</summary>
    private void ReportStatus(string message, bool isError)
    {
        ToastManager.CreateToast()
            .WithTitle(isError ? "Error" : "Success")
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
                ReportStatus("Dymo LetraTag 200B not found.", true);
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
