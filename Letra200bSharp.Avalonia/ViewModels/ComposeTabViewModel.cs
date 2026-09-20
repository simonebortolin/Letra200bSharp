using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
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
/// segments, generalized to any element type (see <see cref="LetraHelper.CreateComposedJob"/>).
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

        Elements.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasElements));
    }

    /// <summary>Reorders the staged list by moving <paramref name="element"/> to <paramref name="newIndex"/> - see <see cref="Views.ComposeTabView"/>'s drag-and-drop handling (same pattern as DIN Rail's row list).</summary>
    public void MoveElement(ComposeElement element, int newIndex) => _composition.Move(element.Id, newIndex);

    [RelayCommand]
    private void RemoveElement(ComposeElement element) => _composition.Remove(element.Id);

    [RelayCommand]
    private void ClearAll() => _composition.Clear();

    /// <summary>Restores a previously printed composition (see <see cref="Services.HistoryEntry.ComposeParams"/>), replacing whatever was staged, and refreshes the preview so the user can see what they're about to reprint.</summary>
    public void LoadFrom(ComposeHistoryParams parameters)
    {
        var elements = parameters.Elements.Select(ep => new ComposeElement(
            Guid.NewGuid(),
            Enum.Parse<ComposeElementKind>(ep.Kind),
            ep.Summary,
            RenderElementThumbnail(ep.TextParams, ep.BarcodeParams, ep.QrParams, ep.DrawParams, ep.ImageParams),
            ep.TextParams,
            ep.BarcodeParams,
            ep.QrParams,
            ep.DrawParams,
            ep.ImageParams));
        _composition.SetAll(elements);

        PreviewCommand.Execute(null);
    }

    /// <summary>
    /// Renders one element's raw printable content (no quiet margin/scaling), always at
    /// <c>noCut: false</c> regardless of what its own tab defaults to standalone (Text and DIN
    /// Rail render at 32 dots tall there; this tab renders everything at 30 so every element
    /// lines up at the same height once concatenated - see <see cref="LetraHelper.CreateComposedJob"/>).
    /// </summary>
    private byte[] RenderElementImage(TextHistoryParams? text, BarcodeHistoryParams? barcode, QrHistoryParams? qr, DrawHistoryParams? draw, ImageHistoryParams? image = null)
    {
        if (text is { } t)
        {
            // Mirrors TextTabViewModel.ComposedText/Line2Enabled - a second line only makes sense
            // with a size/style that leaves room for it.
            bool line2Enabled = t.Style != nameof(LetraHelper.TextStyle.Vertical) && t.Size != "L" && t.Size != "XL";
            string composedText = line2Enabled && !string.IsNullOrEmpty(t.Line2) ? t.Line1 + Environment.NewLine + t.Line2 : t.Line1;
            return _render.RenderTextContentImage(
                composedText,
                t.FontFamily ?? "Arial",
                Enum.Parse<LetraHelper.LabelTextSize>(t.Size),
                Enum.Parse<LetraHelper.TextStyle>(t.Style),
                t.UpperCase,
                (float)t.WidthScale,
                Enum.Parse<LetraHelper.TextBoxStyle>(t.BoxStyle),
                Enum.Parse<LetraHelper.TextAlign>(t.Align),
                noCut: false);
        }

        if (barcode is { } b)
        {
            var caption = new LetraHelper.CaptionOptions(
                Enum.Parse<LetraHelper.CaptionPosition>(b.CaptionPosition),
                b.CaptionFontFamily,
                Enum.Parse<LetraHelper.CaptionSize>(b.CaptionSize),
                Enum.Parse<LetraHelper.TextAlign>(b.CaptionAlign));
            return _render.RenderBarcodeContentImage(b.Data, Enum.Parse<LetraHelper.BarcodeSymbology>(b.Symbology), caption, noCut: false);
        }

        if (qr is { } q)
        {
            return _render.RenderTwoDContentImage(q.Data, Enum.Parse<LetraHelper.TwoDSymbology>(q.Symbology));
        }

        if (draw is { } d)
        {
            // Already exactly this shape - the Draw tab's canvas is always 30 dots tall.
            return d.Png;
        }

        if (image is { } i)
        {
            // Forced noCut:false regardless of what the Image tab's own checkbox said when this
            // was staged, same as every other element type - see the class remarks above.
            return _render.RenderImageContentImage(i.ImageBytes, i.PreRendered, noCut: false);
        }

        throw new InvalidOperationException("This element has no renderable content.");
    }

    private byte[] RenderElementThumbnail(TextHistoryParams? text, BarcodeHistoryParams? barcode, QrHistoryParams? qr, DrawHistoryParams? draw, ImageHistoryParams? image = null) =>
        _render.PreviewImage(RenderElementImage(text, barcode, qr, draw, image), noCut: false, preRendered: true);

    private byte[][] RenderAllElements(IReadOnlyList<ComposeElement> elements) =>
        elements.Select(e => RenderElementImage(e.TextParams, e.BarcodeParams, e.QrParams, e.DrawParams, e.ImageParams)).ToArray();

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!HasElements)
        {
            var previousBitmap = PreviewBitmap;
            PreviewBitmap = null;
            previousBitmap?.Dispose();
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

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();
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
            var job = await Task.Run(() => _letra.CreateComposedJob(RenderAllElements(elements)));
            var result = await LetraPrinter.PrintAsync(device, job);
            _reportStatus(result.Message, !result.Printed);
            _recordStats(result);

            if (result.Printed)
            {
                try
                {
                    RecordHistory(elements, printed: true);
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
            RecordHistory(Elements.ToList(), printed: false);
            _reportStatus(Strings.Status_SavedToHistory, false);
        }
        catch (Exception ex)
        {
            _reportStatus(ex.Message, true);
        }
    }

    private void RecordHistory(IReadOnlyList<ComposeElement> elements, bool printed)
    {
        var thumbnail = _render.PreviewComposedImage(RenderAllElements(elements));
        var elementParams = elements
            .Select(e => new ComposeElementParams(e.Kind.ToString(), e.Summary, e.TextParams, e.BarcodeParams, e.QrParams, e.DrawParams, e.ImageParams))
            .ToList();
        var parameters = new ComposeHistoryParams(elementParams);
        var summary = $"{elements.Count} element{(elements.Count == 1 ? "" : "s")}: " + string.Join(" | ", elements.Select(e => e.Summary));
        _historyService.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, HistoryKind.Compose, summary, thumbnail, ComposeParams: parameters, Printed: printed));
    }
}
