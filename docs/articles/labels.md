# Labels

A label describes *what* to print, independently of the printer. Required content goes in the
constructor and every option is an `init` property, so new options never break existing code:

```csharp
var label = new TextLabel("Hello") { Size = LabelTextSize.XL, Align = TextAlign.Center };
var bolder = label with { Formatting = new() { Bold = true } };
```

| Label | For |
| ----- | --- |
| [`TextLabel`](xref:Letra200bSharp.TextLabel) | One or more lines of text: size, style (outline, shadow, vertical), bold/italic/underline/strikethrough, letter spacing, width scale, alignment, and a decorative frame with per-side margins. |
| [`BarcodeLabel`](xref:Letra200bSharp.BarcodeLabel) | Code 128, Code 39, Codabar, ITF, EAN-13/8, UPC-A/E - or `Auto`, which picks from the data - with an optional caption above or below. |
| [`TwoDCodeLabel`](xref:Letra200bSharp.TwoDCodeLabel) | QR, Micro QR, rMQR and Data Matrix, sized so modules stay square and scannable. |
| [`ImageLabel`](xref:Letra200bSharp.ImageLabel) | A picture, thresholded adaptively (light colors still print), or content already rendered for the printer (`PreRendered`). |
| [`DinRailLabel`](xref:Letra200bSharp.DinRailLabel) | A strip of cells for a row of DIN rail devices, each as wide as its device (18 mm per module). |
| [`ComposedLabel`](xref:Letra200bSharp.ComposedLabel) | Several labels printed back to back as one. |

## Edge-to-edge

The LetraTag 200B's head has 32 dots, but the first and last don't reliably print. By default
content is laid out on the 30 printable dots; `EdgeToEdge = true` uses all 32.

## Optional renderer features

Some renderers can tell more about a label before printing it. Check for them with a type test:

```csharp
var renderer = printing.GetRenderer(printer);

if (renderer is ITwoDCodePlanner planner)
{
    TwoDCodePlan plan = planner.Plan(new TwoDCodeLabel("https://example.com"));
    // plan.SymbolName, plan.ModuleSizeMm, plan.MayNotScanWell ...
}

if (renderer is IDinRailLayout layout)
{
    float lengthMm = layout.LengthMm(1.5m);
}
```

## Storing a rendered label

[`ILabelRenderer.RenderContent`](xref:Letra200bSharp.ILabelRenderer.RenderContent(Letra200bSharp.Label))
returns the label's printable content as a 1-bit PNG. Reprinting it as an `ImageLabel` with
`PreRendered = true` gives exactly the same dots, without the original data.
