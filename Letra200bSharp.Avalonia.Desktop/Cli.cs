using CommandLine;
using CommandLine.Text;
using Letra200bSharp;
using Letra200bSharp.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Letra200bSharp.Avalonia.Desktop;

[Verb("list-devices", HelpText = "Scan for nearby Dymo LetraTag 200B printers.")]
internal class ListDevicesOptions
{
}

[Verb("image", HelpText = "Print an image file.")]
internal class ImageOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("path", Required = true, HelpText = "Path to the image file.")]
    public required string Path { get; set; }

    [Option("no-cut", HelpText = "The image already accounts for the printer's unprintable top/bottom row (see PrepareBitmap).")]
    public bool NoCut { get; set; }

    [Option("pre-rendered", HelpText = "The image was already thresholded/sized externally (e.g. ImageMagick); skip the auto-rescale heuristic used for arbitrary photos.")]
    public bool PreRendered { get; set; }
}

[Verb("text", HelpText = "Print text.")]
internal class TextOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("line1", Required = true, HelpText = "First line of text.")]
    public required string Line1 { get; set; }

    [Option("line2", HelpText = "Second line of text (ignored for size L/XL or the Vertical style).")]
    public string? Line2 { get; set; }

    [Option("font", Default = "Arial", HelpText = "Font family name.")]
    public string Font { get; set; } = "Arial";

    [Option("size", Default = LabelTextSize.M, HelpText = "XS, S, M, L, or XL.")]
    public LabelTextSize Size { get; set; }

    [Option("style", Default = TextStyle.Normal, HelpText = "Normal, Outline, Shadow, or Vertical.")]
    public TextStyle Style { get; set; }

    [Option("box", Default = TextBoxStyle.None, HelpText = "None, Underline, Square, Pointed, Rounded, Edged, Crocodile, Heart, Star, Flower, or Ribbon (ignored for size XL).")]
    public TextBoxStyle Box { get; set; }

    [Option("uppercase", HelpText = "Render the text in all uppercase.")]
    public bool UpperCase { get; set; }

    [Option("width-scale", Default = 1f, HelpText = "Horizontal glyph stretch factor.")]
    public float WidthScale { get; set; }

    [Option("bold", HelpText = "Bold weight - independent of --italic/--underline/--strikethrough, combines freely with any of them and with --style.")]
    public bool Bold { get; set; }

    [Option("italic", HelpText = "Italic slant - combines freely with --bold/--underline/--strikethrough and --style.")]
    public bool Italic { get; set; }

    [Option("underline", HelpText = "Underline each line of text.")]
    public bool Underline { get; set; }

    [Option("strikethrough", HelpText = "Strike through each line of text.")]
    public bool Strikethrough { get; set; }

    [Option("letter-spacing", Default = 0f, HelpText = "Extra gap after every glyph, as a fraction of the em size (0 = normal spacing) - independent of --width-scale, which stretches glyphs instead of spacing them apart.")]
    public float LetterSpacing { get; set; }

    [Option("frame-top", Default = 0, HelpText = "Extra margin (printer dots) above the --box frame.")]
    public int FrameTop { get; set; }

    [Option("frame-bottom", Default = 0, HelpText = "Extra margin (printer dots) below the --box frame.")]
    public int FrameBottom { get; set; }

    [Option("frame-left", Default = 0, HelpText = "Extra margin (printer dots) left of the --box frame.")]
    public int FrameLeft { get; set; }

    [Option("frame-right", Default = 0, HelpText = "Extra margin (printer dots) right of the --box frame.")]
    public int FrameRight { get; set; }

    [Option("no-cut", HelpText = "See PrepareBitmap - the rendered text already accounts for the printer's unprintable top/bottom row.")]
    public bool NoCut { get; set; }
}

[Verb("barcode", HelpText = "Print a 1D barcode.")]
internal class BarcodeOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("data", Required = true, HelpText = "Barcode content.")]
    public required string Data { get; set; }

    [Option("symbology", Default = BarcodeSymbology.Auto, HelpText = "Auto (guesses from the data's shape/checksum), Code128, Code39, Codabar, Itf, Ean13, Ean8, UpcA, or UpcE.")]
    public BarcodeSymbology Symbology { get; set; }

    [Option("no-cut", HelpText = "See PrepareBitmap - the rendered barcode already accounts for the printer's unprintable top/bottom row.")]
    public bool NoCut { get; set; }

    [Option("caption-position", Default = CaptionPosition.None, HelpText = "None, Below, or Above - prints the barcode's own data as a small caption alongside the bars.")]
    public CaptionPosition CaptionPosition { get; set; }

    [Option("caption-font", Default = "Arial", HelpText = "Font family for the caption (bold, ignored if caption-position is None).")]
    public string CaptionFont { get; set; } = "Arial";

    [Option("caption-size", Default = CaptionSize.M, HelpText = "S, M, or L - how much of the printable height the caption takes up.")]
    public CaptionSize CaptionSize { get; set; }

    [Option("caption-align", Default = TextAlign.Center, HelpText = "Left, Center, or Right - alignment between the caption and the bars, whichever ends up narrower.")]
    public TextAlign CaptionAlign { get; set; }
}

