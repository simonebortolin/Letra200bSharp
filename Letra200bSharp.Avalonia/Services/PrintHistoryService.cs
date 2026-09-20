using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Letra200bSharp.Avalonia.Services;

public enum HistoryKind
{
    Image,
    Text,
    Barcode,
    DinRail,
    Qr2D,
    Draw,
    Compose
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
public sealed record BarcodeHistoryParams(
    string Data,
    string Symbology,
    bool NoCut,
    string CaptionPosition = "None",
    string CaptionFontFamily = "Arial",
    string CaptionSize = "M",
    string CaptionAlign = "Center");

/// <summary>Enough of a 2D Code tab's state to restore it and let the user reprint - see <see cref="ViewModels.QrTabViewModel.LoadFrom"/>.</summary>
public sealed record QrHistoryParams(string Data, string Symbology);

/// <summary>
/// A Draw tab canvas, kept pixel-exact so it can be reprinted (or reopened for further editing)
/// - see <see cref="ViewModels.DrawTabViewModel.LoadFrom"/>. Unlike an Image tab job, the source
/// is always a small, already print-sized monochrome drawing rather than an arbitrary photo, so
/// keeping the full bytes around is cheap.
/// </summary>
public sealed record DrawHistoryParams(byte[] Png, int WidthDots);

/// <summary>
/// An Image tab's source bytes, kept so it can become one element of a Compose tab strip (see
/// <see cref="ComposeElement"/>/<see cref="ComposeElementParams"/>). Unlike a plain Image print or
/// save, this only exists because the user took the deliberate extra step of hitting
/// "Concatenate" - a regular <see cref="HistoryEntry"/> for an Image job still never keeps the
/// source bytes (an arbitrary photo could be huge), so this type is never used there.
/// </summary>
public sealed record ImageHistoryParams(byte[] ImageBytes, bool PreRendered);

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
/// One element of a composed label kept in history - the same per-tab shape as
/// <see cref="ComposeElement"/> (see the Compose tab's staging list), minus the id and
/// thumbnail that only matter while it's still being staged.
/// </summary>
public sealed record ComposeElementParams(
    string Kind,
    string Summary,
    TextHistoryParams? TextParams = null,
    BarcodeHistoryParams? BarcodeParams = null,
    QrHistoryParams? QrParams = null,
    DrawHistoryParams? DrawParams = null,
    ImageHistoryParams? ImageParams = null);

/// <summary>Enough of a Compose tab's state to restore it and let the user reprint - see <see cref="ViewModels.ComposeTabViewModel.LoadFrom"/>.</summary>
public sealed record ComposeHistoryParams(IReadOnlyList<ComposeElementParams> Elements);

/// <summary>
/// One past print job - or, since <see cref="Printed"/> was added, one deliberately saved design
/// that was never (yet) sent to a printer, for a user who wants to build up a library of labels
/// without a Dymo in reach. <see cref="ThumbnailPng"/> is the same PNG bytes
/// <see cref="Letra200bSharp.LetraHelper.PreviewImage(byte[], bool, bool)"/> already produces for
/// the tab's live preview, so it stays tiny. Every job except Image carries enough state to be
/// reprinted (<see cref="TextParams"/>/<see cref="BarcodeParams"/>/<see cref="QrParams"/>/<see cref="DinRailParams"/>/<see cref="DrawParams"/>/<see cref="ComposeParams"/>) -
/// an Image job's original source bytes aren't kept around (they could be an arbitrarily large
/// photo), so it shows up in history for reference only.
/// </summary>
/// <param name="Printed">
/// <c>true</c> if this entry came from an actual successful print; <c>false</c> if the user
/// explicitly saved the design without printing it (see each tab's <c>Save</c> command). Defaults
/// to <c>true</c> so entries persisted before this field existed still deserialize as prints.
/// </param>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    HistoryKind Kind,
    string Summary,
    byte[] ThumbnailPng,
    TextHistoryParams? TextParams = null,
    BarcodeHistoryParams? BarcodeParams = null,
    DinRailHistoryParams? DinRailParams = null,
    QrHistoryParams? QrParams = null,
    bool Printed = true,
    DrawHistoryParams? DrawParams = null,
    ComposeHistoryParams? ComposeParams = null)
{
    [JsonIgnore]
    public bool CanReprint => TextParams != null || BarcodeParams != null || DinRailParams != null || QrParams != null || DrawParams != null || ComposeParams != null;
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
