using Microsoft.Extensions.DependencyInjection;

namespace Letra200bSharp.Tests;

/// <summary>Font-independent checks of the LetraTag 200B driver and renderer through the public API.</summary>
public class LetraTagLabelRendererTests
{
    private readonly ILabelPrinterDriver _driver = LabelPrinting.Create(letra => letra.AddLetraTag200b()).DefaultDriver;

    private ILabelRenderer Renderer => _driver.Renderer;

    [Test]
    public async Task Capabilities_describe_a_32_dot_head_with_30_printable_dots()
    {
        await Assert.That(_driver.Capabilities.HeadDots).IsEqualTo(32);
        await Assert.That(_driver.Capabilities.PrintableHeadDots).IsEqualTo(30);
        await Assert.That(_driver.Capabilities.HeadDotsPerMm).IsEqualTo(_driver.Capabilities.FeedDotsPerMm / 2f);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Barcodes_only_reach_the_unprintable_edge_rows_when_edge_to_edge(bool edgeToEdge)
    {
        var bitmap = Renderer.Render(new BarcodeLabel("ABC-123", BarcodeSymbology.Code128) { EdgeToEdge = edgeToEdge });

        await Assert.That(bitmap.HeadDots).IsEqualTo(32);
        await Assert.That(bitmap.Length).IsGreaterThan(0);
        await Assert.That(Ink.InRow(bitmap, 0) || Ink.InRow(bitmap, 31)).IsEqualTo(edgeToEdge);
    }

    [Test]
    public async Task Pre_rendered_content_round_trips_pixel_exact()
    {
        var label = new TwoDCodeLabel("round trip");
        var content = Renderer.RenderContent(label);

        var direct = Renderer.Render(label);
        var reprinted = Renderer.Render(new ImageLabel(content) { PreRendered = true });

        await Assert.That(Ink.Hex(reprinted)).IsEqualTo(Ink.Hex(direct));
    }

    [Test]
    public async Task Pre_rendered_images_of_the_wrong_height_are_rejected()
    {
        var wrongHeight = Images.Png(40, 20, _ => { });

        await Assert.That(() => Renderer.Render(new ImageLabel(wrongHeight) { PreRendered = true })).Throws<ArgumentException>();
    }

    [Test]
    public async Task Composed_labels_are_as_long_as_their_parts()
    {
        var a = new BarcodeLabel("AB", BarcodeSymbology.Code128);
        var b = new TwoDCodeLabel("hi");

        int separate = Renderer.Render(a).Length + Renderer.Render(b).Length;
        var composed = Renderer.Render(new ComposedLabel(new Label[] { a, b }));

        await Assert.That(composed.Length).IsEqualTo(separate);
        await Assert.That(composed.HeadDots).IsEqualTo(32);
    }
}
