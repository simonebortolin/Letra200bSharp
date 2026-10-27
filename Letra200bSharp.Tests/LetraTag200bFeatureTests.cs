using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Letra200bSharp.Tests;

/// <summary>
/// The LetraTag 200B renderer's features added since the original fork (which only printed
/// images), checked through the public API with font-independent measurements.
/// </summary>
public class LetraTag200bFeatureTests
{
    private readonly ILabelRenderer _renderer = LabelPrinting.Create(letra => letra.AddLetraTag200b()).DefaultDriver.Renderer;

    // ---- Images --------------------------------------------------------------------------

    [Test]
    public async Task Photos_keep_their_aspect_ratio_with_feed_dots_half_the_size_of_head_dots()
    {
        var square = Images.Png(100, 100, c => c.DrawRect(10, 10, 80, 80, new SKPaint { Color = SKColors.Black }));

        // 30 printable head dots tall, so 2 * 30 feed dots long.
        await Assert.That(_renderer.Render(new ImageLabel(square)).Length).IsEqualTo(60);
    }

    [Test]
    public async Task Light_colors_still_print_thanks_to_the_adaptive_threshold()
    {
        // A yellow logo is lighter than a fixed 50% cutoff, but clearly its own cluster.
        var yellowLogo = Images.Png(120, 60, c => c.DrawCircle(60, 30, 25, new SKPaint { Color = SKColors.Yellow }));

        await Assert.That(Ink.Count(_renderer.Render(new ImageLabel(yellowLogo)))).IsGreaterThan(0);
    }

    [Test]
    public async Task Transparent_backgrounds_count_as_blank_tape_and_uniform_images_print_nothing()
    {
        var onTransparent = Images.Png(120, 60, c => c.DrawRect(40, 10, 40, 40, new SKPaint { Color = SKColors.Black }), SKColors.Transparent);
        var uniformGray = Images.Png(120, 60, _ => { }, new SKColor(128, 128, 128));

        var logo = _renderer.Render(new ImageLabel(onTransparent));
        await Assert.That(Ink.Count(logo)).IsGreaterThan(0);
        await Assert.That(Ink.Count(logo)).IsLessThan(logo.Length * logo.HeadDots / 2);
        await Assert.That(Ink.Count(_renderer.Render(new ImageLabel(uniformGray)))).IsEqualTo(0);
    }

    [Test]
    public async Task Without_edge_to_edge_the_unprintable_first_and_last_head_rows_stay_blank()
    {
        var allBlack = Images.Png(64, 64, c => c.DrawRect(0, 0, 64, 64, new SKPaint { Color = SKColors.Black }), SKColors.White);

        var padded = _renderer.Render(new ImageLabel(allBlack) { PreRendered = false });
        var full = _renderer.Render(new ImageLabel(Images.Png(64, 32, c => c.DrawRect(0, 0, 64, 32, new SKPaint { Color = SKColors.Black }))) { PreRendered = true, EdgeToEdge = true });

        await Assert.That(Ink.InRow(padded, 0)).IsFalse();
        await Assert.That(Ink.InRow(padded, 31)).IsFalse();
        await Assert.That(Ink.InRow(full, 0)).IsTrue();
        await Assert.That(Ink.InRow(full, 31)).IsTrue();
    }

    [Test]
    public async Task Previews_are_upscaled_and_mark_the_unprintable_rows_in_gray()
    {
        var label = new BarcodeLabel("ABC", BarcodeSymbology.Code128);
        int length = _renderer.Render(label).Length;

        using var preview = Images.Decode(_renderer.RenderPreview(label));

        await Assert.That(preview.Height).IsEqualTo(32 * 4);
        await Assert.That(preview.Width).IsEqualTo(length * 2);
        await Assert.That(preview.GetPixel(0, 0)).IsEqualTo(new SKColor(224, 224, 224));
        await Assert.That(preview.GetPixel(0, preview.Height - 1)).IsEqualTo(new SKColor(224, 224, 224));
    }

    // ---- Text ----------------------------------------------------------------------------

