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

/// <summary>
/// Enough of a Text tab's state to restore it and let the user reprint - see <see cref="ViewModels.TextTabViewModel.LoadFrom"/>.
/// <see cref="Bold"/>/<see cref="Italic"/> are independently toggleable (post-1.5); <c>Style</c>
/// itself used to also carry "Bold"/"Italic" as mutually-exclusive values pre-1.5 - see
/// <see cref="WithLegacyStyleMigrated"/> for how an old saved entry maps onto the new fields.
/// </summary>
public sealed record TextHistoryParams(
    string Line1,
    string? Line2,
    string? FontFamily,
    string Size,
    string Style,
    decimal WidthScale,
    string BoxStyle,
    bool UpperCase,
    string Align = "Left",
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false,
    decimal LetterSpacing = 0m,
    int FrameTop = 0,
    int FrameBottom = 0,
    int FrameLeft = 0,
    int FrameRight = 0)
{
    /// <summary>A pre-1.5 entry stored Bold/Italic as <c>Style</c> values, which no longer exist on <see cref="TextStyle"/> - map them onto the new independent <see cref="Bold"/>/<see cref="Italic"/> fields instead.</summary>
    public TextHistoryParams WithLegacyStyleMigrated() => Style switch
    {
        "Bold" => this with { Style = nameof(TextStyle.Normal), Bold = true },
        "Italic" => this with { Style = nameof(TextStyle.Normal), Italic = true },
        _ => this
    };

    /// <summary>
    /// The label these settings describe. Mirrors <see cref="ViewModels.TextTabViewModel"/>'s
    /// own composing of the two lines: a second line only makes sense with a size/style that
    /// leaves room for it.
    /// </summary>
    public TextLabel ToLabel(bool edgeToEdge)
    {
        var t = WithLegacyStyleMigrated();
        bool line2Enabled = t.Style != nameof(TextStyle.Vertical) && t.Size != nameof(LabelTextSize.L) && t.Size != nameof(LabelTextSize.XL);
        string text = line2Enabled && !string.IsNullOrEmpty(t.Line2) ? t.Line1 + Environment.NewLine + t.Line2 : t.Line1;
        return new TextLabel(text)
        {
            FontFamily = t.FontFamily ?? "Arial",
            Size = Enum.Parse<LabelTextSize>(t.Size),
            Style = Enum.Parse<TextStyle>(t.Style),
            UpperCase = t.UpperCase,
            WidthScale = (float)t.WidthScale,
            Box = Enum.Parse<TextBoxStyle>(t.BoxStyle),
            Align = Enum.Parse<TextAlign>(t.Align),
            Formatting = new TextFormatting { Bold = t.Bold, Italic = t.Italic, Underline = t.Underline, Strikethrough = t.Strikethrough, LetterSpacing = (float)t.LetterSpacing },
            FrameSpacing = new FrameSpacing { Top = t.FrameTop, Bottom = t.FrameBottom, Left = t.FrameLeft, Right = t.FrameRight },
            EdgeToEdge = edgeToEdge
        };
    }
}

/// <summary>
/// Enough of a Barcode tab's state to restore it and let the user reprint - see <see cref="ViewModels.BarcodeTabViewModel.LoadFrom"/>.
/// <c>ShowNumber</c> is a legacy (pre-1.5) flag, read only from existing history files and never
/// written back; <see cref="WithLegacyCaptionMigrated"/> maps it to the caption it always rendered as.
/// </summary>
public sealed record BarcodeHistoryParams(
    string Data,
    string Symbology,
    bool NoCut,
    string CaptionPosition = "None",
    string CaptionFontFamily = "Arial",
    string CaptionSize = "M",
    string CaptionAlign = "Center",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ShowNumber = false)
{
    /// <summary>The old fixed "show number" caption was exactly Below / Arial / M / Center.</summary>
    public BarcodeHistoryParams WithLegacyCaptionMigrated() =>
        ShowNumber && CaptionPosition == nameof(Letra200bSharp.CaptionPosition.None)
            ? this with { CaptionPosition = nameof(Letra200bSharp.CaptionPosition.Below), CaptionFontFamily = "Arial", CaptionSize = nameof(Letra200bSharp.CaptionSize.M), CaptionAlign = nameof(TextAlign.Center), ShowNumber = false }
            : this;

    [JsonIgnore]
    public BarcodeSymbology ParsedSymbology => Enum.Parse<BarcodeSymbology>(Symbology);

    public CaptionOptions ToCaption() => new()
    {
        Position = Enum.Parse<Letra200bSharp.CaptionPosition>(CaptionPosition),
        FontFamily = CaptionFontFamily,
        Size = Enum.Parse<Letra200bSharp.CaptionSize>(CaptionSize),
        Align = Enum.Parse<TextAlign>(CaptionAlign)
    };

    /// <summary>The label these settings describe.</summary>
    public BarcodeLabel ToLabel() => new(Data, ParsedSymbology) { Caption = ToCaption(), EdgeToEdge = NoCut };
}

