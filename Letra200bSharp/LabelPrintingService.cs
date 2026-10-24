using Microsoft.Extensions.Options;

namespace Letra200bSharp;

/// <summary>Default <see cref="ILabelPrintingService"/>: routes every call to the right registered driver.</summary>
internal sealed class LabelPrintingService : ILabelPrintingService
{
    private readonly Dictionary<string, ILabelPrinterDriver> _driversByName;

    public LabelPrintingService(IEnumerable<ILabelPrinterDriver> drivers, IOptions<LetraOptions> options)
    {
        Drivers = drivers.ToList();
        if (Drivers.Count == 0)
        {
            throw new InvalidOperationException(
                "No label printer driver is registered. Register one when configuring Letra, " +
                "e.g. services.AddLetra().AddLetraTag200b(), or builder.AddDriver<TDriver>() for your own.");
        }

        _driversByName = new Dictionary<string, ILabelPrinterDriver>(StringComparer.Ordinal);
        foreach (var driver in Drivers)
        {
            if (!_driversByName.TryAdd(driver.Name, driver))
            {
                throw new InvalidOperationException($"More than one label printer driver is named '{driver.Name}'.");
            }
        }

        var defaultName = options.Value.DefaultDriver;
        if (defaultName == null)
        {
            DefaultDriver = Drivers[0];
        }
        else if (!_driversByName.TryGetValue(defaultName, out var defaultDriver))
        {
            throw new InvalidOperationException($"The default label printer driver '{defaultName}' isn't registered.");
        }
        else
        {
            DefaultDriver = defaultDriver;
        }
    }

    public IReadOnlyList<ILabelPrinterDriver> Drivers { get; }

    public ILabelPrinterDriver DefaultDriver { get; }

    public ILabelPrinterDriver GetDriver(PrinterInfo printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return _driversByName.TryGetValue(printer.Driver, out var driver)
            ? driver
            : throw new InvalidOperationException($"No label printer driver named '{printer.Driver}' is registered.");
    }

    public async Task<bool> IsAnyDriverAvailableAsync(CancellationToken cancellationToken = default)
    {
        foreach (var driver in Drivers)
        {
            if (await driver.IsAvailableAsync(cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var printers = new List<PrinterInfo>();
        foreach (var driver in Drivers)
        {
            if (!await driver.IsAvailableAsync(cancellationToken))
            {
                continue;
            }

            foreach (var printer in await driver.DiscoverAsync(cancellationToken))
            {
                if (!printers.Contains(printer))
                {
                    printers.Add(printer);
                }
            }
        }

        return printers;
    }

    public ILabelRenderer GetRenderer(PrinterInfo? printer = null) =>
        (printer == null ? DefaultDriver : GetDriver(printer)).Renderer;

    public byte[] RenderPreview(Label label, PrinterInfo? printer = null) =>
        GetRenderer(printer).RenderPreview(label);

    public async Task<PrintResult> PrintAsync(PrinterInfo printer, Label label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        var driver = GetDriver(printer);

        // Rendering is CPU-bound (and can take a while for a large photo), so keep it off the
        // caller's thread - typically a UI thread.
        var bitmap = await Task.Run(() => driver.Renderer.Render(label), cancellationToken);
        return await driver.PrintAsync(printer, bitmap, cancellationToken);
    }
}