    private LabelBitmap Text(string text, Func<TextLabel, TextLabel>? configure = null) =>
        _renderer.Render((configure ?? (l => l))(new TextLabel(text)));

    [Test]
    public async Task Text_size_presets_fill_more_of_the_head_as_they_grow()
    {
        await Assert.That(Ink.Count(Text("Hello"))).IsGreaterThan(0);
        await Assert.That(Ink.RowSpan(Text("Hello", l => l with { Size = LabelTextSize.XS })))
            .IsLessThan(Ink.RowSpan(Text("Hello", l => l with { Size = LabelTextSize.XL })));
    }

    [Test]
    public async Task Formatting_options_each_change_the_print()
    {
        // Only "changes": how much ink a decoration adds depends on the font - e.g. with Arial an
        // underline shrinks the glyphs to make room for it, so the total ink can even go down.
        var plain = Ink.Hex(Text("Hello"));

        await Assert.That(Ink.Hex(Text("Hello", l => l with { Formatting = new() { Bold = true } }))).IsNotEqualTo(plain);
        await Assert.That(Ink.Hex(Text("Hello", l => l with { Formatting = new() { Italic = true } }))).IsNotEqualTo(plain);
        await Assert.That(Ink.Hex(Text("Hello", l => l with { Formatting = new() { Underline = true } }))).IsNotEqualTo(plain);
        await Assert.That(Ink.Hex(Text("Hello", l => l with { Formatting = new() { Strikethrough = true } }))).IsNotEqualTo(plain);
        await Assert.That(Text("Hello", l => l with { Formatting = new() { LetterSpacing = 0.5f } }).Length).IsGreaterThan(Text("Hello").Length);
    }

    [Test]
    public async Task Width_scale_stretches_and_upper_case_changes_the_glyphs()
    {
        await Assert.That(Text("Hello", l => l with { WidthScale = 2f }).Length).IsGreaterThan(Text("Hello").Length);
        await Assert.That(Ink.Hex(Text("hello", l => l with { UpperCase = true }))).IsEqualTo(Ink.Hex(Text("HELLO")));
    }

    [Test]
    [Arguments(TextBoxStyle.Square)]
    [Arguments(TextBoxStyle.Rounded)]
    [Arguments(TextBoxStyle.Pointed)]
    [Arguments(TextBoxStyle.Edged)]
    [Arguments(TextBoxStyle.Crocodile)]
    [Arguments(TextBoxStyle.Heart)]
    [Arguments(TextBoxStyle.Star)]
    [Arguments(TextBoxStyle.Flower)]
    [Arguments(TextBoxStyle.Ribbon)]
    [Arguments(TextBoxStyle.Underline)]
    public async Task Every_box_style_draws_a_frame(TextBoxStyle box)
    {
        var framed = Text("Hi", l => l with { Size = LabelTextSize.S, Box = box });

        await Assert.That(Ink.Hex(framed)).IsNotEqualTo(Ink.Hex(Text("Hi", l => l with { Size = LabelTextSize.S })));
    }

    [Test]
    public async Task Frame_spacing_moves_the_frame_on_each_side()
    {
        var plain = Text("Hi", l => l with { Size = LabelTextSize.S, Box = TextBoxStyle.Square });
        var left = Text("Hi", l => l with { Size = LabelTextSize.S, Box = TextBoxStyle.Square, FrameSpacing = new() { Left = 3 } });
        var top = Text("Hi", l => l with { Size = LabelTextSize.S, Box = TextBoxStyle.Square, FrameSpacing = new() { Top = 3 } });

        await Assert.That(Ink.Hex(left)).IsNotEqualTo(Ink.Hex(plain));
        await Assert.That(Ink.Hex(top)).IsNotEqualTo(Ink.Hex(plain));
        await Assert.That(Ink.Hex(top)).IsNotEqualTo(Ink.Hex(left));
    }

