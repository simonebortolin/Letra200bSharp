namespace Letra200bSharp;

/// <summary>
/// Everything needed to support one printer model (or family): how to find it, how its labels
/// are rendered, and how a rendered label is sent to it. Supporting a new printer means
/// implementing this interface and registering it with <see cref="LetraBuilder.AddDriver{TDriver}"/>
/// (or a driver package's own <c>Add...()</c> extension, e.g. <c>AddLetraTag200b()</c>).
/// </summary>
/// <remarks>
/// Drivers are registered as singletons and may be called concurrently, so implementations must
/// be thread-safe.
/// </remarks>
public interface ILabelPrinterDriver
{
    /// <summary>Unique, stable key for this driver, e.g. <c>"LetraTag200b"</c> - stored in <see cref="PrinterInfo.Driver"/>.</summary>
    string Name { get; }

    /// <summary>Physical characteristics of the printers this driver handles.</summary>
    PrinterCapabilities Capabilities { get; }

    /// <summary>Renders labels for this driver's printers.</summary>
    ILabelRenderer Renderer { get; }

    /// <summary>
    /// Whether the transport this driver needs (e.g. a Bluetooth adapter that's switched on) is
    /// usable right now - lets callers say "Bluetooth is off" instead of "no printer found".
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks for nearby printers this driver can talk to.</summary>
    Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends an already-rendered label (see <see cref="ILabelRenderer.Render"/>) to <paramref name="printer"/>.</summary>
    /// <param name="printer">A printer of this driver - from <see cref="DiscoverAsync"/>, or built from a known id.</param>
    Task<PrintResult> PrintAsync(PrinterInfo printer, LabelBitmap bitmap, CancellationToken cancellationToken = default);
}
