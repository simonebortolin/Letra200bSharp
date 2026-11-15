namespace Letra200bSharp;

/// <summary>
/// What to print, independently of which printer prints it. Each concrete label carries its
/// required content as constructor arguments and every optional setting as an <c>init</c>
/// property, so new options can be added without breaking existing callers (source or binary).
/// </summary>
/// <remarks>
/// The hierarchy is closed: renderers (see <see cref="ILabelRenderer"/>) handle every label type
/// below, so new kinds of content are added here rather than by deriving elsewhere.
/// </remarks>
public abstract record Label
{
    private protected Label()
    {
    }
}

/// <summary>One or more lines of text, optionally framed.</summary>
public sealed record TextLabel : Label
{
    /// <param name="text">The text to print; line breaks start a new line.</param>
    public TextLabel(string text)
    {
        Text = text;
    }

    /// <summary>The text to print; line breaks start a new line.</summary>
    public string Text { get; init; }

    /// <summary>Font family name, or <c>null</c> for the driver's default font.</summary>
    public string? FontFamily { get; init; }

    public LabelTextSize Size { get; init; } = LabelTextSize.M;

    public TextStyle Style { get; init; } = TextStyle.Normal;

    /// <summary>Whether to print <see cref="Text"/> in all caps.</summary>
    public bool UpperCase { get; init; }

    /// <summary>
    /// Horizontal stretch factor for the glyphs (1 = normal, 2 = twice as wide, 0.5 = half as
    /// wide), independent of <see cref="Size"/>, which only affects how much of the head height
    /// is filled.
    /// </summary>
    public float WidthScale { get; init; } = 1f;

    /// <summary>Decorative border/underline framing the text.</summary>
    public TextBoxStyle Box { get; init; } = TextBoxStyle.None;

    /// <summary>Per-side extra margin around the frame drawn by <see cref="Box"/>.</summary>
    public FrameSpacing FrameSpacing { get; init; }

    /// <summary>Horizontal alignment of shorter lines relative to the widest one.</summary>
    public TextAlign Align { get; init; } = TextAlign.Left;

    public TextFormatting Formatting { get; init; }

    /// <summary>
    /// Lay the text out over the printer's full head height, including the edge dots it may not
    /// reliably print, instead of only its printable area. Ignored inside a <see cref="ComposedLabel"/>.
    /// </summary>
    public bool EdgeToEdge { get; init; }
}

/// <summary>A 1D barcode, optionally captioned with its own data.</summary>
public sealed record BarcodeLabel : Label
{
    /// <param name="data">
    /// The barcode's content - digits only, with the exact digit count each symbology requires,
    /// for <see cref="BarcodeSymbology.Ean13"/>/<see cref="BarcodeSymbology.Ean8"/>/<see cref="BarcodeSymbology.UpcA"/>/<see cref="BarcodeSymbology.UpcE"/>.
    /// </param>
    /// <param name="symbology">How to encode <paramref name="data"/>.</param>
    public BarcodeLabel(string data, BarcodeSymbology symbology = BarcodeSymbology.Auto)
    {
        Data = data;
        Symbology = symbology;
    }

    public string Data { get; init; }

    public BarcodeSymbology Symbology { get; init; }

    public CaptionOptions Caption { get; init; } = CaptionOptions.None;

    /// <inheritdoc cref="TextLabel.EdgeToEdge"/>
    public bool EdgeToEdge { get; init; }
}

/// <summary>A 2D matrix code (QR, Micro QR, rMQR, Data Matrix).</summary>
public sealed record TwoDCodeLabel : Label
{
    public TwoDCodeLabel(string data, TwoDSymbology symbology = TwoDSymbology.Auto)
    {
        Data = data;
        Symbology = symbology;
    }

    public string Data { get; init; }

    public TwoDSymbology Symbology { get; init; }
}

/// <summary>An image - either an arbitrary picture, or content already rendered for the printer.</summary>
public sealed record ImageLabel : Label
{
    /// <param name="image">Encoded image bytes (PNG, JPEG, ...).</param>
    public ImageLabel(byte[] image)
    {
        Image = image;
    }

    /// <summary>Encoded image bytes (PNG, JPEG, ...).</summary>
    public byte[] Image { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="Image"/> is already a 1-bit image sized for the printer - its
    /// short side exactly the printable head height (or the full head height with
    /// <see cref="EdgeToEdge"/>) - e.g. the output of <see cref="ILabelRenderer.RenderContent"/>.
    /// It is then printed pixel-exact instead of being thresholded and scaled.
    /// </summary>
    public bool PreRendered { get; init; }

    /// <inheritdoc cref="TextLabel.EdgeToEdge"/>
    public bool EdgeToEdge { get; init; }
}

/// <summary>One cell of a <see cref="DinRailLabel"/>: its text, and how many DIN rail modules wide it is.</summary>
/// <param name="Text">The cell's text; may be empty.</param>
/// <param name="Modules">Width in DIN rail modules (see <see cref="DinRailLabel.ModuleWidthMm"/>); fractional widths are allowed.</param>
public readonly record struct DinRailCell(string Text, decimal Modules);

/// <summary>
/// A continuous strip for a row of DIN rail devices, each cell sized to the physical width of the
/// device under it.
/// </summary>
public sealed record DinRailLabel : Label
{
    /// <summary>Width, in millimeters, of one DIN rail module (EN 50022 terminal block pitch).</summary>
    public const float ModuleWidthMm = 18f;

    public DinRailLabel(IReadOnlyList<DinRailCell> cells)
    {
        Cells = cells;
    }

    public IReadOnlyList<DinRailCell> Cells { get; init; }

    /// <inheritdoc cref="TextLabel.FontFamily"/>
    public string? FontFamily { get; init; }

    /// <summary>Text style for every cell. <see cref="TextStyle.Vertical"/> isn't supported here.</summary>
    public TextStyle Style { get; init; } = TextStyle.Normal;

    public bool UpperCase { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Center;

    public DinRailSizing Sizing { get; init; } = DinRailSizing.MaxPerLabel;

    /// <summary>Whether to draw a thin separator between adjacent cells.</summary>
    public bool ShowSeparators { get; init; }

    public TextFormatting Formatting { get; init; }

    /// <inheritdoc cref="TextLabel.EdgeToEdge"/>
    public bool EdgeToEdge { get; init; }
}

/// <summary>
/// Several labels printed back to back as one continuous label. Every part is laid out at the
/// printer's printable height, whatever its own <c>EdgeToEdge</c> setting, so they all line up.
/// </summary>
public sealed record ComposedLabel : Label
{
    public ComposedLabel(IReadOnlyList<Label> parts)
    {
        Parts = parts;
    }

    public IReadOnlyList<Label> Parts { get; init; }
}