    [Test]
    public async Task Two_lines_stack_and_alignment_moves_the_shorter_one()
    {
        string text = "Wide first line" + Environment.NewLine + "x";
        var left = Text(text, l => l with { Size = LabelTextSize.S, Align = TextAlign.Left });
        var right = Text(text, l => l with { Size = LabelTextSize.S, Align = TextAlign.Right });

        await Assert.That(right.Length).IsEqualTo(left.Length);
        await Assert.That(Ink.Hex(right)).IsNotEqualTo(Ink.Hex(left));
    }

    [Test]
    [Arguments(TextStyle.Outline)]
    [Arguments(TextStyle.Shadow)]
    [Arguments(TextStyle.Vertical)]
    public async Task Text_styles_change_the_print(TextStyle style)
    {
        await Assert.That(Ink.Hex(Text("HI", l => l with { Style = style }))).IsNotEqualTo(Ink.Hex(Text("HI")));
    }

    // ---- 1D barcodes ---------------------------------------------------------------------

    [Test]
    [Arguments("ABC-123", BarcodeSymbology.Code128)]
    [Arguments("CODE39", BarcodeSymbology.Code39)]
    [Arguments("A12345B", BarcodeSymbology.Codabar)]
    [Arguments("12345678", BarcodeSymbology.Itf)]
    [Arguments("4006381333931", BarcodeSymbology.Ean13)]
    [Arguments("96385074", BarcodeSymbology.Ean8)]
    [Arguments("036000291452", BarcodeSymbology.UpcA)]
    [Arguments("04252614", BarcodeSymbology.UpcE)]
    public async Task Every_symbology_encodes_valid_data(string data, BarcodeSymbology symbology)
    {
        var bitmap = _renderer.Render(new BarcodeLabel(data, symbology));

        await Assert.That(Ink.Count(bitmap)).IsGreaterThan(0);
        await Assert.That(bitmap.HeadDots).IsEqualTo(32);
    }

    [Test]
    [Arguments("4006381333931", BarcodeSymbology.Ean13)]
    [Arguments("HELLO-39", BarcodeSymbology.Code39)]       // fits Code 39's character set
    [Arguments("hello_world!", BarcodeSymbology.Code128)]  // doesn't
    public async Task Auto_picks_the_symbology_from_the_data(string data, BarcodeSymbology expected)
    {
        await Assert.That(Ink.Hex(_renderer.Render(new BarcodeLabel(data))))
            .IsEqualTo(Ink.Hex(_renderer.Render(new BarcodeLabel(data, expected))));
    }

