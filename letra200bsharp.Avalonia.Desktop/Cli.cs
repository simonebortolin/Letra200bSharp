using CommandLine;
using CommandLine.Text;
using Letra200bSharp;

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

    [Option("size", Default = LetraHelper.LabelTextSize.M, HelpText = "XS, S, M, L, or XL.")]
    public LetraHelper.LabelTextSize Size { get; set; }

    [Option("style", Default = LetraHelper.TextStyle.Normal, HelpText = "Normal, Bold, Italic, Outline, Shadow, or Vertical.")]
    public LetraHelper.TextStyle Style { get; set; }

    [Option("box", Default = LetraHelper.TextBoxStyle.None, HelpText = "None, Underline, Square, Pointed, Rounded, Edged, or Crocodile (ignored for size XL).")]
    public LetraHelper.TextBoxStyle Box { get; set; }

    [Option("uppercase", HelpText = "Render the text in all uppercase.")]
    public bool UpperCase { get; set; }

    [Option("width-scale", Default = 1f, HelpText = "Horizontal glyph stretch factor.")]
    public float WidthScale { get; set; }

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

    [Option("symbology", Default = LetraHelper.BarcodeSymbology.Code128, HelpText = "Code128, Code39, Codabar, Itf, Ean13, Ean8, UpcA, or UpcE.")]
    public LetraHelper.BarcodeSymbology Symbology { get; set; }

    [Option("no-cut", HelpText = "See PrepareBitmap - the rendered barcode already accounts for the printer's unprintable top/bottom row.")]
    public bool NoCut { get; set; }
}

/// <summary>
/// The headless counterpart of the Image/Text/Barcode tabs in the Avalonia GUI - one verb
/// per tab, exposing the same <see cref="LetraHelper"/> options. Replaces the old, image-only
/// letra200bsharp.Console project.
/// </summary>
internal static class Cli
{
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
        var parserResult = parser.ParseArguments<ListDevicesOptions, ImageOptions, TextOptions, BarcodeOptions>(args);

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
            errs => Task.FromResult(1));
    }

    private static async Task<int> RunListDevicesAsync()
    {
        try
        {
            var devices = await LetraPrinter.ScanForDevicesAsync();
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
            var job = LetraHelper.CreateJob(imageBytes, o.NoCut, o.PreRendered);
            return await PrintAsync(o.Address, job);
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
            bool line2Allowed = o.Style != LetraHelper.TextStyle.Vertical && o.Size != LetraHelper.LabelTextSize.L && o.Size != LetraHelper.LabelTextSize.XL;
            string text = line2Allowed && !string.IsNullOrEmpty(o.Line2)
                ? o.Line1 + Environment.NewLine + o.Line2
                : o.Line1;

            var job = LetraHelper.CreateJob(text, o.Font, o.Size, o.Style, o.UpperCase, o.WidthScale, o.Box, noCut: o.NoCut);
            return await PrintAsync(o.Address, job);
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
            var job = LetraHelper.CreateJob(o.Data, o.Symbology, o.NoCut);
            return await PrintAsync(o.Address, job);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> PrintAsync(string address, List<byte[]> job)
    {
        var result = await LetraPrinter.PrintAsync(address, job);
        Console.WriteLine(result.Printed ? $"Printed: {result.Message}" : $"Error: {result.Message}");
        return result.Printed ? 0 : 1;
    }
}
