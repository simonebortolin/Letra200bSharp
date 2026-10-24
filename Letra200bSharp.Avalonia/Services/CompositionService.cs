using System.Collections.ObjectModel;
using System.Text.Json;

namespace Letra200bSharp.Avalonia.Services;

/// <summary>Which tab a staged (or historical) compose element came from - drives which *HistoryParams field is populated and how it's re-rendered.</summary>
public enum ComposeElementKind
{
    Text,
    Barcode,
    Qr2D,
    Draw,
    Image
}

/// <summary>
/// One element staged for the Compose tab's multi-part label. Reuses each tab's own
/// *HistoryParams shape (so re-rendering an element is exactly what that tab's own reprint
/// already does) without reusing the History screen/list itself - this is a separate, ordered,
/// print-order list, not a repurposing of History's newest-first log.
/// </summary>
public sealed record ComposeElement(
    Guid Id,
    ComposeElementKind Kind,
    string Summary,
    byte[] ThumbnailPng,
    TextHistoryParams? TextParams = null,
    BarcodeHistoryParams? BarcodeParams = null,
    QrHistoryParams? QrParams = null,
    DrawHistoryParams? DrawParams = null,
    ImageHistoryParams? ImageParams = null);

/// <summary>
/// Holds the ordered list of elements currently staged for the Compose tab - same persisted-JSON
/// pattern as <see cref="PrintHistoryService"/>/<see cref="SymbolLibraryService"/>, so a
/// composition being built survives an app restart. Unlike print history, entries are appended at
/// the end (order is the print order) rather than inserted at the front, and nothing here is ever
/// pruned automatically.
/// </summary>
public sealed class CompositionService
{
    private readonly string _filePath;
    private readonly ILabelPrintingService _printing;

    public ObservableCollection<ComposeElement> Elements { get; } = new();

    public CompositionService(ILabelPrintingService printing)
    {
        _printing = printing;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Letra200bSharp");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "composition.json");
        Load();
    }

    /// <summary>
    /// One element as a part of a <see cref="ComposedLabel"/> - which lays every part out at the
    /// printable head height, whatever its own tab defaults to standalone, so they all line up.
    /// Draw and Image elements are already print-sized content and are composed as-is.
    /// </summary>
    public static Label ToLabel(TextHistoryParams? text, BarcodeHistoryParams? barcode, QrHistoryParams? qr, DrawHistoryParams? draw, ImageHistoryParams? image)
    {
        // TextHistoryParams.ToLabel also migrates a composition staged before independently-
        // toggleable Bold/Italic existed, the same way TextTabViewModel does for its own history.
        if (text != null)
        {
            return text.ToLabel(edgeToEdge: false);
        }

        if (barcode != null)
        {
            return barcode.ToLabel();
        }

        if (qr != null)
        {
            return qr.ToLabel();
        }

        if (draw != null)
        {
            return draw.ToLabel();
        }

        if (image != null)
        {
            return image.ToLabel();
        }

        throw new InvalidOperationException("This element has no renderable content.");
    }

    public static Label ToLabel(ComposeElement element) =>
        ToLabel(element.TextParams, element.BarcodeParams, element.QrParams, element.DrawParams, element.ImageParams);

    /// <summary>
    /// A staged element's thumbnail, built through the same compose path as the final print so it
    /// matches what actually gets printed for every element type.
    /// </summary>
    public byte[] RenderThumbnail(Label part) => _printing.RenderPreview(new ComposedLabel(new[] { part }));

    /// <summary>Renders the element's content and thumbnail, then appends it to the end of the staged list.</summary>
    public void Stage(ComposeElementKind kind, string summary, TextHistoryParams? text = null, BarcodeHistoryParams? barcode = null, QrHistoryParams? qr = null, DrawHistoryParams? draw = null, ImageHistoryParams? image = null)
    {
        var thumbnail = RenderThumbnail(ToLabel(text, barcode, qr, draw, image));
        Add(new ComposeElement(Guid.NewGuid(), kind, summary, thumbnail, text, barcode, qr, draw, image));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<ComposeElement>>(json);
            if (loaded == null)
            {
                return;
            }

            // Image elements staged by an earlier 1.5 pre-release build stored the source photo under a
            // different field and deserialize without content - drop them rather than fail later.
            foreach (var element in loaded.Where(e => e.Kind != ComposeElementKind.Image || e.ImageParams?.ContentPng != null))
            {
                Elements.Add(element);
            }
        }
        catch
        {
            // A corrupt/unreadable file shouldn't stop the app from starting - just start with an
            // empty (in-progress) composition instead.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Elements.ToList());
        File.WriteAllText(_filePath, json);
    }

    /// <summary>Appends an element to the end of the staged list - print order, not newest-first.</summary>
    public void Add(ComposeElement element)
    {
        Elements.Add(element);
        Save();
    }

    /// <summary>Inserts a copy of the element identified by <paramref name="id"/> right after it - its parameters and thumbnail are immutable, so the copy only needs a new id.</summary>
    public void Duplicate(Guid id)
    {
        int index = Elements.ToList().FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return;
        }

        Elements.Insert(index + 1, Elements[index] with { Id = Guid.NewGuid() });
        Save();
    }

    public void Remove(Guid id)
    {
        var element = Elements.FirstOrDefault(e => e.Id == id);
        if (element != null)
        {
            Elements.Remove(element);
            Save();
        }
    }

    /// <summary>Reorders the staged list by moving the element identified by <paramref name="id"/> to <paramref name="newIndex"/> - see the Compose tab's drag-and-drop handling.</summary>
    public void Move(Guid id, int newIndex)
    {
        var elements = Elements.ToList();
        int oldIndex = elements.FindIndex(e => e.Id == id);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Elements.Count - 1);
        if (newIndex != oldIndex)
        {
            Elements.Move(oldIndex, newIndex);
            Save();
        }
    }

    public void Clear()
    {
        Elements.Clear();
        Save();
    }

    /// <summary>Replaces the whole staged list at once - used when reloading a saved composition from history.</summary>
    public void SetAll(IEnumerable<ComposeElement> elements)
    {
        Elements.Clear();
        foreach (var element in elements)
        {
            Elements.Add(element);
        }
        Save();
    }
}