    [Test]
    [Arguments("123", BarcodeSymbology.Ean13)]
    [Arguments("4006381333932", BarcodeSymbology.Ean13)] // wrong check digit
    [Arguments("12345", BarcodeSymbology.Itf)]           // ITF needs an even digit count
    public async Task Invalid_data_is_rejected(string data, BarcodeSymbology symbology)
    {
        await Assert.That(() => _renderer.Render(new BarcodeLabel(data, symbology))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Captions_print_below_or_above_the_bars()
    {
        var bars = new BarcodeLabel("ABC-123", BarcodeSymbology.Code128);
        var below = _renderer.Render(bars with { Caption = new() { Position = CaptionPosition.Below } });
        var above = _renderer.Render(bars with { Caption = new() { Position = CaptionPosition.Above } });

        await Assert.That(Ink.Hex(below)).IsNotEqualTo(Ink.Hex(_renderer.Render(bars)));
        await Assert.That(Ink.Hex(above)).IsNotEqualTo(Ink.Hex(below));
    }

    // ---- 2D codes ------------------------------------------------------------------------

    [Test]
    [Arguments("HELLO1", TwoDSymbology.QrCode)]
    [Arguments("12345", TwoDSymbology.MicroQrCode)]
    [Arguments("https://x.y/abc", TwoDSymbology.RectangularMicroQrCode)]
    [Arguments("datamatrix!", TwoDSymbology.DataMatrix)]
    public async Task Every_2d_symbology_renders_with_square_modules(string data, TwoDSymbology symbology)
    {
        var label = new TwoDCodeLabel(data, symbology);
        var plan = ((ITwoDCodePlanner)_renderer).Plan(label);

        await Assert.That(plan.Symbology).IsEqualTo(symbology);
        await Assert.That(plan.DotsPerModuleLong).IsEqualTo(plan.DotsPerModuleShort * 2);
        await Assert.That(plan.ModulesShort * plan.DotsPerModuleShort).IsLessThanOrEqualTo(30);
        await Assert.That(Ink.Count(_renderer.Render(label))).IsGreaterThan(0);
    }

    [Test]
    [Arguments("hi")]
    [Arguments("https://github.com/simonebortolin")]
    public async Task Auto_never_picks_a_full_qr_code(string data)
    {
        var plan = ((ITwoDCodePlanner)_renderer).Plan(new TwoDCodeLabel(data));

        await Assert.That(plan.Symbology).IsNotEqualTo(TwoDSymbology.QrCode);
        await Assert.That(plan.Symbology).IsNotEqualTo(TwoDSymbology.Auto);
        await Assert.That(plan.MayNotScanWell).IsFalse();
    }

    [Test]
    public async Task Data_too_long_for_the_head_is_rejected()
    {
        await Assert.That(() => _renderer.Render(new TwoDCodeLabel(new string('x', 500), TwoDSymbology.QrCode))).Throws<ArgumentException>();
    }

    // ---- DIN rail ------------------------------------------------------------------------

    private static DinRailLabel Rail(params (string Text, decimal Modules)[] cells) =>
        new(cells.Select(c => new DinRailCell(c.Text, c.Modules)).ToList());

    [Test]
    public async Task Din_rail_strips_are_as_long_as_their_modules()
    {
        var one = _renderer.Render(Rail(("A", 1m)));
        var three = _renderer.Render(Rail(("A", 1m), ("B", 2m)));

        await Assert.That(three.Length).IsGreaterThanOrEqualTo(3 * one.Length - 2);
        await Assert.That(three.Length).IsLessThanOrEqualTo(3 * one.Length + 2);
        await Assert.That(((IDinRailLayout)_renderer).LengthMm(1.5m)).IsEqualTo(27f);
    }

    [Test]
    public async Task Separators_and_sizing_change_the_strip()
    {
        var label = Rail(("Lights", 1m), ("Kitchen sockets", 2m));

        await Assert.That(Ink.Hex(_renderer.Render(label with { ShowSeparators = true }))).IsNotEqualTo(Ink.Hex(_renderer.Render(label)));
        await Assert.That(Ink.Hex(_renderer.Render(label with { Sizing = DinRailSizing.Uniform })))
            .IsNotEqualTo(Ink.Hex(_renderer.Render(label with { Sizing = DinRailSizing.MaxPerLabel })));
    }

    [Test]
    public async Task Text_too_long_for_its_cell_needs_a_smaller_scale()
    {
        var layout = (IDinRailLayout)_renderer;
        // A single word can't wrap onto a second line, so it has to shrink to fit.
        var label = Rail(("A", 1m), ("Supercalifragilistic", 1m));

        await Assert.That(layout.RequiredScale(label, 0)).IsEqualTo(1f);
        await Assert.That(layout.RequiredScale(label, 1)).IsLessThan(1f);
    }

    // ---- Composition ---------------------------------------------------------------------

    [Test]
    public async Task Nested_compositions_flatten_into_one_strip()
    {
        var a = new BarcodeLabel("AB", BarcodeSymbology.Code128);
        var b = new TwoDCodeLabel("hi");

        var flat = _renderer.Render(new ComposedLabel(new Label[] { a, b, a }));
        var nested = _renderer.Render(new ComposedLabel(new Label[] { a, new ComposedLabel(new Label[] { b, a }) }));

        await Assert.That(Ink.Hex(nested)).IsEqualTo(Ink.Hex(flat));
    }

    [Test]
    public async Task An_empty_composition_is_rejected()
    {
        await Assert.That(() => _renderer.Render(new ComposedLabel(Array.Empty<Label>()))).Throws<ArgumentException>();
    }
}