/// <summary>
/// Enough of a 2D Code tab's state to restore it and let the user reprint - see <see cref="ViewModels.QrTabViewModel.LoadFrom"/>.
/// <c>Symbology</c> is the tab's display label (see <see cref="SymbologyChoices"/>), not the enum name.
/// </summary>
public sealed record QrHistoryParams(string Data, string Symbology)
{
    /// <summary>2D Code tab ComboBox label paired with the <see cref="TwoDSymbology"/> it selects - the labels are what gets persisted.</summary>
    public static readonly IReadOnlyList<(string Label, TwoDSymbology Value)> SymbologyChoices = new[]
    {
        ("Auto", TwoDSymbology.Auto),
        ("QR", TwoDSymbology.QrCode),
        ("Micro QR", TwoDSymbology.MicroQrCode),
        ("rMQR (rectangular)", TwoDSymbology.RectangularMicroQrCode),
        ("Data Matrix", TwoDSymbology.DataMatrix),
    };

    public static TwoDSymbology ParseSymbology(string label) =>
        SymbologyChoices.FirstOrDefault(c => c.Label == label, SymbologyChoices[0]).Value;

    [JsonIgnore]
    public TwoDSymbology ParsedSymbology => ParseSymbology(Symbology);

    /// <summary>The label these settings describe.</summary>
    public TwoDCodeLabel ToLabel() => new(Data, ParsedSymbology);
}

/// <summary>
/// A Draw tab canvas, kept pixel-exact so it can be reprinted (or reopened for further editing)
/// - see <see cref="ViewModels.DrawTabViewModel.LoadFrom"/>. Unlike an Image tab job, the source
/// is always a small, already print-sized monochrome drawing rather than an arbitrary photo, so
/// keeping the full bytes around is cheap.
/// </summary>
public sealed record DrawHistoryParams(byte[] Png, int WidthDots)
{
    /// <summary>The drawing as a label - already print-sized, at the printable head height.</summary>
    public ImageLabel ToLabel() => new(Png) { PreRendered = true };
}

/// <summary>
/// An Image tab element of a Compose tab strip (see <see cref="ComposeElement"/>/<see cref="ComposeElementParams"/>),
/// kept as its already thresholded and print-sized content PNG (see
/// <see cref="ILabelRenderer.RenderContent"/>), never the source photo. That keeps
/// composition.json and history.json small no matter how large the original image was, and
/// avoids re-thresholding the photo on every preview.
/// </summary>
public sealed record ImageHistoryParams(byte[] ContentPng)
{
    /// <summary>The stored content as a label, printed pixel-exact.</summary>
    public ImageLabel ToLabel() => new(ContentPng) { PreRendered = true };
}

/// <summary>One row of a DIN Rail strip - see <see cref="DinRailHistoryParams"/>.</summary>
public sealed record DinRailRowParams(string Text, decimal Modules);

/// <summary>
/// Enough of a DIN Rail tab's state to restore it and let the user reprint - see <see cref="ViewModels.DinRailTabViewModel.LoadFrom"/>.
/// <see cref="Bold"/>/<see cref="Italic"/> are independently toggleable (post-1.5) - see
/// <see cref="WithLegacyStyleMigrated"/>, same migration <see cref="TextHistoryParams"/> needs.
/// </summary>
public sealed record DinRailHistoryParams(
    IReadOnlyList<DinRailRowParams> Rows,
    string? FontFamily,
    string Style,
    bool UpperCase,
    string Align,
    string Sizing,
    bool ShowSeparators,
    bool Bold = false,
    bool Italic = false)
{
    public DinRailHistoryParams WithLegacyStyleMigrated() => Style switch
    {
        "Bold" => this with { Style = nameof(TextStyle.Normal), Bold = true },
        "Italic" => this with { Style = nameof(TextStyle.Normal), Italic = true },
        _ => this
    };

    [JsonIgnore]
    public TextFormatting Formatting => new() { Bold = Bold, Italic = Italic };
}

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
/// <see cref="ILabelRenderer.RenderPreview"/> already produces for
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
