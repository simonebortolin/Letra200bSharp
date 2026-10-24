# Letra200bSharp

Print labels - text, 1D barcodes, 2D codes, images, DIN rail strips and compositions of them -
on Bluetooth LE label printers, starting with the Dymo LetraTag 200B.

- `Letra200bSharp` - the printer-agnostic core: labels, the `ILabelPrinterDriver` contract,
  `AddLetra()` and `ILabelPrintingService`.
- `Letra200bSharp.200b` - the Dymo LetraTag 200B driver.

```csharp
services.AddLetra().AddLetraTag200b();
// or: var printing = LabelPrinting.Create(letra => letra.AddLetraTag200b());

var printer = (await printing.DiscoverAsync()).First();
await printing.PrintAsync(printer, new TextLabel("Hello") { Size = LabelTextSize.L });
```

Documentation: https://simonebortolin.github.io/Letra200bSharp/
