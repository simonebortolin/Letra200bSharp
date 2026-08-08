using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using letra200bsharp;

namespace letra200bsharp.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<BluetoothDevice> Devices { get; } = new();

    [ObservableProperty]
    public partial BluetoothDevice? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsStatusError { get; set; }

    public ImageTabViewModel Image { get; }
    public TextTabViewModel Text { get; }

    public MainViewModel()
    {
        Image = new ImageTabViewModel(() => SelectedDevice, ReportStatus);
        Text = new TextTabViewModel(() => SelectedDevice, ReportStatus);

        _ = RefreshDevicesAsync();
    }

    private void ReportStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
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
