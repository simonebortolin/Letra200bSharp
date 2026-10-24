using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Letra200bSharp.Tests;

/// <summary>Font-independent checks of the Dymo LetraTag 200B renderer through the public API.</summary>
public class LetraTagLabelRendererTests
{
    private readonly ILabelPrinterDriver _driver = LabelPrinting.Create(letra => letra.AddDymoLetraTag()).DefaultDriver;

    private ILabelRenderer Renderer => _driver.Renderer;

    [Fact]
    public void Capabilities_describe_a_32_dot_head_with_30_printable_dots()
    {
        Assert.Equal(32, _driver.Capabilities.HeadDots);
        Assert.Equal(30, _driver.Capabilities.PrintableHeadDots);
        Assert.Equal(_driver.Capabilities.FeedDotsPerMm / 2f, _driver.Capabilities.HeadDotsPerMm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Barcodes_render_across_the_full_head(bool edgeToEdge)
    {
        var bitmap = Renderer.Render(new BarcodeLabel("ABC-123", BarcodeSymbology.Code128) { EdgeToEdge = edgeToEdge });

        Assert.Equal(32, bitmap.HeadDots);
        Assert.True(bitmap.Length > 0);
        // Without edge-to-edge the unprintable first and last head rows stay blank.
        bool edgeRowsInked = Enumerable.Range(0, bitmap.Length).Any(feed => bitmap[feed, 0] || bitmap[feed, 31]);
        Assert.Equal(edgeToEdge, edgeRowsInked);
    }

    [Fact]
    public void Invalid_barcode_data_is_an_argument_exception()
    {
        Assert.Throws<ArgumentException>(() => Renderer.Render(new BarcodeLabel("123", BarcodeSymbology.Ean13)));
    }

    [Fact]
    public void Two_d_codes_are_planned_without_ever_falling_back_to_unscannable_modules()
    {
        var planner = Assert.IsAssignableFrom<ITwoDCodePlanner>(Renderer);

        var plan = planner.Plan(new TwoDCodeLabel("hello"));

        Assert.NotEqual(TwoDSymbology.Auto, plan.Symbology);
        Assert.False(plan.MayNotScanWell);
        Assert.Equal(plan.DotsPerModuleShort * 2, plan.DotsPerModuleLong);
    }

    [Fact]
    public void Din_rail_cells_are_measured_in_18_mm_modules()
    {
        var layout = Assert.IsAssignableFrom<IDinRailLayout>(Renderer);

        Assert.Equal(27f, layout.LengthMm(1.5m));
    }

    [Fact]
    public void Pre_rendered_content_round_trips_pixel_exact()
    {
        var label = new TwoDCodeLabel("round trip");
        var content = Renderer.RenderContent(label);

        var direct = Renderer.Render(label);
        var reprinted = Renderer.Render(new ImageLabel(content) { PreRendered = true });

        Assert.Equal(direct.Dots.ToArray(), reprinted.Dots.ToArray());
    }

    [Fact]
    public void Pre_rendered_images_of_the_wrong_height_are_rejected()
    {
        using var bitmap = new SKBitmap(40, 20);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);

        Assert.Throws<ArgumentException>(() => Renderer.Render(new ImageLabel(png.ToArray()) { PreRendered = true }));
    }

    [Fact]
    public void Composed_labels_are_as_long_as_their_parts()
    {
        var a = new BarcodeLabel("AB", BarcodeSymbology.Code128);
        var b = new TwoDCodeLabel("hi");

        int separate = Renderer.Render(a).Length + Renderer.Render(b).Length;
        var composed = Renderer.Render(new ComposedLabel(new Label[] { a, b }));

        Assert.Equal(separate, composed.Length);
        Assert.Equal(32, composed.HeadDots);
    }
}
