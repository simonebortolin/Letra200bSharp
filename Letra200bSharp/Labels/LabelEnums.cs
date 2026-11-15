namespace Letra200bSharp;

/// <summary>Horizontal alignment of shorter lines relative to the widest line in a multi-line label.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right
}

/// <summary>
/// Layout/paint effect applied to the whole text block - mutually exclusive with each other
/// (unlike <see cref="TextFormatting"/>'s Bold/Italic/Underline/Strikethrough, which combine
/// freely with these and with each other).
/// </summary>
public enum TextStyle
{
    Normal,
    Outline,
    Shadow,

    /// <summary>
    /// Prints each character on its own line, so the text reads top-to-bottom instead of
    /// left-to-right (e.g. "HI" becomes "H" then "I" stacked); existing line breaks are
    /// preserved as their own (blank) line.
    /// </summary>
    Vertical
}

/// <summary>
/// Decorative border/underline framing the text, matching the geometric subset of the real Dymo
/// app's "box and underline styles" plus a few illustrated shapes (Heart/Star/Flower/Ribbon)
/// inspired by similar third-party apps.
/// </summary>
public enum TextBoxStyle
{
    None,
    Underline,
    Square,
    Pointed,
    Rounded,
    Edged,
    Crocodile,
    Heart,
    Star,
    Flower,
    Ribbon
}

/// <summary>
/// How much of the printer's fixed head height the rendered text glyphs fill (and how much
/// horizontal margin surrounds them), from mostly blank margin (<see cref="XS"/>) to nearly
/// edge-to-edge (<see cref="XL"/>). An absolute point size wouldn't mean anything, since text is
/// always scaled to the head height.
/// </summary>
public enum LabelTextSize
{
    XS,
    S,
    M,
    L,
    XL
}

/// <summary>1D barcode symbologies. 2D codes have their own <see cref="TwoDSymbology"/> and <see cref="TwoDCodeLabel"/>.</summary>
public enum BarcodeSymbology
{
    /// <summary>Not a real symbology - resolved to a concrete one from the data's shape (and checksum) before rendering.</summary>
    Auto,
    Code128,
    Code39,
    Codabar,
    Itf,
    Ean13,
    Ean8,
    UpcA,
    UpcE
}

/// <summary>Where an optional caption prints relative to a barcode - see <see cref="CaptionOptions"/>.</summary>
public enum CaptionPosition
{
    /// <summary>No caption - bars only. Unlike the real Dymo app, this is the default.</summary>
    None,
    Below,
    Above
}

/// <summary>How much of the barcode's printable height an enabled caption takes up.</summary>
public enum CaptionSize
{
    S,
    M,
    L
}

/// <summary>
/// 2D matrix symbologies. Unlike a 1D barcode, a matrix code's modules have to stay physically
/// square, so sizing is driven entirely by how many modules fit the printer's head-axis dots.
/// </summary>
public enum TwoDSymbology
{
    /// <summary>
    /// Not a real symbology - resolved to whichever concrete symbology prints the data on the
    /// least tape while keeping every module comfortably scannable.
    /// </summary>
    Auto,
    QrCode,
    MicroQrCode,
    RectangularMicroQrCode,
    DataMatrix
}

/// <summary>
/// How each DIN rail cell's text is sized. Neither mode ever distorts glyphs - both apply a
/// single uniform scale factor, equally in X and Y.
/// </summary>
public enum DinRailSizing
{
    /// <summary>Every cell uses the same font scale - the smallest any single cell needs - so the whole strip reads at one consistent size.</summary>
    Uniform,

    /// <summary>Each cell independently uses the largest scale that fits its own text.</summary>
    MaxPerLabel
}
