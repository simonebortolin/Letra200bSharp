using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Letra200bSharp.Avalonia.Services;

public enum HistoryKind
{
    Image,
    Text,
    Barcode,
    DinRail
}

/// <summary>Enough of a Text tab's state to restore it and let the user reprint - see <see cref="ViewModels.TextTabViewModel.LoadFrom"/>.</summary>
public sealed record TextHistoryParams(
    string Line1,
    string? Line2,
    string? FontFamily,
    string Size,
    string Style,
    decimal WidthScale,
    string BoxStyle,
    bool UpperCase,
    string Align = "Left");

/// <summary>Enough of a Barcode tab's state to restore it and let the user reprint - see <see cref="ViewModels.BarcodeTabViewModel.LoadFrom"/>.</summary>
public sealed record BarcodeHistoryParams(string Data, string Symbology, bool NoCut, bool ShowNumber = false);

/// <summary>One row of a DIN Rail strip - see <see cref="DinRailHistoryParams"/>.</summary>
public sealed record DinRailRowParams(string Text, decimal Modules);

/// <summary>Enough of a DIN Rail tab's state to restore it and let the user reprint - see <see cref="ViewModels.DinRailTabViewModel.LoadFrom"/>.</summary>
public sealed record DinRailHistoryParams(
    IReadOnlyList<DinRailRowParams> Rows,
    string? FontFamily,
    string Style,
    bool UpperCase,
    string Align,
    string Sizing,
    bool ShowSeparators);

/// <summary>
/// One past print job. <see cref="ThumbnailPng"/> is the same PNG bytes <see cref="Letra200bSharp.LetraHelper.PreviewImage(byte[], bool, bool)"/>
/// already produces for the tab's live preview, so it stays tiny. Only Text, Barcode and DIN
/// Rail jobs carry enough state to be reprinted (<see cref="TextParams"/>/<see cref="BarcodeParams"/>/<see cref="DinRailParams"/>) -
/// an Image job's original source bytes aren't kept around (they could be an arbitrarily large
/// photo), so it shows up in history for reference only.
/// </summary>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    HistoryKind Kind,
    string Summary,
    byte[] ThumbnailPng,
    TextHistoryParams? TextParams = null,
    BarcodeHistoryParams? BarcodeParams = null,
    DinRailHistoryParams? DinRailParams = null)
{
    [JsonIgnore]
    public bool CanReprint => TextParams != null || BarcodeParams != null || DinRailParams != null;
}

/// <summary>
/// Keeps the last <see cref="MaxEntries"/> successful print jobs, persisted as JSON so they
/// survive an app restart. <see cref="Entries"/> is the live, UI-bound collection - all
/// mutations go through it directly so every view showing it updates without any manual refresh.
/// </summary>
public sealed class PrintHistoryService
{
    private const int MaxEntries = 50;

    private readonly string _filePath;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public PrintHistoryService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Letra200bSharp");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "history.json");
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
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (loaded == null)
            {
                return;
            }

            foreach (var entry in loaded)
            {
                Entries.Add(entry);
            }
        }
        catch
        {
            // A corrupt/unreadable history file shouldn't stop the app from starting - just
            // start with an empty history instead.
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Entries);
        File.WriteAllText(_filePath, json);
    }

    public void Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        Save();
    }

    public void Remove(Guid id)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == id);
        if (entry != null)
        {
            Entries.Remove(entry);
            Save();
        }
    }
}
