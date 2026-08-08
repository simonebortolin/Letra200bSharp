using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class ImageTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;

    /// <summary>
    /// Wired up by the view (needs a TopLevel to show a native file picker), since the
    /// ViewModel itself has no platform/visual-tree access. Returns the picked file's
    /// display name plus its already-read bytes: on Android (and other sandboxed
    /// platforms) the picker hands back a content:// URI rather than a real filesystem
    /// path, so the view reads it once via the storage API's stream instead of a path
    /// the ViewModel could later feed to <see cref="File"/>.
    /// </summary>
    public Func<Task<(string Name, byte[] Bytes)?>>? PickFileAsync { get; set; }

    private byte[]? _imageBytes;

    [ObservableProperty]
    public partial string? ImagePath { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoCutEnabled))]
    public partial bool PreRendered { get; set; }

    [ObservableProperty]
    public partial bool NoCut { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// "No cut" only makes sense for an image that was already deliberately sized for the
    /// printer's full 32px head resolution - i.e. "Pre-rendered" - so it stays disabled
    /// (and unchecked) otherwise.
    /// </summary>
    public bool NoCutEnabled => PreRendered;

    public ImageTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
    }

    partial void OnPreRenderedChanged(bool value)
    {
        if (!value)
        {
            NoCut = false;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickFileAsync == null)
        {
            return;
        }

        var picked = await PickFileAsync();
        if (picked == null)
        {
            return;
        }

        _imageBytes = picked.Value.Bytes;
        ImagePath = picked.Value.Name;
        await UpdatePreviewAsync();
    }

    private async Task UpdatePreviewAsync()
    {
        if (_imageBytes == null)
        {
            return;
        }

        var imageBytes = _imageBytes;
        var noCut = NoCut;
        var preRendered = PreRendered;
        try
        {
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = LetraHelper.PreviewImage(imageBytes, noCut, preRendered);
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            var previousBitmap = PreviewBitmap;
            PreviewBitmap = bitmap;
            previousBitmap?.Dispose();
        }
        catch (Exception ex)
        {
            _reportStatus($"Unable to generate preview: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (_imageBytes == null)
        {
            _reportStatus("No image file selected.", true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus("No device selected.", true);
            return;
        }

        var imageBytes = _imageBytes;
        var noCut = NoCut;
        var preRendered = PreRendered;

        IsBusy = true;
        try
        {
            var job = await Task.Run(() => LetraHelper.CreateJob(imageBytes, noCut, preRendered));

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
