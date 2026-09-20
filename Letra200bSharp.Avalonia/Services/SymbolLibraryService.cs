using System.Collections.ObjectModel;
using System.Text.Json;

namespace Letra200bSharp.Avalonia.Services;

/// <summary>
/// One reusable hand-drawn symbol saved from the Draw tab - <see cref="PixelPng"/> is a
/// pixel-exact, 1-bit-per-pixel PNG already sized to the printer's head axis (30 rows) with the
/// blank columns on each side trimmed off, so it's directly usable with
/// <see cref="Letra200bSharp.LetraHelper.CreateJob(byte[], bool, bool)"/>'s <c>preRendered</c>
/// path - no re-encoding needed to print or reload it.
/// </summary>
public sealed record SavedSymbol(Guid Id, string Name, DateTimeOffset Timestamp, int WidthDots, byte[] PixelPng);

/// <summary>
/// Persists the user's personal library of hand-drawn symbols (see <see cref="ViewModels.DrawTabViewModel"/>)
/// as JSON, the same load/save/mutate-the-live-collection pattern as <see cref="PrintHistoryService"/>
/// - unlike print history, nothing here is ever pruned automatically, since these are
/// deliberately curated, reusable building blocks rather than a rolling log.
/// </summary>
public sealed class SymbolLibraryService
{
    private readonly string _filePath;

    public ObservableCollection<SavedSymbol> Symbols { get; } = new();

    public SymbolLibraryService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Letra200bSharp");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "symbols.json");
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
            var loaded = JsonSerializer.Deserialize<List<SavedSymbol>>(json);
            if (loaded == null)
            {
                return;
            }

            foreach (var symbol in loaded)
            {
                Symbols.Add(symbol);
            }
        }
        catch
        {
            // A corrupt/unreadable library file shouldn't stop the app from starting - just
            // start with an empty library instead.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Symbols);
        File.WriteAllText(_filePath, json);
    }

    public void Add(SavedSymbol symbol)
    {
        Symbols.Insert(0, symbol);
        Save();
    }

    public void Remove(Guid id)
    {
        var symbol = Symbols.FirstOrDefault(s => s.Id == id);
        if (symbol != null)
        {
            Symbols.Remove(symbol);
            Save();
        }
    }

    /// <summary>Looks up a symbol by name (case-insensitive, first match) - used by the CLI's <c>print-symbol</c> verb.</summary>
    public SavedSymbol? FindByName(string name) =>
        Symbols.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
