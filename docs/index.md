---
_layout: landing
---

# Letra200bSharp

A .NET library for printing labels - text, 1D barcodes, 2D codes, images, DIN rail strips and
compositions of them - on Bluetooth LE label printers, starting with the **Dymo LetraTag 200B**.

```csharp
var printing = LabelPrinting.Create(letra => letra.AddLetraTag200b());

var printer = (await printing.DiscoverAsync()).First();
await printing.PrintAsync(printer, new TextLabel("Hello") { Size = LabelTextSize.L });
```

- [Getting started](articles/getting-started.md) - install, configure, discover, preview and print.
- [Labels](articles/labels.md) - every kind of label and its options.
- [Adding a printer](articles/adding-a-printer.md) - support another model by implementing one interface.
- [API reference](api/Letra200bSharp.yml) - generated from the libraries' XML documentation.

| Package | Contents |
| ------- | -------- |
| `Letra200bSharp` | The printer-agnostic core: labels, the driver contract, `AddLetra()` and `ILabelPrintingService`. |
| `Letra200bSharp.200b` | The Dymo LetraTag 200B driver: its renderer, BLE wire format and transport. |

The source, the desktop/Android app built on the library, and the reverse-engineered protocol
notes are on [GitHub](https://github.com/simonebortolin/Letra200bSharp).
