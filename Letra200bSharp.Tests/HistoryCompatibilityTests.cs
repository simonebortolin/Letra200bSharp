using System.Text.Json;
using Letra200bSharp.Avalonia.Services;

namespace Letra200bSharp.Tests;

/// <summary>
/// The app's saved history and compositions must keep loading - and reprinting the same label -
/// across versions, including entries written before Bold/Italic and captions were independent.
/// </summary>
public class HistoryCompatibilityTests
{
    [Test]
    [Arguments("Bold", true, false)]
    [Arguments("Italic", false, true)]
    public async Task Legacy_bold_and_italic_text_styles_become_independent_flags(string legacyStyle, bool bold, bool italic)
    {
        var label = new TextHistoryParams("Hi", null, "Arial", "M", legacyStyle, 1m, "None", false).ToLabel(edgeToEdge: true);

        await Assert.That(label.Style).IsEqualTo(TextStyle.Normal);
        await Assert.That(label.Formatting.Bold).IsEqualTo(bold);
        await Assert.That(label.Formatting.Italic).IsEqualTo(italic);
    }

    [Test]
    public async Task Legacy_din_rail_styles_are_migrated_too()
    {
        var legacy = new DinRailHistoryParams(new[] { new DinRailRowParams("A", 1m) }, "Arial", "Bold", false, "Center", "Uniform", true);

        var migrated = legacy.WithLegacyStyleMigrated();

        await Assert.That(migrated.Style).IsEqualTo("Normal");
        await Assert.That(migrated.Formatting.Bold).IsTrue();
    }

    [Test]
    public async Task The_legacy_show_number_flag_becomes_the_caption_it_rendered_as()
    {
        var caption = new BarcodeHistoryParams("123", "Code128", false, ShowNumber: true).WithLegacyCaptionMigrated().ToCaption();

        await Assert.That(caption).IsEqualTo(new CaptionOptions { Position = CaptionPosition.Below, FontFamily = "Arial", Size = CaptionSize.M, Align = TextAlign.Center });
    }

    [Test]
    [Arguments("M", "Normal", true)]
    [Arguments("L", "Normal", false)]
    [Arguments("XL", "Normal", false)]
    [Arguments("M", "Vertical", false)]
    public async Task A_second_line_is_kept_only_when_size_and_style_leave_room_for_it(string size, string style, bool twoLines)
    {
        var label = new TextHistoryParams("One", "Two", "Arial", size, style, 1m, "None", false).ToLabel(edgeToEdge: true);

        await Assert.That(label.Text.Contains(Environment.NewLine)).IsEqualTo(twoLines);
    }

    [Test]
    [Arguments("Auto", TwoDSymbology.Auto)]
    [Arguments("Micro QR", TwoDSymbology.MicroQrCode)]
    [Arguments("rMQR (rectangular)", TwoDSymbology.RectangularMicroQrCode)]
    [Arguments("something unknown", TwoDSymbology.Auto)]
    public async Task Persisted_2d_symbology_labels_map_back_to_symbologies(string persisted, TwoDSymbology expected)
    {
        await Assert.That(new QrHistoryParams("x", persisted).ToLabel().Symbology).IsEqualTo(expected);
    }

    [Test]
    public async Task Composed_parts_are_laid_out_at_the_printable_height()
    {
        var text = new TextHistoryParams("Hi", null, "Arial", "M", "Normal", 1m, "None", false);

        var part = CompositionService.ToLabel(text, null, null, null, null) as TextLabel;
        await Assert.That(part).IsNotNull();
        await Assert.That(part!.EdgeToEdge).IsFalse();
        await Assert.That(CompositionService.ToLabel(null, null, null, new DrawHistoryParams(new byte[] { 1 }, 1), null) is ImageLabel { PreRendered: true }).IsTrue();
        await Assert.That(() => CompositionService.ToLabel(null, null, null, null, null)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task History_written_by_an_older_version_still_loads()
    {
        // A text entry saved before Underline/Strikethrough/LetterSpacing/frame margins existed,
        // and a barcode entry saved with the old ShowNumber flag.
        const string json = """
            [
              {"Id":"6b0f0d4e-7d0e-4c2a-9a55-2c1f3b7a8e01","Timestamp":"2026-08-20T10:00:00+02:00","Kind":1,"Summary":"Hi","ThumbnailPng":"AQ==",
               "TextParams":{"Line1":"Hi","Line2":null,"FontFamily":"Arial","Size":"M","Style":"Bold","WidthScale":1,"BoxStyle":"None","UpperCase":false}},
              {"Id":"6b0f0d4e-7d0e-4c2a-9a55-2c1f3b7a8e02","Timestamp":"2026-08-20T10:01:00+02:00","Kind":2,"Summary":"123","ThumbnailPng":"AQ==",
               "BarcodeParams":{"Data":"123","Symbology":"Code128","NoCut":true,"ShowNumber":true}}
            ]
            """;

        var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json)!;

        await Assert.That(entries[0].Printed).IsTrue();
        await Assert.That(entries[0].TextParams!.ToLabel(edgeToEdge: true).Formatting.Bold).IsTrue();
        var barcode = entries[1].BarcodeParams!.WithLegacyCaptionMigrated().ToLabel();
        await Assert.That(barcode.Caption.Position).IsEqualTo(CaptionPosition.Below);
        await Assert.That(barcode.EdgeToEdge).IsTrue();
    }
}
