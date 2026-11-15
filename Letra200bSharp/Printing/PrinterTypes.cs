namespace Letra200bSharp;

/// <summary>A printer found by (or known to) a driver.</summary>
/// <param name="Id">Driver-specific address of the printer, e.g. a Bluetooth device id.</param>
/// <param name="Name">Human-readable name, e.g. as advertised by the device.</param>
/// <param name="Driver">The <see cref="ILabelPrinterDriver.Name"/> of the driver that handles it.</param>
public sealed record PrinterInfo(string Id, string Name, string Driver)
{
    public override string ToString() => Name;
}

/// <summary>Physical characteristics of a printer model, in printer dots.</summary>
public sealed record PrinterCapabilities
{
    /// <summary>Human-readable model name, e.g. "Dymo LetraTag 200B".</summary>
    public required string Model { get; init; }

    /// <summary>Total dots across the print head (the label's height).</summary>
    public required int HeadDots { get; init; }

    /// <summary>Dots across the print head that reliably end up on the label - at most <see cref="HeadDots"/>.</summary>
    public required int PrintableHeadDots { get; init; }

    /// <summary>Head-axis resolution, in dots per millimeter.</summary>
    public required float HeadDotsPerMm { get; init; }

    /// <summary>Feed-axis (label length) resolution, in dots per millimeter.</summary>
    public required float FeedDotsPerMm { get; init; }
}

/// <summary>
/// A rendered label: one bit per printer dot, <see cref="HeadDots"/> tall (across the print head)
/// and <see cref="Length"/> long (along the feed axis). Immutable.
/// </summary>
public sealed class LabelBitmap
{
    private readonly byte[] _dots;

    /// <param name="length">Dots along the feed axis.</param>
    /// <param name="headDots">Dots across the print head.</param>
    /// <param name="dots">
    /// One byte per dot, 1 = ink and 0 = blank, head-axis major: the dot at
    /// (<c>feed</c>, <c>head</c>) is at index <c>head + feed * headDots</c>. Copied.
    /// </param>
    public LabelBitmap(int length, int headDots, ReadOnlySpan<byte> dots)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headDots);
        if (dots.Length != length * headDots)
        {
            throw new ArgumentException($"Expected {length * headDots} dots ({length}×{headDots}), got {dots.Length}.", nameof(dots));
        }

        Length = length;
        HeadDots = headDots;
        _dots = dots.ToArray();
    }

    /// <summary>Dots along the feed axis (label length).</summary>
    public int Length { get; }

    /// <summary>Dots across the print head (label height).</summary>
    public int HeadDots { get; }

    /// <summary>Whether the dot at <paramref name="feed"/>, <paramref name="head"/> is inked.</summary>
    public bool this[int feed, int head] => _dots[head + feed * HeadDots] != 0;

    /// <summary>The raw dots, in the layout described by the constructor.</summary>
    public ReadOnlySpan<byte> Dots => _dots;
}

/// <summary>The outcome of a print attempt.</summary>
public sealed record PrintResult
{
    /// <summary>Whether the label was (likely) printed - see <see cref="Message"/> for any caveat.</summary>
    public required bool Printed { get; init; }

    /// <summary>Human-readable explanation of the outcome.</summary>
    public required string Message { get; init; }

    /// <summary>The raw status code reported by the printer, if it reported one.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Transport-level numbers about the attempt, if it got far enough to produce any.</summary>
    public PrintStatistics? Statistics { get; init; }
}

/// <summary>Low-level, purely informational numbers about a print attempt ("stats for nerds").</summary>
public sealed record PrintStatistics
{
    /// <summary>Combined size of everything sent to the printer.</summary>
    public required int TotalBytes { get; init; }

    /// <summary>How many separate writes the job was split into.</summary>
    public required int PacketCount { get; init; }

    /// <summary>Wall-clock time from starting the attempt to getting a result.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Driver-specific extras (e.g. Bluetooth service and MTU), as display name to value.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
}