[Verb("qr", HelpText = "Print a 2D matrix code (QR, Micro QR, rMQR, or Data Matrix).")]
internal class QrOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("data", Required = true, HelpText = "Code content.")]
    public required string Data { get; set; }

    [Option("symbology", Default = TwoDSymbology.Auto, HelpText = "Auto (least tape while staying scannable), QrCode, MicroQrCode, RectangularMicroQrCode, or DataMatrix. Note: on this printer a full QR is always near the practical scanning limit - prefer rMQR or Micro QR.")]
    public TwoDSymbology Symbology { get; set; }
}

[Verb("print-symbol", HelpText = "Print a symbol previously saved from the GUI's Draw tab symbol library.")]
internal class PrintSymbolOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("name", Required = true, HelpText = "Name of the saved symbol (case-insensitive) - see the Draw tab's symbol library.")]
    public required string Name { get; set; }
}

[Verb("din", HelpText = "Print a DIN rail label strip - one or more text segments, each sized to a number of 18mm DIN modules, printed as a single continuous label.")]
internal class DinOptions
{
    [Option("address", Required = true, HelpText = "Bluetooth address (or id) of the printer.")]
    public required string Address { get; set; }

    [Option("label", Required = true, Min = 1, HelpText = "One label segment as \"text=modules\" (modules = how many 18mm DIN modules wide, e.g. \"Generale=2\"). Repeat for multiple segments in the strip.")]
    public required IEnumerable<string> Labels { get; set; }

    [Option("font", Default = "Arial", HelpText = "Font family name.")]
    public string Font { get; set; } = "Arial";

    [Option("style", Default = TextStyle.Normal, HelpText = "Normal, Outline, or Shadow (Vertical isn't supported for DIN rail labels).")]
    public TextStyle Style { get; set; }

    [Option("bold", HelpText = "Bold weight - independent of --italic, combines freely with it and with --style.")]
    public bool Bold { get; set; }

    [Option("italic", HelpText = "Italic slant - combines freely with --bold and --style.")]
    public bool Italic { get; set; }

    [Option("align", Default = TextAlign.Center, HelpText = "Left, Center, or Right.")]
    public TextAlign Align { get; set; }

    [Option("sizing", Default = DinRailSizing.MaxPerLabel, HelpText = "Uniform (every segment rendered at the same, shared scale) or MaxPerLabel (each segment independently as large as it can be).")]
    public DinRailSizing Sizing { get; set; }

    [Option("uppercase", HelpText = "Render every label in all uppercase.")]
    public bool UpperCase { get; set; }

    [Option("no-separators", HelpText = "Don't print a vertical separator line between adjacent segments.")]
    public bool NoSeparators { get; set; }

    [Option("no-cut", HelpText = "See PrepareBitmap - the rendered strip already accounts for the printer's unprintable top/bottom row.")]
    public bool NoCut { get; set; }
}

/// <summary>
/// The headless counterpart of the Image/Text/Barcode/2D Code/DIN Rail tabs in the Avalonia GUI -
/// one verb per tab, exposing the same label options. Replaces the old, image-only
/// letra200bsharp.Console project.
/// </summary>
internal static class Cli
{
    private static readonly ILabelPrintingService Printing = LabelPrinting.Create(letra => letra.AddDymoLetraTag());

    /// <returns>
    /// The process exit code, or <c>null</c> if <paramref name="args"/> didn't start with one
    /// of our verbs at all (<see cref="BadVerbSelectedError"/>/<see cref="NoVerbSelectedError"/>)
    /// - e.g. an Avalonia-specific flag - in which case the caller should launch the GUI with
    /// <paramref name="args"/> instead of treating this as a CLI usage error.
    /// </returns>
    public static async Task<int?> RunAsync(string[] args)
    {
        // Parser.Default auto-prints help/error text to Console.Out as a side effect of
        // parsing, before we get a chance to decide whether this is even one of our verbs -
        // that would print a spurious "not recognized" error for args meant for the GUI (e.g.
        // an Avalonia-specific flag). Suppress that and print it ourselves, only once we know
        // this really was a CLI invocation.
        var parser = new Parser(cfg =>
        {
            cfg.HelpWriter = null;
            cfg.AutoHelp = true;
            cfg.AutoVersion = true;
        });
        var parserResult = parser.ParseArguments<ListDevicesOptions, ImageOptions, TextOptions, BarcodeOptions, QrOptions, PrintSymbolOptions, DinOptions>(args);

        if (parserResult is NotParsed<object> notParsed)
        {
            if (notParsed.Errors.All(e => e is BadVerbSelectedError or NoVerbSelectedError))
            {
                return null;
            }

            Console.WriteLine(HelpText.AutoBuild(parserResult));
            bool isHelpOrVersion = notParsed.Errors.Any(e => e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError);
            return isHelpOrVersion ? 0 : 1;
        }

        return await parserResult.MapResult(
            (ListDevicesOptions o) => RunListDevicesAsync(),
            (ImageOptions o) => RunImageAsync(o),
            (TextOptions o) => RunTextAsync(o),
            (BarcodeOptions o) => RunBarcodeAsync(o),
            (QrOptions o) => RunQrAsync(o),
            (PrintSymbolOptions o) => RunPrintSymbolAsync(o),
            (DinOptions o) => RunDinAsync(o),
            errs => Task.FromResult(1));
    }

