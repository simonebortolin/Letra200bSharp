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

    public ObservableCollection<ComposeElement> Elements { get; } = new();

    public CompositionService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Letra200bSharp");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "composition.json");
        Load();
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

            foreach (var element in loaded)
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
