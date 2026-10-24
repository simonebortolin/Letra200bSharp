# Adding a printer

A printer model is one [`ILabelPrinterDriver`](xref:Letra200bSharp.ILabelPrinterDriver):

- `Name` - a unique, stable key, stored in every `PrinterInfo` it discovers;
- `Capabilities` - head dots, printable dots and resolution;
- `Renderer` - an [`ILabelRenderer`](xref:Letra200bSharp.ILabelRenderer) that turns labels into a
  [`LabelBitmap`](xref:Letra200bSharp.LabelBitmap) for this head;
- `IsAvailableAsync`, `DiscoverAsync` and `PrintAsync` - the transport.

```csharp
public sealed class MyPrinterDriver : ILabelPrinterDriver
{
    public MyPrinterDriver(IOptions<MyPrinterOptions> options) { /* ... */ }

    public string Name => "Vendor.Model";

    public PrinterCapabilities Capabilities { get; } = new()
    {
        Model = "Vendor Model",
        HeadDots = 64,
        PrintableHeadDots = 64,
        HeadDotsPerMm = 8,
        FeedDotsPerMm = 8
    };

    public ILabelRenderer Renderer { get; } = new MyRenderer();

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => /* radio on? */;

    public Task<IReadOnlyList<PrinterInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        /* scan, then new PrinterInfo(id, name, Name) for each printer */;

    public Task<PrintResult> PrintAsync(PrinterInfo printer, LabelBitmap bitmap, CancellationToken cancellationToken = default) =>
        /* encode the dots in the printer's protocol and send them */;
}
```

Drivers are singletons and may be called concurrently, so they must be thread-safe.

## Register it

```csharp
services.AddLetra()
    .AddLetraTag200b()
    .AddDriver<MyPrinterDriver>();
```

`AddDriver<T>()` resolves the driver from the container, so it can take options and other
services. For a reusable package, wrap the registration in an extension on
[`LetraBuilder`](xref:Letra200bSharp.LetraBuilder), the way `Letra200bSharp.200b` does with
`AddLetraTag200b()`:

```csharp
public static class MyPrinterLetraBuilderExtensions
{
    public static LetraBuilder AddMyPrinter(this LetraBuilder builder, Action<MyPrinterOptions>? configure = null)
    {
        if (configure != null)
        {
            builder.Services.Configure(configure);
        }

        return builder.AddDriver<MyPrinterDriver>();
    }
}
```

Discovery, previews and printing through `ILabelPrintingService` then span every registered
driver, and each print goes to the driver of the printer it targets.
