namespace Letra200bSharp;

/// <summary>
/// Independently toggleable text decorations - unlike <see cref="TextStyle"/>, any combination is
/// valid (e.g. Bold + Italic + Underline together).
/// </summary>
public readonly record struct TextFormatting
{
    /// <summary>No decoration and normal letter spacing.</summary>
    public static TextFormatting None => default;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strikethrough { get; init; }

    /// <summary>
    /// Extra gap inserted after every glyph, as a fraction of the font's em size (0 = normal
    /// spacing) - additive, unlike <see cref="TextLabel.WidthScale"/>, which stretches each glyph
    /// instead of spacing them apart.
    /// </summary>
    public float LetterSpacing { get; init; }
}

/// <summary>
/// Extra margin, in printer dots and on top of whatever margin the frame style already reserves,
/// between the text and each side of the frame drawn by <see cref="TextLabel.Box"/>.
/// </summary>
public readonly record struct FrameSpacing
{
    /// <summary>No extra margin on any side.</summary>
    public static FrameSpacing None => default;

    public int Top { get; init; }

    public int Bottom { get; init; }

    public int Left { get; init; }

    public int Right { get; init; }

    /// <summary>The same extra margin on every side.</summary>
    public static FrameSpacing All(int dots) => new() { Top = dots, Bottom = dots, Left = dots, Right = dots };
}

/// <summary>
/// Styling for the optional caption printed alongside a barcode - always the barcode's own data
/// as bold text; what's configurable is where it goes, in what font, how much height it gets, and
/// how it lines up against the (possibly narrower or wider) bars.
/// </summary>
public sealed record CaptionOptions
{
    /// <summary>No caption - bars only.</summary>
    public static CaptionOptions None { get; } = new();

    /// <summary>Where to print it, or <see cref="CaptionPosition.None"/> to omit it entirely.</summary>
    public CaptionPosition Position { get; init; } = CaptionPosition.None;

    /// <summary>Font family for the caption text, or <c>null</c> for the driver's default font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>How much of the printable height the caption takes up.</summary>
    public CaptionSize Size { get; init; } = CaptionSize.M;

    /// <summary>Horizontal alignment between the caption and the bars, whichever of the two ends up narrower.</summary>
    public TextAlign Align { get; init; } = TextAlign.Center;
}
