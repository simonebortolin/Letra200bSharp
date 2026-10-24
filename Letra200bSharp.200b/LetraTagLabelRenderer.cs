using SkiaSharp;

namespace Letra200bSharp.LetraTag200b
{
    /// <summary>
    /// <see cref="ILabelRenderer"/> for the Dymo LetraTag 200B: maps each <see cref="Label"/> onto the
    /// SkiaSharp rendering pipeline in <see cref="RenderHelper"/>.
    /// </summary>
    internal sealed class LetraTagLabelRenderer : ILabelRenderer, ITwoDCodePlanner, IDinRailLayout
    {
        private readonly RenderHelper _render;
        private readonly string _defaultFontFamily;

        public LetraTagLabelRenderer(LetraTag200bOptions options)
        {
            _defaultFontFamily = options.DefaultFontFamily;
            _render = new RenderHelper(_defaultFontFamily);
        }

        public LabelBitmap Render(Label label)
        {
            var (content, noCut) = RenderPrintable(label);
            var image = _render.PrepareImage(content, noCut, preRendered: !IsPhoto(label));
            return new LabelBitmap(image.Width, image.Height, image.Data);
        }

        public byte[] RenderPreview(Label label) => label switch
        {
            // Photos (and pre-rendered images) go straight through the image pipeline, the
            // same as printing them does.
            ImageLabel image => _render.PreviewImage(image.Image, image.EdgeToEdge, image.PreRendered),
            ComposedLabel composed => _render.PreviewComposedImage(RenderParts(composed)),
            _ => PreviewPrintable(label)
        };

        public byte[] RenderContent(Label label) => label switch
        {
            TextLabel text => RenderText(text, text.EdgeToEdge),
            BarcodeLabel barcode => _render.RenderBarcodeContentImage(barcode.Data, barcode.Symbology, Caption(barcode), barcode.EdgeToEdge),
            TwoDCodeLabel twoD => _render.RenderTwoDContentImage(twoD.Data, twoD.Symbology),
            ImageLabel image => _render.RenderImageContentImage(image.Image, image.PreRendered, image.EdgeToEdge),
            DinRailLabel dinRail => RenderDinRail(dinRail, dinRail.EdgeToEdge),
            ComposedLabel composed => Encode(_render.ComposeElementImages(RenderParts(composed))),
            _ => throw Unsupported(label)
        };

        public TwoDCodePlan Plan(TwoDCodeLabel label) => _render.PlanTwoDImage(label.Data, label.Symbology);

        public float LengthMm(decimal modules) => _render.DinRailLengthMm(modules);

        public float RequiredScale(DinRailLabel label, int cellIndex)
        {
            var cell = label.Cells[cellIndex];
            return _render.DinRailRequiredScale(cell.Text, Font(label.FontFamily), label.Style, label.UpperCase, label.Align, cell.Modules, label.EdgeToEdge, label.Formatting);
        }

        /// <summary>
        /// The image <see cref="Render"/> hands to <see cref="RenderHelper.PrepareImage"/>, and whether
        /// it already spans the full 32-dot head (<c>noCut</c>) rather than the 30 printable dots.
        /// </summary>
        private (byte[] Content, bool NoCut) RenderPrintable(Label label) => label switch
        {
            ImageLabel image => (image.Image, image.EdgeToEdge),

            // Every composed part already carries its own head-axis padding by this point - see
            // RenderHelper.ComposeElementImages.
            ComposedLabel => (RenderContent(label), true),
            TextLabel text => (RenderContent(label), text.EdgeToEdge),
            BarcodeLabel barcode => (RenderContent(label), barcode.EdgeToEdge),
            TwoDCodeLabel => (RenderContent(label), false),
            DinRailLabel dinRail => (RenderContent(label), dinRail.EdgeToEdge),
            _ => throw Unsupported(label)
        };

        private byte[] PreviewPrintable(Label label)
        {
            var (content, noCut) = RenderPrintable(label);
            return _render.PreviewImage(content, noCut, preRendered: true);
        }

        private static bool IsPhoto(Label label) => label is ImageLabel { PreRendered: false };

        /// <summary>
        /// Each part's printable content for <see cref="RenderHelper.ComposeElementImages"/> -
        /// always laid out at the printable height (<c>noCut: false</c>) so the parts line up,
        /// except images, which are composed as-is.
        /// </summary>
        private List<byte[]> RenderParts(ComposedLabel composed)
        {
            var parts = new List<byte[]>();
            foreach (var part in composed.Parts)
            {
                if (part is ComposedLabel nested)
                {
                    parts.AddRange(RenderParts(nested));
                    continue;
                }

                parts.Add(part switch
                {
                    TextLabel text => RenderText(text, noCut: false),
                    BarcodeLabel barcode => _render.RenderBarcodeContentImage(barcode.Data, barcode.Symbology, Caption(barcode), noCut: false),
                    DinRailLabel dinRail => RenderDinRail(dinRail, noCut: false),
                    ImageLabel { PreRendered: true } image => image.Image,
                    _ => RenderContent(part)
                });
            }

            return parts;
        }

        private byte[] RenderText(TextLabel text, bool noCut) =>
            _render.RenderTextContentImage(text.Text, Font(text.FontFamily), text.Size, text.Style, text.UpperCase, text.WidthScale, text.Box, text.Align, noCut, text.Formatting, text.FrameSpacing);

        private byte[] RenderDinRail(DinRailLabel dinRail, bool noCut) =>
            _render.RenderDinRailRowImage(dinRail.Cells.Select(c => (c.Text, c.Modules)).ToList(), Font(dinRail.FontFamily), dinRail.Style, dinRail.UpperCase, dinRail.Align, dinRail.Sizing, dinRail.ShowSeparators, noCut, dinRail.Formatting);

        private CaptionOptions Caption(BarcodeLabel barcode) => barcode.Caption ?? CaptionOptions.None;

        private string Font(string? fontFamily) => fontFamily ?? _defaultFontFamily;

        private static byte[] Encode(SKBitmap bitmap)
        {
            using (bitmap)
            using (var image = SKImage.FromBitmap(bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                return encoded.ToArray();
            }
        }

        private static NotSupportedException Unsupported(Label label) =>
            new($"{label.GetType().Name} isn't supported by the Dymo LetraTag 200B renderer.");
    }
}
