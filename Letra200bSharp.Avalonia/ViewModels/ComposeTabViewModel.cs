using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InTheHand.Bluetooth;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Resources;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Avalonia.ViewModels;

/// <summary>
/// Concatenates several already-configured elements - added here from Text/Barcode/2D
/// Code/Draw/Image's own "Concatenate" command, not composed from scratch in this tab - into one
/// continuous printed strip along the feed axis, the same idea DIN Rail already uses for text
/// segments, generalized to any element type (see <see cref="ILetraHelper.CreateComposedJob"/>).
/// The staging list itself lives in <see cref="CompositionService"/>, not here or in
/// <see cref="Services.PrintHistoryService"/> - a separate, ordered, print-order list. DIN Rail
/// itself is deliberately not composable (it's already its own multi-segment strip).
/// </summary>
public partial class ComposeTabViewModel : ViewModelBase
{
    private readonly Func<BluetoothDevice?> _getSelectedDevice;
    private readonly Action<string, bool> _reportStatus;
    private readonly PrintHistoryService _historyService;
    private readonly CompositionService _composition;
    private readonly IRenderHelper _render;
    private readonly ILetraHelper _letra;
    private readonly Action<LetraPrintResult> _recordStats;

    /// <summary>Bumped on every preview request, so a slower, older render finishing late never overwrites a newer one.</summary>
    private int _previewVersion;

    private bool _previewRefreshQueued;

    public ObservableCollection<ComposeElement> Elements => _composition.Elements;

    public bool HasElements => Elements.Count > 0;

    [ObservableProperty]
    public partial Bitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; set; }

    public ComposeTabViewModel(Func<BluetoothDevice?> getSelectedDevice, Action<string, bool> reportStatus, PrintHistoryService historyService, CompositionService composition, IRenderHelper render, ILetraHelper letra, Action<LetraPrintResult> recordStats)
    {
        _getSelectedDevice = getSelectedDevice;
        _reportStatus = reportStatus;
        _historyService = historyService;
        _composition = composition;
        _render = render;
        _letra = letra;
        _recordStats = recordStats;

        Elements.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasElements));
            SchedulePreviewRefresh();
        };
    }

    /// <summary>
    /// Keeps the preview in sync with the staged list (add from another tab, remove, reorder,
    /// clear, reload from history). Posted rather than run inline so a bulk change like
    /// <see cref="CompositionService.SetAll"/> - one CollectionChanged per item - renders once,
    /// against the final list.
    /// </summary>
    private void SchedulePreviewRefresh()
    {
        if (_previewRefreshQueued)
        {
            return;
        }

        _previewRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _previewRefreshQueued = false;
            PreviewCommand.Execute(null);
        });
    }

    /// <summary>Reorders the staged list by moving <paramref name="element"/> to <paramref name="newIndex"/> - see <see cref="Views.ComposeTabView"/>'s drag-and-drop handling (same pattern as DIN Rail's row list).</summary>
    public void MoveElement(ComposeElement element, int newIndex) => _composition.Move(element.Id, newIndex);

    [RelayCommand]
    private void DuplicateElement(ComposeElement element) => _composition.Duplicate(element.Id);

    [RelayCommand]
    private void RemoveElement(ComposeElement element) => _composition.Remove(element.Id);

    [RelayCommand]
    private void ClearAll() => _composition.Clear();

    /// <summary>
    /// Restores a previously printed composition (see <see cref="Services.HistoryEntry.ComposeParams"/>),
    /// replacing whatever was staged. Every element is rendered before anything is replaced, so an
    /// entry that can't be rendered anymore (e.g. a missing font) is reported and leaves the
    /// current staged list untouched instead of clearing it or crashing the Reprint command.
    /// </summary>
    public void LoadFrom(ComposeHistoryParams parameters)
    {
        List<ComposeElement> elements;
        try
        {
            elements = parameters.Elements.Select(ep => new ComposeElement(
                Guid.NewGuid(),
                Enum.Parse<ComposeElementKind>(ep.Kind),
                ep.Summary,
                _composition.RenderThumbnail(_composition.RenderElementImage(ep.TextParams, ep.BarcodeParams, ep.QrParams, ep.DrawParams, ep.ImageParams)),
                ep.TextParams,
                ep.BarcodeParams,
                ep.QrParams,
                ep.DrawParams,
                ep.ImageParams)).ToList();
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
            return;
        }

        _composition.SetAll(elements);
    }

    private byte[][] RenderAllElements(IReadOnlyList<ComposeElement> elements) =>
        elements.Select(_composition.RenderElementImage).ToArray();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PreviewAsync()
    {
        int version = ++_previewVersion;

        if (!HasElements)
        {
            var previousBitmap = PreviewBitmap;
            PreviewBitmap = null;
            previousBitmap?.Dispose();
            IsPreviewLoading = false;
            return;
        }

        var elements = Elements.ToList();

        try
        {
            IsPreviewLoading = true;
            var bitmap = await Task.Run(() =>
            {
                var previewBytes = _render.PreviewComposedImage(RenderAllElements(elements));
                using var stream = new MemoryStream(previewBytes);
                return new Bitmap(stream);
            });

            if (version != _previewVersion)
            {
                bitmap.Dispose();
                return;
            }

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            if (version == _previewVersion)
            {
                _reportStatus(string.Format(Strings.Status_UnableToGeneratePreview, ex.Message), true);
            }
        }
        finally
        {
            if (version == _previewVersion)
            {
                IsPreviewLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (!HasElements)
        {
            _reportStatus(Strings.ComposeTab_NoElements, true);
            return;
        }

        var device = _getSelectedDevice();
        if (device == null)
        {
            _reportStatus(Strings.Status_NoDeviceSelected, true);
            return;
        }

        var elements = Elements.ToList();

        IsBusy = true;
        try
        {
            var elementPngs = await Task.Run(() => RenderAllElements(elements));
            var job = await Task.Run(() => _letra.CreateComposedJob(elementPngs));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(elements, elementPngs, printed: true);
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
    /// Saves the current composition to history without printing it - lets a design be
    /// kept/reused (see <see cref="Services.HistoryEntry.Printed"/>) even without a printer in
    /// reach. Unlike the best-effort recording after a successful print, a failure here is the
    /// whole point of the action, so it's reported to the user instead of swallowed.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (!HasElements)
        {
            _reportStatus(Strings.ComposeTab_NoElements, true);
            return;
        }

        try
        {
            var elements = Elements.ToList();
            RecordHistory(elements, RenderAllElements(elements), printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    /// <summary>Takes the already-rendered element content so a print doesn't render everything twice.</summary>
    private void RecordHistory(IReadOnlyList<ComposeElement> elements, IReadOnlyList<byte[]> elementPngs, bool printed)
    {
        var thumbnail = _render.PreviewComposedImage(elementPngs);
        var elementParams = elements
            .Select(e => new ComposeElementParams(e.Kind.ToString(), e.Summary, e.TextParams, e.BarcodeParams, e.QrParams, e.DrawParams, e.ImageParams))
            .ToList();
        var parameters = new ComposeHistoryParams(elementParams);
        var summary = $"{elements.Count} element{(elements.Count == 1 ? "" : "s")}: " + string.Join(" | ", elements.Select(e => e.Summary));
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Compose, summary, thumbnail, ComposeParams: parameters, Printed: printed));
    }
}
