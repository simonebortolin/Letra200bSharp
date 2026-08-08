using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

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

    [ObservableProperty]
    public partial bool IsStatusVisible { get; set; }

    private static readonly TimeSpan StatusDisplayDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ErrorStatusDisplayDuration = TimeSpan.FromSeconds(7);

    private CancellationTokenSource? _statusHideCts;

    public ImageTabViewModel Image { get; }
    public TextTabViewModel Text { get; }
    public BarcodeTabViewModel Barcode { get; }

    public MainViewModel()
    {
        Image = new ImageTabViewModel(() => SelectedDevice, ReportStatus);
        Text = new TextTabViewModel(() => SelectedDevice, ReportStatus);
        Barcode = new BarcodeTabViewModel(() => SelectedDevice, ReportStatus);

        _ = RefreshDevicesAsync();
    }

    /// <summary>Shows <paramref name="message"/> as a toast, then hides it again on its own
    /// after <see cref="StatusDisplayDuration"/> (or <see cref="ErrorStatusDisplayDuration"/>
    /// for errors, since those are worth a little longer to read).</summary>
    private void ReportStatus(string message, bool isError)
    {
        _statusHideCts?.Cancel();
        var cts = new CancellationTokenSource();
        _statusHideCts = cts;

        StatusMessage = message;
        IsStatusError = isError;
        IsStatusVisible = true;

        _ = HideStatusAfterDelayAsync(isError ? ErrorStatusDisplayDuration : StatusDisplayDuration, cts.Token);
    }

    private async Task HideStatusAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            IsStatusVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer status message; that one owns hiding the toast now.
        }
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
