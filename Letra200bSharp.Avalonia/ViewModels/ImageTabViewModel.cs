using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

public partial class ImageTabViewModel : ViewModelBase
{
    private readonly Func<PrinterInfo?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly CompositionService _composition;
    private readonly ILabelPrintingService _printing;
    private readonly Action<PrintResult> _recordStats;

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

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    /// <summary>
    /// "No cut" only makes sense for an image that was already deliberately sized for the
    /// printer's full 32px head resolution - i.e. "Pre-rendered" - so it stays disabled
    /// (and unchecked) otherwise.
    /// </summary>
    public bool NoCutEnabled => PreRendered;

    public ImageTabViewModel(Func<PrinterInfo?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, CompositionService composition, ILabelPrintingService printing, Action<PrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _composition = composition;
        _printing = printing;
        _recordStats = recordStats;
    }

    private static ImageLabel BuildLabel(byte[] imageBytes, bool noCut, bool preRendered) =>
        new(imageBytes) { PreRendered = preRendered, EdgeToEdge = noCut };

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

        var label = BuildLabel(_imageBytes, NoCut, PreRendered);
        var printer = _getSelectedDevice();
        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = _printing.RenderPreview(label, printer);
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            var previousBitmap = PreviewBitmap;
            PreviewBitmap = bitmap;
            previousBitmap?.Dispose();
        }
        catch (Exception ex)
        {
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
        if (_imageBytes == null)
        {
            _reportStatus(Strings.ImageTab_NoImageSelected, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var imageBytes = _imageBytes;
        var noCut = NoCut;
        var preRendered = PreRendered;

        IsBusy = true;
        try
        {
            var result = await _printing.PrintAsync(device, BuildLabel(imageBytes, noCut, preRendered));
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(imageBytes, noCut, preRendered, printed: true);
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
    /// Saves the current image to history without printing it - lets a design be kept/reused
    /// (see <see cref="Services.HistoryEntry.Printed"/>) even without a printer in reach. Unlike
    /// the best-effort recording after a successful print, a failure here is the whole point of
    /// the action, so it's reported to the user instead of swallowed. No reprint parameters are
    /// kept for images either way - see <see cref="Services.HistoryEntry"/>.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (_imageBytes == null)
        {
            _reportStatus(Strings.ImageTab_NoImageSelected, true);
            return;
        }

        try
        {
            RecordHistory(_imageBytes, NoCut, PreRendered, printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    private void RecordHistory(byte[] imageBytes, bool noCut, bool preRendered, bool printed)
    {
        var thumbnail = _printing.RenderPreview(BuildLabel(imageBytes, noCut, preRendered), _getSelectedDevice());
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Image, ImagePath ?? Strings.ImageTab_DefaultHistoryLabel, thumbnail, Printed: printed));
    }

    /// <summary>
    /// Adds the current image to the Compose tab's staging list (see <see cref="CompositionService"/>)
    /// - lets it become one part of a longer, multi-element printed strip. Only the already
    /// thresholded, print-sized content is kept (see <see cref="ImageHistoryParams"/>), never the
    /// source photo.
    /// </summary>
    [RelayCommand]
    private void Concatenate()
    {
        if (_imageBytes == null)
        {
            _reportStatus(Strings.ImageTab_NoImageSelected, true);
            return;
        }

        try
        {
            // Forced noCut:false, like every other element type, so this lines up at the same
            // height as the rest of the composition regardless of what's checked on this tab.
            var content = _printing.GetRenderer(_getSelectedDevice()).RenderContent(BuildLabel(_imageBytes, noCut: false, PreRendered));
            var parameters = new ImageHistoryParams(content);
            _composition.Stage(ComposeElementKind.Image, ImagePath ?? Strings.ImageTab_DefaultHistoryLabel, image: parameters);
            _reportStatus(Strings.Status_AddedToComposition, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }
}
