namespace Letra200bSharp;

/// <summary>
/// Turns a <see cref="Label"/> into pixels for one printer model. Provided by each
/// <see cref="ILabelPrinterDriver"/>, since how content is laid out depends on the printer's head
/// height, dot shape and unprintable edges.
/// </summary>
/// <remarks>
/// A renderer may also implement optional feature interfaces such as <see cref="ITwoDCodePlanner"/>
/// or <see cref="IDinRailLayout"/>; callers check for them with a type test.
/// </remarks>
public interface ILabelRenderer
{
    /// <summary>Renders <paramref name="label"/> to the exact dots that will be printed.</summary>
    /// <exception cref="ArgumentException">The label's content can't be rendered (e.g. invalid barcode data).</exception>
    LabelBitmap Render(Label label);

    /// <summary>
    /// Renders an on-screen preview of <paramref name="label"/> as a PNG, faithful to what will be
    /// printed (including dot aspect ratio and any unprintable edges).
    /// </summary>
    /// <exception cref="ArgumentException">The label's content can't be rendered.</exception>
    byte[] RenderPreview(Label label);

    /// <summary>
    /// Renders <paramref name="label"/>'s printable content as a 1-bit PNG at printer resolution,
    /// suitable for storing and reprinting later as an <see cref="ImageLabel"/> with
    /// <see cref="ImageLabel.PreRendered"/> set.
    /// </summary>
    /// <exception cref="ArgumentException">The label's content can't be rendered.</exception>
    byte[] RenderContent(Label label);
}

/// <summary>Optional <see cref="ILabelRenderer"/> feature: explains how a 2D code will map onto the printer before printing it.</summary>
public interface ITwoDCodePlanner
{
    /// <exception cref="ArgumentException">The data can't be encoded at a size that fits the printer.</exception>
    TwoDCodePlan Plan(TwoDCodeLabel label);
}

/// <summary>Optional <see cref="ILabelRenderer"/> feature: physical measurements of DIN rail labels.</summary>
public interface IDinRailLayout
{
    /// <summary>Physical length, in millimeters, of a cell <paramref name="modules"/> wide.</summary>
    float LengthMm(decimal modules);

    /// <summary>
    /// The uniform scale the text of <paramref name="label"/>'s cell at <paramref name="cellIndex"/>
    /// needs to fit its width (1 = natural size) - small values flag text likely too small to read.
    /// </summary>
    float RequiredScale(DinRailLabel label, int cellIndex);
}

/// <summary>How a 2D code maps onto the printer: the symbol chosen, its size in modules, and the resulting physical module size.</summary>
public sealed record TwoDCodePlan
{
    /// <summary>The concrete symbology used (never <see cref="TwoDSymbology.Auto"/>).</summary>
    public required TwoDSymbology Symbology { get; init; }

    /// <summary>Human-readable symbol name, e.g. "QR V2", "Micro QR M3", "rMQR R11x27", "Data Matrix 16×16".</summary>
    public required string SymbolName { get; init; }

    /// <summary>Module count along the symbol's shorter side (mapped to the printer's head axis).</summary>
    public required int ModulesShort { get; init; }

    /// <summary>Module count along the symbol's longer side (mapped to the printer's feed axis).</summary>
    public required int ModulesLong { get; init; }

    /// <summary>Printer dots per module on the head axis.</summary>
    public required int DotsPerModuleShort { get; init; }

    /// <summary>Printer dots per module on the feed axis.</summary>
    public required int DotsPerModuleLong { get; init; }

    /// <summary>Physical size of one (square) module, in millimeters.</summary>
    public required float ModuleSizeMm { get; init; }

    /// <summary><c>true</c> when modules are so small that phone cameras may struggle to scan the code.</summary>
    public required bool MayNotScanWell { get; init; }
}
