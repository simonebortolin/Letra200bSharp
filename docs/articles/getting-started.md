# Getting started

## Register Letra

With `Microsoft.Extensions.DependencyInjection`, register Letra and the drivers of the printers
you want to support. Each driver comes with its own builder:

```csharp
services.AddLetra()
    .AddLetraTag200b(letraTag => letraTag
        .SetDefaultFontFamily("DejaVu Sans")
        .SetReplyTimeout(TimeSpan.FromSeconds(15)));
```

Then inject [`ILabelPrintingService`](xref:Letra200bSharp.ILabelPrintingService), the single
entrypoint for everything else.

Without a container, build a standalone service instead:

```csharp
ILabelPrintingService printing = LabelPrinting.Create(letra => letra.AddLetraTag200b());
```

## Discover a printer

```csharp
if (!await printing.IsAnyDriverAvailableAsync())
{
    // e.g. Bluetooth is off - tell the user instead of "no printer found".
}

IReadOnlyList<PrinterInfo> printers = await printing.DiscoverAsync();
```

Discovery runs every registered driver whose transport is available. Each
[`PrinterInfo`](xref:Letra200bSharp.PrinterInfo) remembers which driver it belongs to, so it can
also be persisted and rebuilt later from its `Id`.

## Preview and print

```csharp
var label = new TextLabel("Kitchen")
{
    Size = LabelTextSize.L,
    Box = TextBoxStyle.Rounded,
    Formatting = new() { Bold = true }
};

byte[] previewPng = printing.RenderPreview(label, printer);
PrintResult result = await printing.PrintAsync(printer, label);

if (!result.Printed)
{
    Console.WriteLine(result.Message);
}
```

Previews work before any printer is found: without a printer they use the default driver
(the first registered, or the one set with `SetDefaultDriver`).

## Options per driver

| LetraTag 200B option | Default | Builder method |
| -------------------- | ------- | -------------- |
| Font for labels that don't name one | `Arial` | `SetDefaultFontFamily` |
| Bluetooth name prefix of printers | `Letratag` | `SetDeviceNamePrefix` |
| Wait for the printer's reply | 10 s | `SetReplyTimeout` |
| Pause between packets | 20 ms | `SetPacketDelay` |
| Requested BLE MTU | 512 | `SetRequestedMtu` |
