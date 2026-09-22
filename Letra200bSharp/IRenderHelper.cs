using System.Collections.Generic;
using SkiaSharp;

namespace Letra200bSharp
{
    /// <summary>
    /// Everything that turns label content (text, a barcode, a 2D code, a DIN rail row, a photo,
    /// or several already-rendered elements) into pixels - previews, PNG content images, and the
    /// packed <see cref="ImageInfo"/> a job is ultimately built from. Deliberately has no
    /// knowledge of the Dymo BLE wire protocol (headers, chunking, checksums) - see
    /// <see cref="ILetraHelper"/> for that half.
    /// </summary>
    public interface IRenderHelper
    {
        /// <summary>See <see cref="RenderHelper.PreviewImage(byte[], bool, bool)"/>.</summary>
        byte[] PreviewImage(byte[] imageBytes, bool noCut = false, bool preRendered = false);

        /// <summary>See <see cref="RenderHelper.PreviewImage(string, string, LetraHelper.LabelTextSize, LetraHelper.TextStyle, bool, float, LetraHelper.TextBoxStyle, LetraHelper.TextAlign, bool, LetraHelper.TextFormatting, LetraHelper.FrameSpacing)"/>.</summary>
        byte[] PreviewImage(string text, string fontFamily = "Arial", LetraHelper.LabelTextSize size = LetraHelper.LabelTextSize.M, LetraHelper.TextStyle style = LetraHelper.TextStyle.Normal, bool upperCase = false, float widthScale = 1f, LetraHelper.TextBoxStyle boxStyle = LetraHelper.TextBoxStyle.None, LetraHelper.TextAlign align = LetraHelper.TextAlign.Left, bool noCut = false, LetraHelper.TextFormatting formatting = default, LetraHelper.FrameSpacing frameSpacing = default);

        /// <summary>See <see cref="RenderHelper.PreviewImage(string, LetraHelper.BarcodeSymbology, bool, LetraHelper.CaptionOptions)"/>.</summary>
        byte[] PreviewImage(string data, LetraHelper.BarcodeSymbology symbology, bool noCut = false, LetraHelper.CaptionOptions caption = default);

        /// <summary>See <see cref="RenderHelper.PreviewImage(string, LetraHelper.TwoDSymbology)"/>.</summary>
        byte[] PreviewImage(string data, LetraHelper.TwoDSymbology symbology);

        /// <summary>See <see cref="RenderHelper.PreviewDinRailRowImage"/>.</summary>
        byte[] PreviewDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators, bool noCut = false, LetraHelper.TextFormatting formatting = default);

        /// <summary>See <see cref="RenderHelper.PreviewComposedImage"/>.</summary>
        byte[] PreviewComposedImage(IReadOnlyList<byte[]> elementPngs);

        /// <summary>See <see cref="RenderHelper.PlanTwoDImage"/>.</summary>
        LetraHelper.TwoDPlan PlanTwoDImage(string data, LetraHelper.TwoDSymbology symbology);

        /// <summary>See <see cref="RenderHelper.DinRailRequiredScale"/>.</summary>
        float DinRailRequiredScale(string text, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, decimal modules, bool noCut = false, LetraHelper.TextFormatting formatting = default);

        /// <summary>See <see cref="RenderHelper.DinRailLengthMm"/>.</summary>
        float DinRailLengthMm(decimal modules);

        /// <summary>See <see cref="RenderHelper.DinRailWidthPixels"/>.</summary>
        int DinRailWidthPixels(decimal modules);

        /// <summary>See <see cref="RenderHelper.RenderTextContentImage"/>.</summary>
        byte[] RenderTextContentImage(string text, string fontFamily, LetraHelper.LabelTextSize size, LetraHelper.TextStyle style, bool upperCase, float widthScale, LetraHelper.TextBoxStyle boxStyle, LetraHelper.TextAlign align, bool noCut, LetraHelper.TextFormatting formatting = default, LetraHelper.FrameSpacing frameSpacing = default);

        /// <summary>See <see cref="RenderHelper.RenderBarcodeContentImage"/>.</summary>
        byte[] RenderBarcodeContentImage(string data, LetraHelper.BarcodeSymbology symbology, LetraHelper.CaptionOptions caption, bool noCut);

        /// <summary>See <see cref="RenderHelper.RenderTwoDContentImage"/>.</summary>
        byte[] RenderTwoDContentImage(string data, LetraHelper.TwoDSymbology symbology);

        /// <summary>See <see cref="RenderHelper.RenderImageContentImage"/>.</summary>
        byte[] RenderImageContentImage(byte[] imageBytes, bool preRendered, bool noCut);

        /// <summary>See <see cref="RenderHelper.RenderDinRailRowImage"/>. Exposed (rather than kept private) so <see cref="ILetraHelper.CreateDinRailRowJob"/> can build a job from it.</summary>
        byte[] RenderDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators, bool noCut, LetraHelper.TextFormatting formatting = default);

        /// <summary>See <see cref="RenderHelper.PrepareImage"/>. Exposed (rather than kept private) so <see cref="ILetraHelper.CreateJob(byte[], bool, bool)"/> can pack it into a job.</summary>
        ImageInfo PrepareImage(byte[] imageBytes, bool noCut = false, bool preRendered = false);

        /// <summary>See <see cref="RenderHelper.ComposeElementImages"/>. Exposed (rather than kept private) so <see cref="ILetraHelper.CreateComposedJob"/> can build a job from it.</summary>
        SKBitmap ComposeElementImages(IReadOnlyList<byte[]> elementPngs);
    }
}
