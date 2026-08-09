using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class BarcodeTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;

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

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    public BarcodeTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
    }

    /// <summary>Restores a previously printed barcode (see <see cref="Services.HistoryEntry.BarcodeParams"/>) and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(BarcodeHistoryParams parameters)
    {
        Data = parameters.Data;
        SelectedSymbology = parameters.Symbology;
        NoCut = parameters.NoCut;

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

        try
        {
            IsPreviewLoading = true;
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

            if (result.Printed)
            {
                RecordHistory(data, symbology, noCut);
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
    /// Best-effort: a history-recording failure must never look like the print itself failed,
    /// so this is never allowed to bubble into <see cref="PrintAsync"/>'s own error reporting.
    /// </summary>
    private void RecordHistory(string data, LetraHelper.BarcodeSymbology symbology, bool noCut)
    {
        try
        {
            var thumbnail = LetraHelper.PreviewImage(data, symbology, noCut);
            var parameters = new BarcodeHistoryParams(data, SelectedSymbology, noCut);
            _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Barcode, $"{SelectedSymbology}: {data}", thumbnail, BarcodeParams: parameters));
        }
        catch
        {
            // See summary above.
        }
    }
}
