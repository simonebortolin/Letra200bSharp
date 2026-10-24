namespace Letra200bSharp;

/// <summary>
/// The single entrypoint for discovering printers, previewing labels and printing them, across
/// every registered <see cref="ILabelPrinterDriver"/>. Obtain it from dependency injection after
/// <c>services.AddLetra()</c>, or with <see cref="LabelPrinting.Create"/> outside a DI container.
/// </summary>
public interface ILabelPrintingService
{
    /// <summary>Every registered driver, in registration order.</summary>
    IReadOnlyList<ILabelPrinterDriver> Drivers { get; }

    /// <summary>
    /// The driver used when no printer is involved yet, e.g. to preview a label before one is
    /// selected - <see cref="LetraOptions.DefaultDriver"/>, or else the first registered.
    /// </summary>
    ILabelPrinterDriver DefaultDriver { get; }

    /// <summary>The driver handling <paramref name="printer"/>.</summary>
    /// <exception cref="InvalidOperationException">No registered driver is named <see cref="PrinterInfo.Driver"/>.</exception>
    ILabelPrinterDriver GetDriver(PrinterInfo printer);

    /// <summary>Whether at least one driver's transport is usable right now (see <see cref="ILabelPrinterDriver.IsAvailableAsync"/>).</summary>
    Task<bool> IsAnyDriverAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks for nearby printers with every driver whose transport is available.</summary>
    Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>The renderer of <paramref name="printer"/>'s driver, or of <see cref="DefaultDriver"/> when <c>null</c>.</summary>
    ILabelRenderer GetRenderer(PrinterInfo? printer = null);

    /// <summary>Renders an on-screen PNG preview - see <see cref="ILabelRenderer.RenderPreview"/>.</summary>
    byte[] RenderPreview(Label label, PrinterInfo? printer = null);

    /// <summary>Renders <paramref name="label"/> for <paramref name="printer"/> and prints it.</summary>
    /// <exception cref="ArgumentException">The label's content can't be rendered.</exception>
    Task<PrintResult> PrintAsync(PrinterInfo printer, Label label, CancellationToken cancellationToken = default);
}