    private static async Task<int> RunListDevicesAsync()
    {
        try
        {
            var devices = await Printing.DiscoverAsync();
            if (devices.Count == 0)
            {
                Console.WriteLine("No Dymo LetraTag 200B found.");
                return 1;
            }

            foreach (var device in devices)
            {
                Console.WriteLine($"{device.Id}\t{device.Name}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunImageAsync(ImageOptions o)
    {
        try
        {
            var imageBytes = await File.ReadAllBytesAsync(o.Path);
            return await PrintAsync(o.Address, new ImageLabel(imageBytes) { PreRendered = o.PreRendered, EdgeToEdge = o.NoCut });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunTextAsync(TextOptions o)
    {
        try
        {
            // Mirrors TextTabViewModel.ComposedText: a second line only makes sense with a
            // size/style that leaves room for it.
            bool line2Allowed = o.Style != TextStyle.Vertical && o.Size != LabelTextSize.L && o.Size != LabelTextSize.XL;
            string text = line2Allowed && !string.IsNullOrEmpty(o.Line2)
                ? o.Line1 + Environment.NewLine + o.Line2
                : o.Line1;

            return await PrintAsync(o.Address, new TextLabel(text)
            {
                FontFamily = o.Font,
                Size = o.Size,
                Style = o.Style,
                UpperCase = o.UpperCase,
                WidthScale = o.WidthScale,
                Box = o.Box,
                Formatting = new TextFormatting { Bold = o.Bold, Italic = o.Italic, Underline = o.Underline, Strikethrough = o.Strikethrough, LetterSpacing = o.LetterSpacing },
                FrameSpacing = new FrameSpacing { Top = o.FrameTop, Bottom = o.FrameBottom, Left = o.FrameLeft, Right = o.FrameRight },
                EdgeToEdge = o.NoCut
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunBarcodeAsync(BarcodeOptions o)
    {
        try
        {
            var caption = new CaptionOptions { Position = o.CaptionPosition, FontFamily = o.CaptionFont, Size = o.CaptionSize, Align = o.CaptionAlign };
            return await PrintAsync(o.Address, new BarcodeLabel(o.Data, o.Symbology) { Caption = caption, EdgeToEdge = o.NoCut });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunQrAsync(QrOptions o)
    {
        try
        {
            return await PrintAsync(o.Address, new TwoDCodeLabel(o.Data, o.Symbology));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunPrintSymbolAsync(PrintSymbolOptions o)
    {
        try
        {
            var library = new SymbolLibraryService();
            var symbol = library.FindByName(o.Name);
            if (symbol == null)
            {
                Console.WriteLine($"Error: no saved symbol named \"{o.Name}\".");
                return 1;
            }

            return await PrintAsync(o.Address, new ImageLabel(symbol.PixelPng) { PreRendered = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunDinAsync(DinOptions o)
    {
        try
        {
            var cells = new List<DinRailCell>();
            foreach (var label in o.Labels)
            {
                int separatorIndex = label.LastIndexOf('=');
                if (separatorIndex < 0 || !decimal.TryParse(label[(separatorIndex + 1)..], out var modules))
                {
                    Console.WriteLine($"Error: --label \"{label}\" isn't in the \"text=modules\" format (e.g. \"Generale=2\").");
                    return 1;
                }

                cells.Add(new DinRailCell(label[..separatorIndex], modules));
            }

            return await PrintAsync(o.Address, new DinRailLabel(cells)
            {
                FontFamily = o.Font,
                Style = o.Style,
                UpperCase = o.UpperCase,
                Align = o.Align,
                Sizing = o.Sizing,
                ShowSeparators = !o.NoSeparators,
                Formatting = new TextFormatting { Bold = o.Bold, Italic = o.Italic },
                EdgeToEdge = o.NoCut
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Prints on the printer at <paramref name="address"/>, handled by the default (and only) driver.</summary>
    private static async Task<int> PrintAsync(string address, Label label)
    {
        var printer = new PrinterInfo(address, address, Printing.DefaultDriver.Name);
        var result = await Printing.PrintAsync(printer, label);
        Console.WriteLine(result.Printed ? $"Printed: {result.Message}" : $"Error: {result.Message}");
        return result.Printed ? 0 : 1;
    }
}
