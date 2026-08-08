using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class BarcodeTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;

    public ObservableCollection<string> Symbologies { get; } = new(Enum.GetNames<LetraHelper.BarcodeSymbology>());

    [ObservableProperty]
    public partial string Data { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedSymbology { get; set; } = nameof(LetraHelper.BarcodeSymbology.Code128);

    [ObservableProperty]
    public partial bool NoCut { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public BarcodeTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
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

        try
        {
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewImage(data, symbology, noCut);
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
        var data = Data;
        if (string.IsNullOrEmpty(data))
        {
            _reportStatus("No barcode data entered.", true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus("No device selected.", true);
            return;
        }

        var symbology = Enum.Parse<LetraHelper.BarcodeSymbology>(SelectedSymbology);
        var noCut = NoCut;

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(data, symbology, noCut));
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
