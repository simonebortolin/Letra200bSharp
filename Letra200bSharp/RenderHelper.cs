using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CodeGlyphX;
using CodeGlyphX.DataMatrix;
using CodeGlyphX.UpcE;

namespace Letra200bSharp
{
    /// <summary>
    /// Turns label content into pixels - see <see cref="IRenderHelper"/> for the contract and why
    /// this is kept separate from <see cref="LetraHelper"/>'s BLE wire-protocol concerns.
    /// </summary>
    public class RenderHelper : IRenderHelper
    {
        /// <summary>
        /// Minimum luma distance between Otsu's two cluster means for the split to count as real
        /// content - below it the image is treated as uniform. Well under a yellow-on-white logo's
        /// ~29 (which must still print) and well above JPEG noise or paper shading.
        /// </summary>
        private const double MinOtsuClusterContrast = 20;

        /// <summary>
        /// Picks the luma cutoff (0..255) that best splits <paramref name="histogram"/> (256
        /// bins, one per luma value 0..255) into a darker and a lighter cluster - Otsu's method:
        /// the split that maximizes the variance between the two clusters' means, which in
        /// practice finds the valley between an image's actual dark/light regions instead of
        /// assuming a fixed midpoint like 128 is always where content and background happen to
        /// split. A flat 128 cutoff alone isn't enough: a light-but-distinct color (a yellow logo,
        /// say, even composited over a transparent background that reads as white) can sit
        /// entirely above 128 in absolute luma, and would vanish under a fixed threshold even
        /// though it's clearly its own cluster once you look at where the image's actual
        /// brightness values fall rather than an arbitrary fixed number. Falls back to the old
        /// fixed 128 when there's no real split to find: an empty/single-bin histogram, or one
        /// whose best two clusters are less than <see cref="MinOtsuClusterContrast"/> apart - Otsu
        /// always splits somewhere, so without this a near-uniform image (off-white paper, JPEG
        /// noise, a faint gradient) would get its background split into black blobs.
        /// </summary>
        /// <returns>The luma value V such that darker-than-V pixels are the ink and V-or-lighter pixels are the background - see <see cref="PrepareBitmap"/>'s use of it.</returns>
        private int ComputeOtsuThreshold(int[] histogram)
        {
            long total = 0;
            for (int i = 0; i < 256; i++)
            {
                total += histogram[i];
            }

            if (total == 0)
            {
                return 128;
            }

            double sumAll = 0;
            for (int i = 0; i < 256; i++)
            {
                sumAll += (double)i * histogram[i];
            }

            long weightDarker = 0;
            double sumDarker = 0;
            double bestVariance = -1;
            double bestMeanDelta = 0;
            int bestSplit = 127;

            for (int t = 0; t < 255; t++)
            {
                weightDarker += histogram[t];
                if (weightDarker == 0)
                {
                    continue;
                }

                long weightLighter = total - weightDarker;
                if (weightLighter == 0)
                {
                    break;
                }

                sumDarker += (double)t * histogram[t];
                double meanDarker = sumDarker / weightDarker;
                double meanLighter = (sumAll - sumDarker) / weightLighter;
                double meanDelta = meanDarker - meanLighter;
                double variance = weightDarker * (double)weightLighter * meanDelta * meanDelta;

                if (variance > bestVariance)
                {
                    bestVariance = variance;
                    bestMeanDelta = -meanDelta;
                    bestSplit = t;
                }
            }

            if (bestMeanDelta < MinOtsuClusterContrast)
            {
                return 128;
            }

            // Luma <= bestSplit is the darker cluster (ink); the caller compares with "<", so +1
            // puts the split value itself on the darker/ink side.
            return bestSplit + 1;
        }

        /// <summary>
        /// Prepare the bitmap that will actually be sent to the printer, in three steps:
        /// - Convert to 1-bit monochrome
        /// - Rotate the image
        /// - Resize it to the printer's pixel width (with the unprintable-edge padding
        ///   handling described by <paramref name="noCut"/>)
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="noCut">
        /// The printer requires 32 pixels (4 bytes) per row, but only the middle 30 pixels
        /// are actually printable: the first and last pixel row get cut off the label. By
        /// default the content is resized to 30 pixels and padded with 1 blank pixel on each
        /// side, so it's the padding that gets missed, not the image. Set to <c>true</c> if
        /// <paramref name="imageBytes"/> already accounts for this (e.g. it already has a
        /// blank first and last row) so it should be resized to the full 32 pixels as-is.
        /// </param>
        /// <param name="preRendered">
        /// Set to <c>true</c> if <paramref name="imageBytes"/> was already rendered for
        /// printing by an external tool (e.g. ImageMagick, as in
        /// https://github.com/ysfchn/dymo-bluetooth: <c>convert ... -resize x30 ... -type
        /// bilevel</c>), meaning it is already thresholded and already sized so its short
        /// side (the printer's head axis) is 30 (or 32 pixels, combined with
        /// <paramref name="noCut"/> <c>true</c>) pixels. In that case the label-length axis
        /// is kept pixel-exact instead of being rescaled by the heuristic used for arbitrary
        /// source photos.
        /// </param>
        /// <returns></returns>
        private SKBitmap PrepareBitmap(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            // Load image from byte array
            using (var stream = new System.IO.MemoryStream(imageBytes))
            {
                using (var skiaImage = SKBitmap.Decode(stream))
                {
                    // Convert to 1-bit monochrome. Two passes: first compute every pixel's
                    // perceptual brightness (luma) composited over a white background (a
                    // transparent pixel is blank label, not whatever its RGB happens to be),
                    // then threshold against Otsu's method rather than a flat 128 cutoff - see
                    // ComputeOtsuThreshold for why a fixed midpoint isn't good enough on its own:
                    // a light-but-distinct color (e.g. a yellow logo, even on a transparent
                    // background that composites to white) can sit entirely above 128 itself,
                    // and would vanish under a fixed threshold even though it's clearly its own
                    // cluster, separate from the background, once you look at the image's actual
                    // brightness distribution instead of an arbitrary fixed number.
                    int sourceWidth = skiaImage.Width;
                    int sourceHeight = skiaImage.Height;
                    var luma = new byte[sourceWidth, sourceHeight];
                    var histogram = new int[256];
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        for (int y = 0; y < sourceHeight; y++)
                        {
                            var color = skiaImage.GetPixel(x, y);
                            float alpha = color.Alpha / 255f;
                            float compositedLuma = (0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue) * alpha + 255 * (1 - alpha);
                            byte l = (byte)Math.Clamp((int)MathF.Round(compositedLuma), 0, 255);
                            luma[x, y] = l;
                            histogram[l]++;
                        }
                    }

                    int threshold = ComputeOtsuThreshold(histogram);

                    var monoBitmap = new SKBitmap(sourceWidth, sourceHeight);
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        for (int y = 0; y < sourceHeight; y++)
                        {
                            monoBitmap.SetPixel(x, y, luma[x, y] < threshold ? SKColors.Black : SKColors.White);
                        }
                    }

                    // Rotate the image (the printer expects a portrait image)
                    monoBitmap = RotateBitmap(monoBitmap, 270);

                    const int printerWidth = 32;
                    const int printableWidth = 30;
                    int targetWidth = noCut ? printerWidth : printableWidth;

                    if (preRendered && monoBitmap.Width != targetWidth)
                    {
                        // preRendered means the caller already sized the image's head-axis
                        // (short) side themselves - if it doesn't match, the rest of this
                        // method would silently stretch/squash it back to targetWidth
                        // (since preRendered skips the aspect-ratio-preserving height
                        // calculation below), corrupting an image that was deliberately
                        // pixel-exact. Fail loudly instead.
                        throw new ArgumentException(
                            $"preRendered image must have its head-axis (short) side exactly " +
                            $"{targetWidth} pixels ({(noCut ? "32 pixels, since noCut is true" : "30 pixels, since noCut is false")}), " +
                            $"but it was {monoBitmap.Width}.",
                            nameof(imageBytes));
                    }

                    // The printer's head-axis dots are physically twice the size of its
                    // feed-axis (label length) dots - two length-axis pixels cover the same
                    // physical distance as one head-axis pixel - so preserving the source
                    // photo's visual aspect ratio needs a factor of 2 here, against whichever
                    // width this bitmap actually gets resized to below (this used to be a
                    // constant "64" - correct only for the old always-32-wide behavior, not
                    // the 30-wide content path used when noCut is false).
                    int height = preRendered
                        ? monoBitmap.Height
                        : (int)(2 * targetWidth * ((float)monoBitmap.Height / monoBitmap.Width));

                    if (noCut)
                    {
                        // The caller already accounted for the unprintable first/last row,
                        // so resize straight to the full 32 pixels expected by the printer.
                        return monoBitmap.Resize(new SKImageInfo(printerWidth, height), SKSamplingOptions.Default);
                    }

                    // Resize the content to 30 pixels and pad it with 1 blank pixel on
                    // each side, so it's the padding that gets missed, not the image.
                    var resizedBitmap = monoBitmap.Resize(new SKImageInfo(printableWidth, height), SKSamplingOptions.Default);

                    var paddedBitmap = new SKBitmap(printerWidth, height);
                    using (var canvas = new SKCanvas(paddedBitmap))
                    {
                        canvas.Clear(SKColors.White);
                        canvas.DrawBitmap(resizedBitmap, 1, 0, SKSamplingOptions.Default);
                    }

                    return paddedBitmap;
                }
            }
        }

        /// <summary>
        /// Prepare image by converting the printed bitmap (see <see cref="PrepareBitmap"/>)
        /// into an array of 1s and 0s. Public (rather than kept private) so
        /// <see cref="ILetraHelper.CreateJob(byte[], bool, bool)"/> can pack the result into a job.
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="noCut">See <see cref="PrepareBitmap"/>.</param>
        /// <param name="preRendered">See <see cref="PrepareBitmap"/>.</param>
        /// <returns></returns>
        public ImageInfo PrepareImage(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            using (var finalBitmap = PrepareBitmap(imageBytes, noCut, preRendered))
            {
                // Convert the image to an array of 1s and 0s
                byte[] data = new byte[finalBitmap.Width * finalBitmap.Height];
                for (int i = 0; i < finalBitmap.Width; i++)
                {
                    for (int j = 0; j < finalBitmap.Height; j++)
                    {
                        var pixel = finalBitmap.GetPixel(i, j);
                        data[i + j * finalBitmap.Width] = pixel.Red < 128 ? (byte)1 : (byte)0;
                    }
                }

                return new ImageInfo { Width = finalBitmap.Height, Height = finalBitmap.Width, Data = data };
            }
        }

        /// <summary>
        /// Renders a PNG preview of what will be printed on the label, built directly from
        /// the same bit array (<see cref="ImageInfo.Data"/>) packed by
        /// <see cref="LetraHelper.CreateJob(byte[], bool, bool)"/>: a black pixel is exactly a dot
        /// that is sent to the printer as "print". Regardless of <paramref name="noCut"/>, the
        /// printer's first and last pixel row are never actually printed, so those two rows
        /// are always rendered as a distinct gray marker instead of their raw bit value, to
        /// keep the preview faithful to what will physically appear on the label. The result
        /// is built in the same portrait layout as the bitmap sent to the printer (head axis
        /// on X, label-length axis on Y) and then rotated with <see cref="RotateBitmap"/> -
        /// the same real geometric rotation used to prepare the source photo, as opposed to a
        /// naive coordinate swap which would mirror the image - to end up in landscape, the
        /// same orientation as the physical label. Finally it's scaled up by
        /// <see cref="PreviewScale"/> using nearest-neighbor sampling (no blending, so each
        /// printer dot stays a crisp block instead of a blurry gradient), giving a fixed,
        /// predictable output height of 32 * <see cref="PreviewScale"/> pixels. The printer's
        /// head-axis dots are physically twice the size of its feed-axis (label length) dots
        /// (see <see cref="PrepareBitmap"/>), so the length axis is scaled at half that rate -
        /// otherwise a label that prints as a physical square would show up on screen as a
        /// rectangle twice as wide as it is tall.
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="noCut">See <see cref="PrepareBitmap"/>.</param>
        /// <param name="preRendered">See <see cref="PrepareBitmap"/>.</param>
        /// <returns>PNG-encoded bytes of the preview image</returns>
        public byte[] PreviewImage(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            var imageInfo = PrepareImage(imageBytes, noCut, preRendered);
            var clippedRowColor = new SKColor(224, 224, 224);

            using (var portraitBitmap = new SKBitmap(imageInfo.Height, imageInfo.Width))
            {
                for (int head = 0; head < imageInfo.Height; head++)
                {
                    bool isClippedRow = head == 0 || head == imageInfo.Height - 1;
                    for (int length = 0; length < imageInfo.Width; length++)
                    {
                        SKColor color;
                        if (isClippedRow)
                        {
                            color = clippedRowColor;
                        }
                        else
                        {
                            byte bit = imageInfo.Data[head + length * imageInfo.Height];
                            color = bit == 1 ? SKColors.Black : SKColors.White;
                        }
                        portraitBitmap.SetPixel(head, length, color);
                    }
                }

                int lengthAxisScale = PreviewScale / 2;
                using (var landscapeBitmap = RotateBitmap(portraitBitmap, 90))
                using (var scaledBitmap = landscapeBitmap.Resize(
                    new SKImageInfo(landscapeBitmap.Width * lengthAxisScale, landscapeBitmap.Height * PreviewScale),
                    new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                using (var image = SKImage.FromBitmap(scaledBitmap))
                using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                {
                    return encoded.ToArray();
                }
            }
        }

        /// <summary>
        /// Integer, nearest-neighbor upscale factor applied to <see cref="PreviewImage(byte[], bool, bool)"/>'s
        /// output, so it has a fixed pixel height (32 * this) and stays crisp instead of
        /// blurring when a UI stretches a 32px-tall bitmap to fill a much larger area.
        /// </summary>
        public const int PreviewScale = 4;

        /// <summary>
        /// Rotates a SkiaSharp SKBitmap object
        /// </summary>
        /// <param name="bitmap">The bitmap to rotate</param>
        /// <param name="degrees">Rotation angle in degrees</param>
        /// <returns>Rotated version of the SKBitmap object</returns>
        private SKBitmap RotateBitmap(SKBitmap bitmap, float degrees)
        {
            float radians = MathF.PI * degrees / 180;
            float sine = MathF.Abs(MathF.Sin(radians));
            float cosine = MathF.Abs(MathF.Cos(radians));
            int originalWidth = bitmap.Width;
            int originalHeight = bitmap.Height;
            int rotatedWidth = (int)(cosine * originalWidth + sine * originalHeight);
            int rotatedHeight = (int)(cosine * originalHeight + sine * originalWidth);

            var rotatedBitmap = new SKBitmap(rotatedWidth, rotatedHeight);

            using (var surface = new SKCanvas(rotatedBitmap))
            {
                surface.Clear();
                surface.Translate(rotatedWidth / 2, rotatedHeight / 2);
                surface.RotateDegrees((float)degrees);
                surface.Translate(-originalWidth / 2, -originalHeight / 2);
                surface.DrawBitmap(bitmap, new SKPoint(), SKSamplingOptions.Default);
            }

            return rotatedBitmap;
        }

        /// <summary>How far a narrower element sits from a wider one's left edge, as a fraction of the leftover space - 0 for Left, 0.5 for Center, 1 for Right.</summary>
        private float AlignFactor(LetraHelper.TextAlign align) => align switch
        {
            LetraHelper.TextAlign.Center => 0.5f,
            LetraHelper.TextAlign.Right => 1f,
            _ => 0f
        };

        /// <summary>Width, in millimeters, of one DIN rail mounting module (EN 50022 terminal block pitch).</summary>
        public const float DinRailModuleWidthMm = 18f;

        /// <summary>
        /// Feed-axis (label length) pixel pitch, in pixels per millimeter. Empirically
        /// calibrated against real printouts rather than derived from the printer's protocol
        /// docs or theoretical estimates (both of which turned out wrong - see below):
        /// - 1st pass: at the original theoretical estimate (5 px/mm, from "feed-axis dots are
        ///   physically twice the size of head-axis dots"), a 1-module (18 mm) and a 2-module
        ///   (36 mm) DIN rail label measured 7 mm and 14 mm long instead - consistently 7/18 of
        ///   the intended length - giving a corrected estimate of 5 * 18/7 = 90/7 px/mm.
        /// - 2nd pass: printing at that 90/7 px/mm estimate (231 px for 1 module) measured
        ///   18.5 mm instead of the intended 18 mm - i.e. the real pitch is 18.5/231 mm/px, so
        ///   the correct px/mm is its reciprocal, 231/18.5 = 462/37.
        /// </summary>
        private const float FeedAxisPixelsPerMm = 462f / 37f;

        /// <summary>Physical printed length, in millimeters, of a DIN rail label spanning <paramref name="modules"/> modules.</summary>
        public float DinRailLengthMm(decimal modules) => (float)modules * DinRailModuleWidthMm;

        /// <summary>Feed-axis pixel width corresponding to <see cref="DinRailLengthMm"/>.</summary>
        public int DinRailWidthPixels(decimal modules) => Math.Max(1, (int)MathF.Round(DinRailLengthMm(modules) * FeedAxisPixelsPerMm));

        private int DecodeWidth(byte[] image)
        {
            using (var bitmap = SKBitmap.Decode(image))
            {
                return bitmap.Width;
            }
        }

        /// <summary>
        /// Below this required scale (target width / natural width), a single line is considered
        /// cramped enough that <see cref="RenderDinRailSegmentNatural"/> will look for a two-line
        /// wrap instead. A one-line render that already only needs light shrinking (e.g. 90% of
        /// natural size) reads perfectly fine on its own - wrapping it would just split it into
        /// two lines each with less height to work with, for no real benefit, so wrapping is only
        /// worth it once one line would otherwise be squeezed a lot.
        /// </summary>
        private const float DinRailWrapConsiderationThreshold = 0.6f;

        /// <summary>
        /// Renders <paramref name="text"/> at its natural, undistorted size - always
        /// <c>widthScale = 1</c>, so glyphs keep their normal proportions - auto-wrapping onto
        /// two lines at a word boundary (whichever split measures narrowest) instead, but only
        /// when staying on one line would need heavy shrinking to fit <paramref name="wrapTargetWidthPx"/>
        /// (see <see cref="DinRailWrapConsiderationThreshold"/>) and a wrap actually improves on
        /// that. The result can still end up wider than <paramref name="wrapTargetWidthPx"/> (e.g.
        /// one long word that can't be split, or wrapping not being enough on its own) - callers
        /// scale the whole image down uniformly afterward if needed (see <see cref="RenderDinRailRowImage"/>),
        /// which is what keeps the 1:1 aspect ratio intact instead of stretching only the width.
        /// Renders at <see cref="LetraHelper.LabelTextSize.XL"/> (edge-to-edge, no blank margin
        /// around the glyphs) rather than the Text tab's usual size presets - the padding ratio
        /// doesn't change how wide the natural rendering ends up (it scales height and width the
        /// same way, so it cancels out once everything is fit to the target height), but it does
        /// change how much of that width is actual glyph ink versus blank margin; DIN segments
        /// are already tightly space-constrained, so giving the glyphs the full share keeps them
        /// bolder/more legible once a segment needs to be scaled down (and separators, when on,
        /// already carve out their own small gap - see <see cref="DinRailSeparatorWidthPx"/>). A
        /// blank/whitespace-only row (used as a spacer) renders as a minimal blank image.
        /// </summary>
        private byte[] RenderDinRailSegmentNatural(string text, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, int wrapTargetWidthPx, bool noCut, LetraHelper.TextFormatting formatting = default)
        {
            int targetHeight = noCut ? 32 : 30;

            if (string.IsNullOrWhiteSpace(text))
            {
                using (var blank = new SKBitmap(1, targetHeight))
                {
                    using (var canvas = new SKCanvas(blank))
                    {
                        canvas.Clear(SKColors.White);
                    }

                    using (var image = SKImage.FromBitmap(blank))
                    using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        return encoded.ToArray();
                    }
                }
            }

            byte[] oneLineImage = RenderTextContentImage(text, fontFamily, LetraHelper.LabelTextSize.XL, style, upperCase, 1f, LetraHelper.TextBoxStyle.None, align, noCut, formatting);
            if (!text.Contains(' '))
            {
                return oneLineImage;
            }

            int oneLineWidth = DecodeWidth(oneLineImage);
            float oneLineScale = Math.Min(1f, wrapTargetWidthPx / (float)oneLineWidth);
            if (oneLineScale >= DinRailWrapConsiderationThreshold)
            {
                // Already fits, or only needs light shrinking - not worth splitting into two
                // shorter (and therefore individually smaller) lines.
                return oneLineImage;
            }

            byte[] bestImage = oneLineImage;
            int bestWidth = oneLineWidth;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    continue;
                }

                string candidate = text.Substring(0, i) + "\n" + text.Substring(i + 1);
                byte[] candidateImage = RenderTextContentImage(candidate, fontFamily, LetraHelper.LabelTextSize.XL, style, upperCase, 1f, LetraHelper.TextBoxStyle.None, align, noCut, formatting);
                int candidateWidth = DecodeWidth(candidateImage);
                if (candidateWidth < bestWidth)
                {
                    bestWidth = candidateWidth;
                    bestImage = candidateImage;
                }
            }

            return bestImage;
        }

        /// <summary>
        /// Estimates how much a DIN rail segment's text would need to shrink to fit
        /// <paramref name="modules"/> modules (see <see cref="DinRailWidthPixels"/>) - 1.0 means
        /// it already fits (or has room to spare), lower means it needs shrinking (see
        /// <see cref="RenderDinRailRowImage"/>). Meant for UI feedback (e.g. warning the user a
        /// label is too long for its module count to stay legible) rather than for the actual
        /// rendering pipeline, so it ignores the couple of pixels a separator line would carve
        /// out of the segment.
        /// </summary>
        public float DinRailRequiredScale(string text, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, decimal modules, bool noCut = false, LetraHelper.TextFormatting formatting = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 1f;
            }

            int targetWidthPx = DinRailWidthPixels(modules);
            byte[] naturalImage = RenderDinRailSegmentNatural(text, fontFamily, style, upperCase, align, targetWidthPx, noCut, formatting);
            int naturalWidth = DecodeWidth(naturalImage);
            return naturalWidth <= targetWidthPx ? 1f : targetWidthPx / (float)naturalWidth;
        }

        /// <summary>How many feed-axis pixels a printed separator line between two adjacent DIN rail segments takes up.</summary>
        private const int DinRailSeparatorWidthPx = 2;

        /// <summary>
        /// Composes <paramref name="rows"/> into a single continuous DIN rail strip image, one
        /// segment per row (see <see cref="DinRailWidthPixels"/> for each row's segment width),
        /// drawn left to right into one bitmap. Each row's text is rendered at its natural,
        /// undistorted size (see <see cref="RenderDinRailSegmentNatural"/>) and then, only if it
        /// doesn't already fit, scaled down - uniformly in both directions, never stretched - by
        /// whatever factor <paramref name="sizing"/> calls for (see <see cref="LetraHelper.DinRailSizing"/>),
        /// then centered vertically and aligned horizontally (<paramref name="align"/>) within its
        /// segment. When <paramref name="showSeparators"/> is set, a thin vertical line is printed
        /// at the boundary between each pair of adjacent segments, carved out of the earlier
        /// segment's own available width rather than added on top of it, so segment widths always
        /// still sum to the requested total physical length. Public (rather than kept private) so
        /// <see cref="ILetraHelper.CreateDinRailRowJob"/> can build a job from it.
        /// </summary>
        public byte[] RenderDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators, bool noCut, LetraHelper.TextFormatting formatting = default)
        {
            int targetHeight = noCut ? 32 : 30;
            float alignFactor = AlignFactor(align);

            var segmentWidths = new int[rows.Count];
            var naturalImages = new byte[rows.Count][];
            var naturalWidths = new int[rows.Count];
            var requiredScales = new float[rows.Count];

            for (int i = 0; i < rows.Count; i++)
            {
                segmentWidths[i] = DinRailWidthPixels(rows[i].Modules);
                bool drawSeparatorAfter = showSeparators && i < rows.Count - 1;
                int textWidthPx = Math.Max(1, segmentWidths[i] - (drawSeparatorAfter ? DinRailSeparatorWidthPx : 0));

                naturalImages[i] = RenderDinRailSegmentNatural(rows[i].Text, fontFamily, style, upperCase, align, textWidthPx, noCut, formatting);
                naturalWidths[i] = DecodeWidth(naturalImages[i]);
                requiredScales[i] = string.IsNullOrWhiteSpace(rows[i].Text) || naturalWidths[i] <= textWidthPx
                    ? 1f
                    : textWidthPx / (float)naturalWidths[i];
            }

            float uniformScale = requiredScales.Where((_, i) => !string.IsNullOrWhiteSpace(rows[i].Text)).DefaultIfEmpty(1f).Min();

            var segments = new List<SKBitmap>();
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    float scale = sizing == LetraHelper.DinRailSizing.Uniform ? uniformScale : requiredScales[i];
                    bool drawSeparatorAfter = showSeparators && i < rows.Count - 1;

                    var segmentBitmap = new SKBitmap(segmentWidths[i], targetHeight);
                    using (var naturalBitmap = SKBitmap.Decode(naturalImages[i]))
                    using (var canvas = new SKCanvas(segmentBitmap))
                    {
                        canvas.Clear(SKColors.White);

                        int scaledWidth = Math.Max(1, (int)MathF.Round(naturalBitmap.Width * scale));
                        int scaledHeight = Math.Max(1, (int)MathF.Round(naturalBitmap.Height * scale));
                        using (var scaledBitmap = Math.Abs(scale - 1f) < 0.001f
                            ? null
                            : naturalBitmap.Resize(new SKImageInfo(scaledWidth, scaledHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                        {
                            var toDraw = scaledBitmap ?? naturalBitmap;
                            float x = (segmentWidths[i] - toDraw.Width) * alignFactor;
                            float y = (targetHeight - toDraw.Height) / 2f;
                            canvas.DrawBitmap(toDraw, x, y, SKSamplingOptions.Default);
                        }

                        if (drawSeparatorAfter)
                        {
                            using (var separatorPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
                            {
                                canvas.DrawRect(new SKRect(segmentWidths[i] - DinRailSeparatorWidthPx, 0, segmentWidths[i], targetHeight), separatorPaint);
                            }
                        }
                    }

                    segments.Add(segmentBitmap);
                }

                int totalWidth = Math.Max(segments.Sum(s => s.Width), 1);
                using (var finalBitmap = new SKBitmap(totalWidth, targetHeight))
                {
                    using (var canvas = new SKCanvas(finalBitmap))
                    {
                        canvas.Clear(SKColors.White);
                        int x = 0;
                        foreach (var segment in segments)
                        {
                            canvas.DrawBitmap(segment, x, 0, SKSamplingOptions.Default);
                            x += segment.Width;
                        }
                    }

                    using (var image = SKImage.FromBitmap(finalBitmap))
                    using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        return encoded.ToArray();
                    }
                }
            }
            finally
            {
                foreach (var segment in segments)
                {
                    segment.Dispose();
                }
            }
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="ILetraHelper.CreateDinRailRowJob"/> would
        /// print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the rendered label</returns>
        public byte[] PreviewDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators, bool noCut = false, LetraHelper.TextFormatting formatting = default)
        {
            byte[] imageBytes = RenderDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, noCut, formatting);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }

        private float GetPaddingRatio(LetraHelper.LabelTextSize size)
        {
            switch (size)
            {
                case LetraHelper.LabelTextSize.XS: return 0.6f;
                case LetraHelper.LabelTextSize.S: return 0.35f;
                case LetraHelper.LabelTextSize.M: return 0.15f;
                case LetraHelper.LabelTextSize.L: return 0.05f;
                case LetraHelper.LabelTextSize.XL: return 0f;
                default: return 0.15f;
            }
        }

        /// <summary>
        /// Target per-line pixel height (out of the default 30px printable height) for the
        /// two-line case: how much of the fixed height each of the two lines gets once the
        /// gap between them (see <see cref="RenderTextContentImage"/>) is subtracted out.
        /// </summary>
        private int GetTwoLineHeight(LetraHelper.LabelTextSize size)
        {
            switch (size)
            {
                case LetraHelper.LabelTextSize.XS: return 12;
                case LetraHelper.LabelTextSize.S: return 13;
                case LetraHelper.LabelTextSize.M: return 14;
                case LetraHelper.LabelTextSize.L: return 15;
                case LetraHelper.LabelTextSize.XL: return 15;
                default: return 14;
            }
        }

        /// <summary>
        /// Renders <paramref name="text"/> as black text on a white background (one or more
        /// lines, stacked), then scales it so its height matches the target printable pixel
        /// height (30, or 32 if <paramref name="noCut"/> is <c>true</c>), keeping the aspect
        /// ratio - equivalent to ImageMagick's <c>-resize x30</c>. Also the "just the printable
        /// content" renderer for a Text-tab-style Compose element (see <see cref="ComposeElementImages"/>).
        /// </summary>
        /// <returns>PNG-encoded bytes of the rendered label</returns>
        public byte[] RenderTextContentImage(string text, string fontFamily, LetraHelper.LabelTextSize size, LetraHelper.TextStyle style, bool upperCase, float widthScale, LetraHelper.TextBoxStyle boxStyle, LetraHelper.TextAlign align, bool noCut, LetraHelper.TextFormatting formatting = default, LetraHelper.FrameSpacing frameSpacing = default)
        {
            if (upperCase)
            {
                text = text.ToUpperInvariant();
            }

            bool rotate90 = style == LetraHelper.TextStyle.Vertical;
            if (rotate90)
            {
                text = string.Join(Environment.NewLine, text.ToCharArray());
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');

            const float renderFontSize = 96;
            int targetHeight = noCut ? 32 : 30;
            // Bold/Italic are independently toggleable (see LetraHelper.TextFormatting) and
            // combine freely with each other and with style's Outline/Shadow/Vertical - SkiaSharp
            // already supports a BoldItalic weight/slant combination directly.
            var fontStyle = new SKFontStyle(
                formatting.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                formatting.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            // Extra gap after every glyph, in the same pre-scale units as everything else here
            // (a fraction of renderFontSize, the em size) - 0 reproduces the exact pre-existing
            // single-DrawText-call layout below.
            float letterSpacingPx = formatting.LetterSpacing * renderFontSize;
            float paddingRatio = GetPaddingRatio(size);

            using (var typeface = SKTypeface.FromFamilyName(fontFamily, fontStyle))
            using (var font = new SKFont(typeface, renderFontSize) { ScaleX = widthScale })
            // No antialiasing: this gets downscaled to a handful of pixels tall (a couple of
            // lines can be well under 10px each), and antialiased gray edges plus a smooth
            // resize filter would get hard-thresholded to black/white afterwards anyway
            // (see PrepareBitmap) - producing noisy, near-random pixels instead of clean
            // glyph shapes. Rendering with hard edges from the start keeps it legible.
            using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
            // Underline/strikethrough are drawn as solid rects regardless of paint.Style (which
            // Outline flips to Stroke-only) - a dedicated always-Fill paint keeps them solid.
            using (var linePaint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill })
            {
                if (style == LetraHelper.TextStyle.Outline)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = renderFontSize * 0.06f;
                }

                float shadowOffset = style == LetraHelper.TextStyle.Shadow ? renderFontSize * 0.08f : 0f;

                font.GetFontMetrics(out SKFontMetrics metrics);
                float ascent = -metrics.Ascent;
                float lineHeight = ascent + metrics.Descent;

                // Splits a line into its individual text elements (grapheme clusters) so
                // per-glyph spacing doesn't split a surrogate pair/combining mark apart.
                static IReadOnlyList<string> TextElements(string line)
                {
                    var elements = new List<string>();
                    var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(line);
                    while (enumerator.MoveNext())
                    {
                        elements.Add((string)enumerator.Current);
                    }
                    return elements;
                }

                float MeasureLine(string line)
                {
                    if (letterSpacingPx == 0f)
                    {
                        return font.MeasureText(line, paint);
                    }

                    var elements = TextElements(line);
                    float width = elements.Sum(e => font.MeasureText(e, paint));
                    return width + Math.Max(0, elements.Count - 1) * letterSpacingPx;
                }

                // Draws one line glyph-by-glyph when letter spacing is in play (so the extra gap
                // can be inserted between each pair), or as a single DrawText call otherwise -
                // letterSpacingPx == 0 reproduces the exact pre-existing layout/pixels.
                void DrawLine(SKCanvas canvas, string line, float x, float y)
                {
                    if (letterSpacingPx == 0f)
                    {
                        canvas.DrawText(line, x, y, SKTextAlign.Left, font, paint);
                        return;
                    }

                    float cx = x;
                    foreach (var element in TextElements(line))
                    {
                        canvas.DrawText(element, cx, y, SKTextAlign.Left, font, paint);
                        cx += font.MeasureText(element, paint) + letterSpacingPx;
                    }
                }

                float[] lineWidths = lines.Select(MeasureLine).ToArray();
                float maxLineWidth = lineWidths.Max();

                // How far a line's own left edge sits from the widest line's left edge, as a
                // fraction of the leftover space - 0 for Left (flush with the widest line), 0.5
                // for Center, 1 for Right - so lines shorter than maxLineWidth (e.g. two lines
                // of very different length) align relative to each other instead of always
                // flushing left.
                float alignFactor = AlignFactor(align);

                float horizontalPadding = maxLineWidth * paddingRatio;

                // The "Line 1 / Line 2" two-line case gets its own vertical layout: no
                // top/bottom margin at all, and instead of a size-dependent outer padding,
                // a size-dependent gap *only between the two lines* is used to hit a target
                // per-line pixel height (see GetTwoLineHeight) once everything is scaled down
                // to the fixed printable height below.
                float verticalPadding = 0f;
                float lineGap = 0f;
                if (lines.Length == 2)
                {
                    int targetPerLine = GetTwoLineHeight(size);
                    int targetGapPixels = Math.Clamp(targetHeight - 2 * targetPerLine, 0, targetHeight - 1);
                    if (size == LetraHelper.LabelTextSize.XL)
                    {
                        // XL is the size DIN Rail's auto-wrap always renders at (see
                        // RenderDinRailSegmentNatural) - its gap felt too roomy there, so pin it
                        // to a small fixed pixel target instead of the tuned-for-Text-tab
                        // formula above. Scoped to XL only so Text tab's manually-typed
                        // Line1/Line2 at the other sizes (already tuned/confirmed - see below)
                        // stays exactly as it was.
                        targetGapPixels = 0;
                    }
                    // Solve for the pre-scale gap that becomes targetGapPixels after the
                    // final uniform resize-to-targetHeight step below (scale = targetHeight /
                    // (2*lineHeight + lineGap)); algebraically:
                    //   lineGap * targetHeight = targetGapPixels * (2*lineHeight + lineGap)
                    lineGap = targetGapPixels <= 0
                        ? 0f
                        : targetGapPixels * 2f * lineHeight / (targetHeight - targetGapPixels);
                    // A hair of top/bottom margin (~1.5 final pixels - same factor as
                    // DrawTextBox's verticalMargin below) so ascenders/descenders never land
                    // exactly on the printer's physically unprintable first/last row (see
                    // PreviewImage's remarks) - without this, the zero-margin 2-line layout can
                    // clip the top of an ascender or the tail of a descender right off. A flat
                    // ~1 final pixel isn't quite enough: rounding the raw height up (Ceiling
                    // below) to fit a whole pixel grid shaves a bit back off the final scaled
                    // margin, so 1.5 leaves enough slack to actually land clear of the edge.
                    verticalPadding = (2f * lineHeight + lineGap) / targetHeight * 1.5f;
                }
                // ascent/descent reserve room for diacritics and descenders the text may not
                // actually use, so measuring padding against them left visible slack even at
                // paddingRatio 0 (e.g. XL). Measure the real ink extent of the rendered lines
                // instead, and base the padding (and the "no padding at all" case) on that.
                // Underline/strikethrough offsets from the baseline, from the font's own metrics
                // when it reports them, else a fallback proportional to the ascent (the final
                // render gets downscaled to a handful of pixels anyway, so an approximate
                // position is enough - same spirit as the Shadow effect's fixed 8%-of-em offset
                // above).
                float underlineOffset = metrics.UnderlinePosition ?? ascent * 0.15f;
                float underlineThickness = Math.Max(Math.Abs(metrics.UnderlineThickness ?? renderFontSize * 0.06f), 1f);
                float strikeoutOffset = metrics.StrikeoutPosition ?? -ascent * 0.4f;
                float strikeoutThickness = Math.Max(Math.Abs(metrics.StrikeoutThickness ?? renderFontSize * 0.06f), 1f);

                float unpaddedContentTop = float.MaxValue;
                float unpaddedContentBottom = float.MinValue;
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineBaseline = ascent + i * (lineHeight + lineGap);
                    font.MeasureText(lines[i], out SKRect inkBounds, paint);
                    unpaddedContentTop = Math.Min(unpaddedContentTop, lineBaseline + inkBounds.Top);
                    unpaddedContentBottom = Math.Max(unpaddedContentBottom, lineBaseline + inkBounds.Bottom);
                    // Extends the ink-extent bounds by the underline's reach so an all-caps/
                    // no-descender line's underline doesn't get clipped by a crop that would
                    // otherwise assume there's nothing below the glyphs themselves.
                    if (formatting.Underline)
                    {
                        unpaddedContentBottom = Math.Max(unpaddedContentBottom, lineBaseline + underlineOffset + underlineThickness / 2f);
                    }
                }

                float contentHeight = Math.Max(unpaddedContentBottom - unpaddedContentTop, 1f);

                float verticalShift;
                if (lines.Length == 2)
                {
                    // Untouched: this layout's line pitch (via GetTwoLineHeight) was tuned
                    // and confirmed against the font-metrics baseline, not ink bounds.
                    verticalShift = verticalPadding;
                }
                else
                {
                    // Same reasoning as the 2-line branch's margin above: even at paddingRatio 0
                    // (LabelTextSize.XL, "fill the entire height") there needs to be a hair of
                    // clearance (~1.5 final pixels) so a descender/ascender doesn't land exactly
                    // on the printer's physically unprintable first/last row - XL's own ratio
                    // only wins out once it would already give more room than that.
                    verticalPadding = Math.Max(contentHeight * paddingRatio, contentHeight / targetHeight * 1.5f);
                    verticalShift = verticalPadding - unpaddedContentTop;
                }

                int width = (int)MathF.Ceiling(maxLineWidth + horizontalPadding * 2 + shadowOffset);
                int height = lines.Length == 2
                    ? (int)MathF.Ceiling(lineHeight * lines.Length + lineGap * (lines.Length - 1) + verticalPadding * 2 + shadowOffset)
                    : (int)MathF.Ceiling(contentHeight + verticalPadding * 2 + shadowOffset);

                using (var bitmap = new SKBitmap(Math.Max(width, 1), Math.Max(height, 1)))
                {
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Clear(SKColors.White);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            float x = horizontalPadding + (maxLineWidth - lineWidths[i]) * alignFactor;
                            float y = verticalShift + ascent + i * (lineHeight + lineGap);
                            if (shadowOffset > 0)
                            {
                                // Simulate a drop shadow on a 1-bit printer by drawing a
                                // second, offset copy behind the main glyphs - the overlap
                                // reads as a solid "echo" trailing each letter.
                                DrawLine(canvas, lines[i], x + shadowOffset, y + shadowOffset);
                            }
                            DrawLine(canvas, lines[i], x, y);

                            if (formatting.Underline)
                            {
                                canvas.DrawRect(new SKRect(x, y + underlineOffset - underlineThickness / 2f, x + lineWidths[i], y + underlineOffset + underlineThickness / 2f), linePaint);
                            }
                            if (formatting.Strikethrough)
                            {
                                canvas.DrawRect(new SKRect(x, y + strikeoutOffset - strikeoutThickness / 2f, x + lineWidths[i], y + strikeoutOffset + strikeoutThickness / 2f), linePaint);
                            }
                        }

                        if (boxStyle != LetraHelper.TextBoxStyle.None)
                        {
                            // Aim for a border that's still ~1.5px wide once this raw canvas
                            // gets scaled down to targetHeight.
                            float borderStrokeWidth = Math.Max(bitmap.Height / (float)targetHeight * 1.5f, 1f);
                            // Keep the border off the printer's unprintable top/bottom row
                            // (see PrepareBitmap): without this, the border sits exactly at
                            // the edge of the 30 printable rows instead of visibly inside
                            // them. ~1.5px of margin once scaled down to targetHeight.
                            float verticalMargin = bitmap.Height / (float)targetHeight * 1.5f;
                            // frameSpacing is authored in final printer dots - convert to this
                            // raw canvas's pre-scale units the same way verticalMargin already
                            // does (bitmap.Height / targetHeight is the pre-scale-per-final-dot
                            // factor; horizontal uses the *2 head/feed-axis correction the final
                            // resize below applies).
                            float perDotY = bitmap.Height / (float)targetHeight;
                            float perDotX = perDotY * 2f;
                            var scaledFrameSpacing = new LetraHelper.FrameSpacing(
                                (int)MathF.Round(frameSpacing.Top * perDotY),
                                (int)MathF.Round(frameSpacing.Bottom * perDotY),
                                (int)MathF.Round(frameSpacing.Left * perDotX),
                                (int)MathF.Round(frameSpacing.Right * perDotX));
                            DrawTextBox(canvas, boxStyle, bitmap.Width, bitmap.Height, borderStrokeWidth, verticalMargin, scaledFrameSpacing);
                        }
                    }

                    SKBitmap finalBitmap;
                    if (rotate90)
                    {
                        // One character per line makes the block tall & narrow (rather than
                        // wide & short like normal text), so scale by width instead of
                        // height - the "thickness" of one line is what must fit the target
                        // height, not the number of stacked lines. The *2 mirrors the same
                        // head-axis/feed-axis physical pixel size correction as PrepareBitmap:
                        // targetHeight pixels here become head-axis dots, each twice the
                        // physical size of a feed-axis (scaledHeight) dot.
                        int scaledHeight = Math.Max((int)MathF.Round(2 * targetHeight * ((float)bitmap.Height / bitmap.Width)), 1);
                        using (var scaledBitmap = bitmap.Resize(new SKImageInfo(targetHeight, scaledHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                        {
                            // Pre-rotate 90° so the image ends up in the landscape
                            // (width=length, height=target) shape the pipeline expects.
                            // PrepareBitmap always rotates by another 270° internally, and
                            // 90+270=360 cancels out, so the glyphs stay upright.
                            finalBitmap = RotateBitmap(scaledBitmap, 90);
                        }
                    }
                    else
                    {
                        // Same head-axis/feed-axis physical pixel size correction as
                        // PrepareBitmap and the rotate90 branch above: targetHeight pixels
                        // become head-axis dots, each twice the physical size of a feed-axis
                        // (scaledWidth) dot, so a naive aspect-preserving resize (without the
                        // *2) would render glyphs visibly squashed once printed.
                        int scaledWidth = Math.Max((int)MathF.Round(2 * targetHeight * ((float)bitmap.Width / bitmap.Height)), 1);
                        finalBitmap = bitmap.Resize(new SKImageInfo(scaledWidth, targetHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
                    }

                    using (finalBitmap)
                    using (var image = SKImage.FromBitmap(finalBitmap))
                    using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        return encoded.ToArray();
                    }
                }
            }
        }

        /// <summary>Draws a decorative border/underline around the full rendered text canvas.</summary>
        private void DrawTextBox(SKCanvas canvas, LetraHelper.TextBoxStyle boxStyle, float width, float height, float strokeWidth, float verticalMargin, LetraHelper.FrameSpacing frameSpacing = default)
        {
            using (var borderPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth })
            {
                float inset = strokeWidth / 2f;
                var rect = new SKRect(
                    inset + frameSpacing.Left,
                    inset + verticalMargin + frameSpacing.Top,
                    width - inset - frameSpacing.Right,
                    height - inset - verticalMargin - frameSpacing.Bottom);
                // A user-driven frameSpacing can, in principle, invert the rect (e.g. asking for
                // more margin than the canvas has room for) - clamp rather than let SkiaSharp draw
                // an inverted/degenerate path.
                if (rect.Width < 1 || rect.Height < 1)
                {
                    rect = new SKRect(inset, inset + verticalMargin, width - inset, height - inset - verticalMargin);
                }

                switch (boxStyle)
                {
                    case LetraHelper.TextBoxStyle.Underline:
                        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Bottom, borderPaint);
                        break;
                    case LetraHelper.TextBoxStyle.Square:
                        canvas.DrawRect(rect, borderPaint);
                        break;
                    case LetraHelper.TextBoxStyle.Rounded:
                        float radius = rect.Height * 0.35f;
                        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
                        break;
                    case LetraHelper.TextBoxStyle.Pointed:
                        DrawPointedBox(canvas, borderPaint, rect);
                        break;
                    case LetraHelper.TextBoxStyle.Edged:
                        DrawZigzagBox(canvas, borderPaint, rect, toothSpan: rect.Height * 0.7f, amplitude: rect.Height * 0.12f);
                        break;
                    case LetraHelper.TextBoxStyle.Crocodile:
                        DrawZigzagBox(canvas, borderPaint, rect, toothSpan: rect.Height, amplitude: rect.Height * 0.3f);
                        break;
                    case LetraHelper.TextBoxStyle.Heart:
                        DrawHeartBox(canvas, borderPaint, rect);
                        break;
                    case LetraHelper.TextBoxStyle.Star:
                        DrawPerimeterSpikeBox(canvas, borderPaint, rect, spikeSpan: Math.Max(rect.Height * 0.6f, 1f), amplitude: Math.Min(rect.Width, rect.Height) * 0.14f);
                        break;
                    case LetraHelper.TextBoxStyle.Flower:
                        DrawPerimeterSpikeBox(canvas, borderPaint, rect, spikeSpan: Math.Max(rect.Height * 0.28f, 1f), amplitude: Math.Min(rect.Width, rect.Height) * 0.05f);
                        break;
                    case LetraHelper.TextBoxStyle.Ribbon:
                        DrawRibbonBox(canvas, borderPaint, rect);
                        break;
                }
            }
        }

        /// <summary>A box with the left and right edges drawn to a point instead of a flat side, like a tag/pennant.</summary>
        private void DrawPointedBox(SKCanvas canvas, SKPaint paint, SKRect rect)
        {
            float pointWidth = Math.Min(rect.Height * 0.35f, rect.Width / 4);
            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(rect.Left + pointWidth, rect.Top);
            pathBuilder.LineTo(rect.Right - pointWidth, rect.Top);
            pathBuilder.LineTo(rect.Right, rect.MidY);
            pathBuilder.LineTo(rect.Right - pointWidth, rect.Bottom);
            pathBuilder.LineTo(rect.Left + pointWidth, rect.Bottom);
            pathBuilder.LineTo(rect.Left, rect.MidY);
            pathBuilder.Close();
            using (var path = pathBuilder.Detach())
            {
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>
        /// A box whose top and bottom edges are a triangular zigzag instead of a flat line
        /// (used for both "Edged" - small/frequent teeth - and "Crocodile" - larger teeth -
        /// via different <paramref name="toothSpan"/>/<paramref name="amplitude"/>).
        /// </summary>
        private void DrawZigzagBox(SKCanvas canvas, SKPaint paint, SKRect rect, float toothSpan, float amplitude)
        {
            int toothCount = Math.Max((int)MathF.Round(rect.Width / toothSpan), 2);
            float toothWidth = rect.Width / toothCount;

            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(rect.Left, rect.Top + amplitude / 2);
            for (int i = 0; i < toothCount; i++)
            {
                float xMid = rect.Left + i * toothWidth + toothWidth / 2f;
                float xEnd = rect.Left + (i + 1) * toothWidth;
                float yPeak = i % 2 == 0 ? rect.Top : rect.Top + amplitude;
                pathBuilder.LineTo(xMid, yPeak);
                pathBuilder.LineTo(xEnd, rect.Top + amplitude / 2);
            }

            pathBuilder.LineTo(rect.Right, rect.Bottom - amplitude / 2);

            for (int i = toothCount - 1; i >= 0; i--)
            {
                float xStart = rect.Left + i * toothWidth;
                float xMid = xStart + toothWidth / 2f;
                float yPeak = i % 2 == 0 ? rect.Bottom : rect.Bottom - amplitude;
                pathBuilder.LineTo(xMid, yPeak);
                pathBuilder.LineTo(xStart, rect.Bottom - amplitude / 2);
            }

            pathBuilder.Close();
            using (var path = pathBuilder.Detach())
            {
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>
        /// A rounded box with a V-shaped notch cut into the top-center edge and a point at the
        /// bottom-center - a heart-like silhouette (cleft top, pointed bottom) built entirely from
        /// straight chamfered edges, same low-res-friendly approach as <see cref="DrawZigzagBox"/>,
        /// so it stays inside the same canvas every other box style already fits within rather than
        /// needing a literal curved heart outline.
        /// </summary>
        private void DrawHeartBox(SKCanvas canvas, SKPaint paint, SKRect rect)
        {
            float chamfer = Math.Min(rect.Height * 0.28f, rect.Width * 0.2f);
            float notchWidth = Math.Min(rect.Height * 0.5f, rect.Width * 0.3f);
            float notchDepth = rect.Height * 0.22f;
            float bottomPointWidth = Math.Min(rect.Height * 0.5f, rect.Width * 0.3f);

            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(rect.Left + chamfer, rect.Top);
            pathBuilder.LineTo(rect.MidX - notchWidth / 2f, rect.Top);
            pathBuilder.LineTo(rect.MidX, rect.Top + notchDepth);
            pathBuilder.LineTo(rect.MidX + notchWidth / 2f, rect.Top);
            pathBuilder.LineTo(rect.Right - chamfer, rect.Top);
            pathBuilder.LineTo(rect.Right, rect.Top + chamfer);
            pathBuilder.LineTo(rect.Right, rect.Bottom - chamfer);
            pathBuilder.LineTo(rect.MidX + bottomPointWidth / 2f, rect.Bottom - chamfer);
            pathBuilder.LineTo(rect.MidX, rect.Bottom);
            pathBuilder.LineTo(rect.MidX - bottomPointWidth / 2f, rect.Bottom - chamfer);
            pathBuilder.LineTo(rect.Left, rect.Bottom - chamfer);
            pathBuilder.LineTo(rect.Left, rect.Top + chamfer);
            pathBuilder.Close();
            using (var path = pathBuilder.Detach())
            {
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>
        /// A rect whose full perimeter (all four sides, unlike <see cref="DrawZigzagBox"/>'s
        /// top/bottom-only teeth) is a triangular spike pattern - used for both "Star" (sparse,
        /// tall spikes) and "Flower" (dense, shallow spikes/scallops) via different
        /// <paramref name="spikeSpan"/>/<paramref name="amplitude"/>, the same "one shape, two
        /// tunings" approach <see cref="DrawZigzagBox"/> already uses for Edged/Crocodile.
        /// </summary>
        private void DrawPerimeterSpikeBox(SKCanvas canvas, SKPaint paint, SKRect rect, float spikeSpan, float amplitude)
        {
            int hCount = Math.Max((int)MathF.Round(rect.Width / spikeSpan), 2);
            int vCount = Math.Max((int)MathF.Round(rect.Height / spikeSpan), 2);
            float hStep = rect.Width / hCount;
            float vStep = rect.Height / vCount;

            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(rect.Left, rect.Top);
            for (int i = 0; i < hCount; i++)
            {
                float xMid = rect.Left + i * hStep + hStep / 2f;
                float xEnd = rect.Left + (i + 1) * hStep;
                pathBuilder.LineTo(xMid, rect.Top - amplitude);
                pathBuilder.LineTo(xEnd, rect.Top);
            }
            for (int i = 0; i < vCount; i++)
            {
                float yMid = rect.Top + i * vStep + vStep / 2f;
                float yEnd = rect.Top + (i + 1) * vStep;
                pathBuilder.LineTo(rect.Right + amplitude, yMid);
                pathBuilder.LineTo(rect.Right, yEnd);
            }
            for (int i = hCount - 1; i >= 0; i--)
            {
                float xMid = rect.Left + i * hStep + hStep / 2f;
                float xStart = rect.Left + i * hStep;
                pathBuilder.LineTo(xMid, rect.Bottom + amplitude);
                pathBuilder.LineTo(xStart, rect.Bottom);
            }
            for (int i = vCount - 1; i >= 0; i--)
            {
                float yMid = rect.Top + i * vStep + vStep / 2f;
                float yStart = rect.Top + i * vStep;
                pathBuilder.LineTo(rect.Left - amplitude, yMid);
                pathBuilder.LineTo(rect.Left, yStart);
            }
            pathBuilder.Close();
            using (var path = pathBuilder.Detach())
            {
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>A banner/ribbon with a V-shaped notch cut inward from each end, like a flag's swallowtail - unlike <see cref="DrawPointedBox"/>'s outward point, the notch stays entirely within <paramref name="rect"/>.</summary>
        private void DrawRibbonBox(SKCanvas canvas, SKPaint paint, SKRect rect)
        {
            float notchDepth = Math.Min(rect.Width / 4f, rect.Height * 0.4f);

            var pathBuilder = new SKPathBuilder();
            pathBuilder.MoveTo(rect.Left, rect.Top);
            pathBuilder.LineTo(rect.Right, rect.Top);
            pathBuilder.LineTo(rect.Right - notchDepth, rect.MidY);
            pathBuilder.LineTo(rect.Right, rect.Bottom);
            pathBuilder.LineTo(rect.Left, rect.Bottom);
            pathBuilder.LineTo(rect.Left + notchDepth, rect.MidY);
            pathBuilder.Close();
            using (var path = pathBuilder.Detach())
            {
                canvas.DrawPath(path, paint);
            }
        }

        /// <summary>
        /// Maps our curated <see cref="LetraHelper.BarcodeSymbology"/> to CodeGlyphX's
        /// <see cref="BarcodeType"/>. Both <see cref="LetraHelper.BarcodeSymbology.Ean13"/> and
        /// <see cref="LetraHelper.BarcodeSymbology.Ean8"/> map to <see cref="BarcodeType.EAN"/> -
        /// CodeGlyphX's EAN encoder picks 8 vs 13 from the digit count, and
        /// <see cref="ResolveAutoSymbology"/>/the encoder itself already validate that.
        /// </summary>
        private BarcodeType ToCodeGlyphXType(LetraHelper.BarcodeSymbology symbology) => symbology switch
        {
            LetraHelper.BarcodeSymbology.Code128 => BarcodeType.Code128,
            LetraHelper.BarcodeSymbology.Code39 => BarcodeType.Code39,
            LetraHelper.BarcodeSymbology.Codabar => BarcodeType.Codabar,
            LetraHelper.BarcodeSymbology.Itf => BarcodeType.ITF,
            LetraHelper.BarcodeSymbology.Ean13 => BarcodeType.EAN,
            LetraHelper.BarcodeSymbology.Ean8 => BarcodeType.EAN,
            LetraHelper.BarcodeSymbology.UpcA => BarcodeType.UPCA,
            LetraHelper.BarcodeSymbology.UpcE => BarcodeType.UPCE,
            _ => throw new ArgumentOutOfRangeException(nameof(symbology))
        };

        /// <summary>
        /// CodeGlyphX's UPC-E encoder takes the 6-digit payload plus an explicit number system
        /// (0 or 1), whereas this app - like the Dymo app, and ZXing before it - works with the
        /// full 8-digit UPC-E (number system + 6-digit payload + check digit). Accept the 8-digit
        /// form (validating its check digit against the expanded UPC-A, the same way
        /// <see cref="ResolveAutoSymbology"/> does) and split it; pass anything else straight
        /// through for CodeGlyphX to validate.
        /// </summary>
        private Barcode1D EncodeUpcE(string data)
        {
            if (data.Length == 8 && data.All(char.IsDigit))
            {
                if (!TryExpandUpcEToUpcA(data, out var upcA) || !HasValidMod10Checksum(upcA))
                {
                    throw new FormatException("An 8-digit UPC-E must be a valid number system (0 or 1), a 6-digit payload, and a matching check digit.");
                }

                var numberSystem = data[0] == '1' ? UpcENumberSystem.One : UpcENumberSystem.Zero;
                return BarcodeEncoder.EncodeUpcE(data.Substring(1, 6), numberSystem);
            }

            return BarcodeEncoder.EncodeUpcE(data);
        }

        /// <summary>
        /// GS1 mod-10 check digit validation shared by EAN-13, EAN-8 and UPC-A (and, via
        /// <see cref="TryExpandUpcEToUpcA"/>, UPC-E): starting from the digit right before the
        /// trailing check digit, weights alternate 3/1 going leftwards; the check digit is
        /// whatever makes the weighted sum a multiple of 10.
        /// </summary>
        /// <param name="digits">All-numeric string, check digit included as the last character.</param>
        private bool HasValidMod10Checksum(string digits)
        {
            int payloadLength = digits.Length - 1;
            int sum = 0;
            for (int i = 0; i < payloadLength; i++)
            {
                int digit = digits[payloadLength - 1 - i] - '0';
                sum += digit * (i % 2 == 0 ? 3 : 1);
            }

            int checkDigit = (10 - sum % 10) % 10;
            return checkDigit == digits[payloadLength] - '0';
        }

        /// <summary>
        /// Expands an 8-digit UPC-E code (number system + 6-digit compressed manufacturer/product
        /// code + check digit) to its equivalent 12-digit UPC-A, per the standard suppressed-zero
        /// table keyed on the compressed code's last digit. The result's own check digit is just
        /// UPC-E's check digit carried across unchanged - <see cref="HasValidMod10Checksum"/>
        /// still needs to be run on it to confirm it's actually correct.
        /// </summary>
        private bool TryExpandUpcEToUpcA(string upcE, [NotNullWhen(true)] out string? upcA)
        {
            upcA = null;
            if (upcE.Length != 8 || (upcE[0] != '0' && upcE[0] != '1'))
                return false;

            char numberSystem = upcE[0];
            string manufacturer = upcE.Substring(1, 6);
            char checkDigit = upcE[7];

            string expanded = manufacturer[5] switch
            {
                '0' or '1' or '2' => manufacturer.Substring(0, 2) + manufacturer[5] + "0000" + manufacturer.Substring(2, 3),
                '3' => manufacturer.Substring(0, 3) + "00000" + manufacturer.Substring(3, 2),
                '4' => manufacturer.Substring(0, 4) + "00000" + manufacturer.Substring(4, 1),
                _ => manufacturer.Substring(0, 5) + "0000" + manufacturer[5]
            };

            upcA = numberSystem + expanded + checkDigit;
            return true;
        }

        /// <summary>
        /// Resolves <see cref="LetraHelper.BarcodeSymbology.Auto"/> to a concrete symbology based
        /// on <paramref name="data"/>'s shape: digits-only data whose length exactly matches a
        /// checksum-carrying symbology (Ean13/UpcA/Ean8/UpcE, checked narrowest-length-first) and
        /// whose check digit validates picks that symbology; other digits-only data falls back to
        /// Itf (needs an even digit count) or, failing that, Code128; non-digit data picks Code39
        /// if every character fits its charset, else Code128 - same fallback ZXing's own encoder
        /// would need since Code128 accepts the full ASCII range.
        /// </summary>
        private LetraHelper.BarcodeSymbology ResolveAutoSymbology(string data)
        {
            if (string.IsNullOrEmpty(data))
                return LetraHelper.BarcodeSymbology.Code128;

            if (data.All(char.IsDigit))
            {
                if (data.Length == 13 && HasValidMod10Checksum(data))
                    return LetraHelper.BarcodeSymbology.Ean13;
                if (data.Length == 12 && HasValidMod10Checksum(data))
                    return LetraHelper.BarcodeSymbology.UpcA;
                // UpcE checked before Ean8: an 8-digit code starting 2-9 can only be Ean8 (UpcE's
                // number-system digit is restricted to 0/1, enforced by TryExpandUpcEToUpcA's own
                // guard), so this ordering doesn't change anything for those. One starting 0/1
                // could legitimately be either - UpcE, or an EAN-8 restricted-circulation/
                // internal-use number, which conventionally also starts with 0 - so the leading
                // digit alone can't break the tie. UpcE wins it: it's a real public retail
                // symbology meant to be scanned by anyone, whereas EAN-8's restricted-circulation
                // range is by definition for a single company's internal use and not something a
                // generic label-printing tool would plausibly be asked to auto-detect.
                if (data.Length == 8 && TryExpandUpcEToUpcA(data, out var expanded) && HasValidMod10Checksum(expanded))
                    return LetraHelper.BarcodeSymbology.UpcE;
                if (data.Length == 8 && HasValidMod10Checksum(data))
                    return LetraHelper.BarcodeSymbology.Ean8;

                return data.Length % 2 == 0 ? LetraHelper.BarcodeSymbology.Itf : LetraHelper.BarcodeSymbology.Code128;
            }

            const string code39Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";
            if (data.All(c => code39Charset.Contains(char.ToUpperInvariant(c))))
                return LetraHelper.BarcodeSymbology.Code39;

            return LetraHelper.BarcodeSymbology.Code128;
        }

        /// <summary>
        /// How many printer dots wide the narrowest barcode module (bar/space unit) gets
        /// scaled to. Encoding at the natural 1 dot per module (see <see cref="RenderBarcodeContentImage"/>)
        /// would make the narrowest bars a single pixel wide - technically correct but too thin
        /// to print/scan reliably.
        /// </summary>
        private const int BarcodeModuleScale = 4;

        /// <summary>Fraction of the printable height given to an enabled caption (see <see cref="LetraHelper.CaptionOptions"/>) - kept small by default since the bars are the actual scannable payload and the caption is just a human-readable aid; the rest of the height goes to the bars themselves.</summary>
        private float GetCaptionHeightRatio(LetraHelper.CaptionSize size) => size switch
        {
            LetraHelper.CaptionSize.S => 0.15f,
            LetraHelper.CaptionSize.M => 0.2f,
            LetraHelper.CaptionSize.L => 0.3f,
            _ => 0.2f
        };

        /// <summary>
        /// Blank row(s) between the bars and the caption (see <see cref="RenderBarcodeContentImage"/>) -
        /// scoped to just that boundary, not extra margin around the whole barcode.
        /// </summary>
        private const int BarcodeNumberGapPx = 1;

        /// <summary>
        /// Renders <paramref name="data"/> at the smallest legible height, bold, in
        /// <paramref name="fontFamily"/> - the caption printed alongside the bars when
        /// <see cref="RenderBarcodeContentImage"/>'s <c>caption</c> isn't <see cref="LetraHelper.CaptionPosition.None"/>.
        /// </summary>
        /// <param name="numberHeight">Final pixel height of the caption (see <see cref="GetCaptionHeightRatio"/>).</param>
        private SKBitmap RenderBarcodeNumberBitmap(string data, string fontFamily, int numberHeight)
        {
            // Rendered through the normal text pipeline at full quality/height first (XL - no
            // padding, glyphs get the full share of the height - and Bold), then scaled down as
            // a whole to the small height actually reserved for it, same trick
            // RenderDinRailSegmentNatural uses: keeps the digits' proportions correct instead of
            // asking SkiaSharp's font layout to work at a height too small to be meaningful.
            byte[] fullHeightPng = RenderTextContentImage(data, fontFamily, LetraHelper.LabelTextSize.XL, LetraHelper.TextStyle.Normal, upperCase: false, widthScale: 1f, LetraHelper.TextBoxStyle.None, LetraHelper.TextAlign.Center, noCut: false, formatting: new LetraHelper.TextFormatting(Bold: true));
            using (var fullHeightBitmap = SKBitmap.Decode(fullHeightPng))
            {
                int width = Math.Max(1, (int)MathF.Round(fullHeightBitmap.Width * (numberHeight / (float)fullHeightBitmap.Height)));
                return fullHeightBitmap.Resize(new SKImageInfo(width, numberHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
        }

        /// <summary>
        /// Renders <paramref name="data"/> as a black-on-white 1D barcode using CodeGlyphX, sized
        /// so its bars exactly match the printer's fixed printable height (30, or 32 if
        /// <paramref name="noCut"/>). Also the "just the printable content" renderer for a
        /// Barcode-tab-style Compose element (see <see cref="ComposeElementImages"/>).
        /// </summary>
        /// <param name="caption">
        /// Whether/how to print <paramref name="data"/> itself as a small caption alongside the
        /// bars (see <see cref="RenderBarcodeNumberBitmap"/>) - unlike the real Dymo app, off by
        /// default (bars only).
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/> (e.g. non-numeric EAN/UPC data, or the wrong digit count).</exception>
        /// <returns>PNG-encoded bytes of the rendered barcode</returns>
        public byte[] RenderBarcodeContentImage(string data, LetraHelper.BarcodeSymbology symbology, LetraHelper.CaptionOptions caption, bool noCut)
        {
            LetraHelper.BarcodeSymbology resolvedSymbology = symbology == LetraHelper.BarcodeSymbology.Auto ? ResolveAutoSymbology(data) : symbology;
            int targetHeight = noCut ? 32 : 30;
            bool showCaption = caption.Position != LetraHelper.CaptionPosition.None;
            // How tall a notch to leave for the caption, once its column range is known below -
            // not a height the bars themselves are ever encoded/resized to (see below: bars
            // always render at the full targetHeight; only the width directly under the caption
            // gets cut down to make room for it, everywhere else stays full height). The notch
            // sits at the top edge for Above, the bottom edge for Below.
            int captionHeight = showCaption ? Math.Max(4, (int)MathF.Round(targetHeight * GetCaptionHeightRatio(caption.Size))) : 0;
            int notchStartY = caption.Position == LetraHelper.CaptionPosition.Above ? 0 : targetHeight - captionHeight;

            Barcode1D barcode;
            try
            {
                // CodeGlyphX's 1D encoders hand back the bare bar/space module run - no quiet
                // zone, no human-readable digits - which is exactly what we want (the equivalent
                // of ZXing's PURE_BARCODE hint). The natural, unstretched pattern is scaled back
                // up ourselves below via a crisp nearest-neighbor resize, matching the rest of
                // this file's approach to keeping bars/edges sharp, rather than letting the
                // library rasterize it.
                barcode = resolvedSymbology == LetraHelper.BarcodeSymbology.UpcE
                    ? EncodeUpcE(data)
                    : BarcodeEncoder.Encode(ToCodeGlyphXType(resolvedSymbology), data);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{data}' isn't valid {(symbology == LetraHelper.BarcodeSymbology.Auto ? $"{resolvedSymbology} (auto-detected)" : symbology.ToString())} barcode data: {ex.Message}", nameof(data), ex);
            }

            int moduleCount = barcode.TotalModules;
            // A 1-pixel-tall row of the bar pattern; the vertical (head) axis is filled in by the
            // nearest-neighbor resize to targetHeight below, since a 1D barcode's bars just run
            // the full label height.
            using (var barsBitmap = new SKBitmap(moduleCount, 1))
            {
                int column = 0;
                foreach (var segment in barcode.Segments)
                {
                    var color = segment.IsBar ? SKColors.Black : SKColors.White;
                    for (int i = 0; i < segment.Modules; i++)
                    {
                        barsBitmap.SetPixel(column++, 0, color);
                    }
                }

                int barsWidth = moduleCount * BarcodeModuleScale;
                using (var scaledBars = barsBitmap.Resize(new SKImageInfo(barsWidth, targetHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                {
                    if (!showCaption)
                    {
                        using (var image = SKImage.FromBitmap(scaledBars))
                        using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                        {
                            return encoded.ToArray();
                        }
                    }

                    // A blank row between the bars and the caption, carved out of the caption's
                    // own reserved height rather than added on top of it - only the caption's own
                    // area gets tighter to make room for the gap next to the bars. It sits at
                    // whichever edge of the notch actually borders the bars: the far edge from
                    // the bars for Below (bars end, then gap, then caption flush with the bottom
                    // edge), the near edge for Above (caption flush with the top edge, then gap,
                    // then bars resume).
                    int gapHeight = Math.Min(BarcodeNumberGapPx, captionHeight - 1);
                    int textHeight = captionHeight - gapHeight;

                    using (var numberBitmap = RenderBarcodeNumberBitmap(data, caption.FontFamily, textHeight))
                    {
                        int finalWidth = Math.Max(barsWidth, numberBitmap.Width);
                        // Whichever of the bars/caption is narrower gets lined up against the
                        // wider one per caption.Align, same left/center/right convention as the
                        // rest of the app (e.g. DIN Rail's own AlignFactor usage).
                        float barsX = (finalWidth - barsWidth) * AlignFactor(caption.Align);
                        float numberX = (finalWidth - numberBitmap.Width) * AlignFactor(caption.Align);
                        float numberY = caption.Position == LetraHelper.CaptionPosition.Above ? notchStartY : notchStartY + gapHeight;

                        using (var finalBitmap = new SKBitmap(finalWidth, targetHeight))
                        using (var canvas = new SKCanvas(finalBitmap))
                        {
                            canvas.Clear(SKColors.White);
                            canvas.DrawBitmap(scaledBars, barsX, 0, SKSamplingOptions.Default);

                            // Cut a notch only under the caption's own width, so the bars stay
                            // full height everywhere else - blank out whatever bars would
                            // otherwise show through there before drawing the caption over the
                            // clear space.
                            using (var notchPaint = new SKPaint { Color = SKColors.White, IsAntialias = false })
                            {
                                canvas.DrawRect(new SKRect(numberX, notchStartY, numberX + numberBitmap.Width, notchStartY + captionHeight), notchPaint);
                            }
                            canvas.DrawBitmap(numberBitmap, numberX, numberY, SKSamplingOptions.Default);

                            using (var image = SKImage.FromBitmap(finalBitmap))
                            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                            {
                                return encoded.ToArray();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="ILetraHelper.CreateJob(string, LetraHelper.BarcodeSymbology, bool, LetraHelper.CaptionOptions)"/>
        /// would print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/>.</exception>
        public byte[] PreviewImage(string data, LetraHelper.BarcodeSymbology symbology, bool noCut = false, LetraHelper.CaptionOptions caption = default)
        {
            byte[] imageBytes = RenderBarcodeContentImage(data, symbology, caption, noCut);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="ILetraHelper.CreateJob(string, string, LetraHelper.LabelTextSize, LetraHelper.TextStyle, bool, float, LetraHelper.TextBoxStyle, LetraHelper.TextAlign, bool)"/>
        /// would print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image</returns>
        public byte[] PreviewImage(string text, string fontFamily = "Arial", LetraHelper.LabelTextSize size = LetraHelper.LabelTextSize.M, LetraHelper.TextStyle style = LetraHelper.TextStyle.Normal, bool upperCase = false, float widthScale = 1f, LetraHelper.TextBoxStyle boxStyle = LetraHelper.TextBoxStyle.None, LetraHelper.TextAlign align = LetraHelper.TextAlign.Left, bool noCut = false, LetraHelper.TextFormatting formatting = default, LetraHelper.FrameSpacing frameSpacing = default)
        {
            byte[] imageBytes = RenderTextContentImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, noCut, formatting, frameSpacing);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }

        // ---------------------------------------------------------------------------------
        // 2D matrix symbologies (QR, Micro QR, rMQR, Data Matrix)
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Printer dots available on the head axis (across the tape) for a 2D symbol. The head
        /// axis is physically 32 dots but only the middle 30 print (see <see cref="PrepareBitmap"/>);
        /// a matrix code can't afford to lose its outer ring, so it's always rendered into 30 and
        /// the pipeline's own 1-dot padding on each side (which only ever adds quiet zone) takes
        /// it to 32. The quiet zone itself is free here - everything around the symbol is bare,
        /// unprinted tape.
        /// </summary>
        private const int TwoDHeadBudgetPixels = 30;

        /// <summary>
        /// Largest QR version <see cref="LetraHelper.TwoDSymbology.QrCode"/> will encode: V3 is 29
        /// modules, the most that fit <see cref="TwoDHeadBudgetPixels"/> at 1 dot/module. (QR's
        /// 21-module minimum already rules out 2 dots/module, so every QR here is in the
        /// <see cref="LetraHelper.TwoDPlan.MayNotScanWell"/> tier.)
        /// </summary>
        private const int TwoDMaxQrVersion = 3;

        /// <summary>The QR/Micro QR alphanumeric-mode character set (uppercase only) - ISO/IEC 18004 Table 5.</summary>
        private const string QrAlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

        /// <summary>
        /// rMQR <see cref="RmQrEncodingOptions.MaximumVersion"/> caps, one per symbol-height bucket
        /// (heights 7, 9, 11, 13, 15, 17 - see ISO/IEC 23941). <see cref="EncodeRmQr"/> tries these
        /// smallest-first: forcing the encoder to a shorter symbol whenever the data still fits is
        /// what maximizes dots-per-module (R7 → 4, R9 → 3, R11/R13/R15 → 2, R17 → 1).
        /// </summary>
        private readonly int[] TwoDRmQrMaxVersionByHeightBucket = { 5, 10, 16, 22, 27, 32 };

        /// <summary>
        /// Encodes <paramref name="data"/> as the requested 2D symbology and returns its raw
        /// module matrix (no quiet zone) plus a display name. <see cref="LetraHelper.TwoDSymbology.Auto"/>
        /// is resolved here - see <see cref="EncodeTwoDAuto"/>.
        /// </summary>
        private (BitMatrix Modules, LetraHelper.TwoDSymbology Resolved, string Name) EncodeTwoD(string data, LetraHelper.TwoDSymbology symbology)
        {
            if (string.IsNullOrEmpty(data))
            {
                throw new ArgumentException("No data to encode.", nameof(data));
            }

            return symbology switch
            {
                LetraHelper.TwoDSymbology.Auto => EncodeTwoDAuto(data),
                LetraHelper.TwoDSymbology.QrCode => EncodeQr(data),
                LetraHelper.TwoDSymbology.MicroQrCode => EncodeMicroQr(data),
                LetraHelper.TwoDSymbology.RectangularMicroQrCode => EncodeRmQr(data),
                LetraHelper.TwoDSymbology.DataMatrix => EncodeDataMatrix(data),
                _ => throw new ArgumentOutOfRangeException(nameof(symbology))
            };
        }

        private (BitMatrix, LetraHelper.TwoDSymbology, string) EncodeQr(string data)
        {
            QrCode qr;
            try
            {
                qr = QrCodeEncoder.EncodeText(data, QrErrorCorrectionLevel.M, minVersion: 1, maxVersion: TwoDMaxQrVersion);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{data}' doesn't fit a QR code of version {TwoDMaxQrVersion} or lower (the largest that fits this printer): {ex.Message}. Try rMQR or Data Matrix instead.", nameof(data), ex);
            }

            return (qr.Modules, LetraHelper.TwoDSymbology.QrCode, $"QR V{qr.Version}");
        }

        private (BitMatrix, LetraHelper.TwoDSymbology, string) EncodeMicroQr(string data)
        {
            MicroQrCode code;
            try
            {
                if (data.All(char.IsDigit))
                {
                    code = MicroQrCodeEncoder.EncodeNumeric(data);
                }
                else if (data.All(c => QrAlphanumericCharset.Contains(c)))
                {
                    code = MicroQrCodeEncoder.EncodeAlphanumeric(data);
                }
                else
                {
                    code = MicroQrCodeEncoder.EncodeText(data);
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{data}' doesn't fit a Micro QR code (M1-M4): {ex.Message}. Try rMQR or Data Matrix instead.", nameof(data), ex);
            }

            return (code.Modules, LetraHelper.TwoDSymbology.MicroQrCode, $"Micro QR M{code.Version}");
        }

        private (BitMatrix, LetraHelper.TwoDSymbology, string) EncodeRmQr(string data)
        {
            Exception? lastError = null;
            foreach (int maxVersion in TwoDRmQrMaxVersionByHeightBucket)
            {
                try
                {
                    var code = RmQrCodeEncoder.EncodeText(data, new RmQrEncodingOptions
                    {
                        ErrorCorrectionLevel = QrErrorCorrectionLevel.M,
                        Mode = RmQrEncodingMode.Auto,
                        MinimumVersion = 1,
                        MaximumVersion = maxVersion
                    });
                    return (code.Modules, LetraHelper.TwoDSymbology.RectangularMicroQrCode, $"rMQR {code.VersionName}");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new ArgumentException($"'{data}' doesn't fit any rMQR symbol: {lastError?.Message}. Try Data Matrix, or a full QR code.", nameof(data), lastError);
        }

        private (BitMatrix, LetraHelper.TwoDSymbology, string) EncodeDataMatrix(string data)
        {
            BitMatrix matrix;
            try
            {
                matrix = DataMatrixCode.Encode(data, new DataMatrixEncodingOptions { Shape = DataMatrixShape.Square });
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{data}' can't be encoded as a Data Matrix: {ex.Message}", nameof(data), ex);
            }

            return (matrix, LetraHelper.TwoDSymbology.DataMatrix, $"Data Matrix {matrix.Width}×{matrix.Height}");
        }

        /// <summary>
        /// Resolves <see cref="LetraHelper.TwoDSymbology.Auto"/>: encodes <paramref name="data"/>
        /// as rMQR, Micro QR and Data Matrix (never full QR - it's always in the
        /// <see cref="LetraHelper.TwoDPlan.MayNotScanWell"/> tier here) and returns whichever
        /// prints on the least tape (fewest feed-axis dots) while keeping every module at least 2
        /// dots wide. Ties break toward the bigger module, then toward the symbology better suited
        /// to narrow marking (rMQR, then Micro QR, then Data Matrix). If nothing clears the 2-dot
        /// bar, the data is too long - the caller has to pick a symbology explicitly to accept a
        /// smaller, less reliable code.
        /// </summary>
        private (BitMatrix, LetraHelper.TwoDSymbology, string) EncodeTwoDAuto(string data)
        {
            (Func<(BitMatrix, LetraHelper.TwoDSymbology, string)> Encode, int Preference)[] attempts =
            {
                (() => EncodeRmQr(data), 0),
                (() => EncodeMicroQr(data), 1),
                (() => EncodeDataMatrix(data), 2),
            };

            (BitMatrix Modules, LetraHelper.TwoDSymbology Resolved, string Name, int FeedDots, int Dots, int Preference)? best = null;
            var errors = new List<string>();

            foreach (var (encode, preference) in attempts)
            {
                try
                {
                    var (modules, resolved, name) = encode();
                    var plan = BuildTwoDPlan(modules, resolved, name);
                    if (plan.MayNotScanWell)
                    {
                        errors.Add($"{name} would print at ~{plan.ModuleSizeMm:0.00} mm/module - too small to scan reliably");
                        continue;
                    }

                    int feedDots = plan.ModulesLong * plan.DotsPerModuleLong;
                    if (best is null
                        || feedDots < best.Value.FeedDots
                        || (feedDots == best.Value.FeedDots && plan.DotsPerModuleShort > best.Value.Dots)
                        || (feedDots == best.Value.FeedDots && plan.DotsPerModuleShort == best.Value.Dots && preference < best.Value.Preference))
                    {
                        best = (modules, resolved, name, feedDots, plan.DotsPerModuleShort, preference);
                    }
                }
                catch (ArgumentException ex)
                {
                    errors.Add(ex.Message);
                }
            }

            if (best is null)
            {
                throw new ArgumentException(
                    "No 2D symbology fits this data at a module size that scans reliably on this printer. Shorten the data, or pick QR / Micro QR / rMQR explicitly to accept a smaller, less reliable code. Details: "
                    + string.Join("; ", errors),
                    nameof(data));
            }

            return (best.Value.Modules, best.Value.Resolved, best.Value.Name);
        }

        /// <summary>
        /// Works out how <paramref name="modules"/> maps onto the printer: the shorter module
        /// axis goes on the head axis (the constrained one), at the largest whole number of
        /// <see cref="TwoDHeadBudgetPixels"/> dots per module that fits; the feed axis gets twice
        /// that, since feed-axis dots are half the physical size, keeping modules square.
        /// </summary>
        /// <exception cref="ArgumentException">The symbol's short side needs more modules than the head axis has dots.</exception>
        private LetraHelper.TwoDPlan BuildTwoDPlan(BitMatrix modules, LetraHelper.TwoDSymbology resolved, string name)
        {
            int shortSide = Math.Min(modules.Width, modules.Height);
            int longSide = Math.Max(modules.Width, modules.Height);

            if (shortSide > TwoDHeadBudgetPixels)
            {
                throw new ArgumentException(
                    $"{name} needs {shortSide} modules across its short side, but only {TwoDHeadBudgetPixels} printer dots are available across the tape - the data is too long for this symbology on this printer.");
            }

            int dotsPerModuleShort = TwoDHeadBudgetPixels / shortSide;
            int dotsPerModuleLong = dotsPerModuleShort * 2;

            // FeedAxisPixelsPerMm counts feed-axis dots; head-axis dots are twice the size, so the
            // head axis resolves at half as many dots per millimeter.
            float moduleSizeMm = dotsPerModuleShort / (FeedAxisPixelsPerMm / 2f);

            return new LetraHelper.TwoDPlan(resolved, name, shortSide, longSide, dotsPerModuleShort, dotsPerModuleLong, moduleSizeMm, dotsPerModuleShort <= 1);
        }

        /// <summary>
        /// Encodes <paramref name="data"/> and lays it out for the printer without rendering -
        /// see <see cref="LetraHelper.TwoDPlan"/>. Meant for UI feedback (showing the chosen
        /// symbol and warning about <see cref="LetraHelper.TwoDPlan.MayNotScanWell"/>) before
        /// committing to a print.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="data"/> can't be encoded as <paramref name="symbology"/> at a size that fits the printer.</exception>
        public LetraHelper.TwoDPlan PlanTwoDImage(string data, LetraHelper.TwoDSymbology symbology)
        {
            var (modules, resolved, name) = EncodeTwoD(data, symbology);
            return BuildTwoDPlan(modules, resolved, name);
        }

        /// <summary>
        /// Renders <paramref name="data"/> as a 2D matrix code, sized so every module is
        /// physically square and the symbol's short side exactly fills the printer's head axis
        /// (see <see cref="BuildTwoDPlan"/>). Produced in the same landscape layout as
        /// <see cref="RenderBarcodeContentImage"/> (head axis = image height = <see cref="TwoDHeadBudgetPixels"/>,
        /// feed axis = image width). A few modules of quiet zone are added on the feed axis (free
        /// - the tape is bare); the head axis relies on the surrounding unprinted tape plus the
        /// pipeline's own padding. Also the "just the printable content" renderer for a 2D-Code-
        /// tab-style Compose element (see <see cref="ComposeElementImages"/>) - always this
        /// 30-dot-tall shape, since a matrix code is never rendered at <c>noCut:true</c>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the rendered symbol.</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> can't be encoded as <paramref name="symbology"/> at a size that fits the printer.</exception>
        public byte[] RenderTwoDContentImage(string data, LetraHelper.TwoDSymbology symbology)
        {
            var (modules, resolved, name) = EncodeTwoD(data, symbology);
            var plan = BuildTwoDPlan(modules, resolved, name);

            bool matrixIsPortrait = modules.Width <= modules.Height;
            int shortSide = plan.ModulesShort;
            int longSide = plan.ModulesLong;
            int dotsShort = plan.DotsPerModuleShort;
            int dotsLong = plan.DotsPerModuleLong;

            int quietModules = resolved == LetraHelper.TwoDSymbology.QrCode ? 4 : 2;
            int quietFeedPx = quietModules * dotsLong;
            int feedPx = longSide * dotsLong + 2 * quietFeedPx;
            int headOffset = (TwoDHeadBudgetPixels - shortSide * dotsShort) / 2;

            using (var bitmap = new SKBitmap(feedPx, TwoDHeadBudgetPixels))
            {
                using (var canvas = new SKCanvas(bitmap))
                using (var darkPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
                {
                    canvas.Clear(SKColors.White);
                    for (int longIndex = 0; longIndex < longSide; longIndex++)
                    {
                        for (int shortIndex = 0; shortIndex < shortSide; shortIndex++)
                        {
                            bool dark = matrixIsPortrait ? modules[shortIndex, longIndex] : modules[longIndex, shortIndex];
                            if (!dark)
                            {
                                continue;
                            }

                            float x = quietFeedPx + longIndex * dotsLong;
                            float y = headOffset + shortIndex * dotsShort;
                            canvas.DrawRect(new SKRect(x, y, x + dotsLong, y + dotsShort), darkPaint);
                        }
                    }
                }

                using (var image = SKImage.FromBitmap(bitmap))
                using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                {
                    return encoded.ToArray();
                }
            }
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="ILetraHelper.CreateJob(string, LetraHelper.TwoDSymbology)"/>
        /// would print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image.</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> can't be encoded as <paramref name="symbology"/> at a size that fits the printer.</exception>
        public byte[] PreviewImage(string data, LetraHelper.TwoDSymbology symbology)
        {
            byte[] imageBytes = RenderTwoDContentImage(data, symbology);
            return PreviewImage(imageBytes, noCut: false, preRendered: true);
        }

        // ---------------------------------------------------------------------------------
        // Composed (multi-element) jobs - concatenating several already-configured pieces
        // (Text/Barcode/2D/Draw/Image) into one continuous label along the feed axis, the same
        // way RenderDinRailRowImage already concatenates several text segments.
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Renders just the printable content for an Image-tab-style element - see
        /// <see cref="RenderTextContentImage"/>. Unlike the other Content methods, this one goes
        /// through <see cref="PrepareBitmap"/> (the same resize/threshold/pad an arbitrary source
        /// photo already gets for a standalone Image job), whose own output is in the printer's
        /// portrait packing order (head axis on <c>Width</c>) rather than the landscape shape
        /// every Content method returns - the same portrait-to-landscape rotation
        /// <see cref="PreviewImage(byte[], bool, bool)"/> already does is applied here to match.
        /// With <paramref name="noCut"/> <c>false</c> (what the Avalonia app's Compose
        /// tab passes), this naturally comes out the full 32-dot head axis, already padded - see
        /// <see cref="ComposeElementImages"/> for how that's reconciled with every other Content
        /// method's bare 30-tall output when composing a strip.
        /// </summary>
        public byte[] RenderImageContentImage(byte[] imageBytes, bool preRendered, bool noCut)
        {
            using (var portraitBitmap = PrepareBitmap(imageBytes, noCut, preRendered))
            using (var landscapeBitmap = RotateBitmap(portraitBitmap, 90))
            using (var image = SKImage.FromBitmap(landscapeBitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                return encoded.ToArray();
            }
        }

        /// <summary>
        /// Pads a Content image up to the printer's full 32-dot head axis, 1 blank row on each
        /// side, if it isn't there already. Content methods other than
        /// <see cref="RenderImageContentImage"/> (Text, Barcode, 2D, and a Draw element's own
        /// stored PNG) render bare, unpadded 30-tall content by design; an Image element instead
        /// comes out of <see cref="PrepareBitmap"/> already at the full 32-dot head axis,
        /// self-padded (see <see cref="RenderImageContentImage"/>'s remarks). Padding the other
        /// 30-tall ones up here - rather than cropping the Image element's own padding back off -
        /// means every element in a composed strip ends up self-contained at the printer's actual
        /// wire height, so <see cref="ILetraHelper.CreateComposedJob"/> can hand the whole thing to
        /// <see cref="ILetraHelper.CreateJob(byte[], bool, bool)"/> as <c>noCut: true</c> (the
        /// caller has already handled the unprintable-edge padding, exactly what that flag means)
        /// instead of asking for a second, redundant round of padding on top.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="bitmap"/> is neither 30 nor 32 tall - not a shape any Content method should ever actually produce.</exception>
        private SKBitmap PadToHeadAxis(SKBitmap bitmap)
        {
            const int contentHeight = 30;
            const int headAxisHeight = 32;

            if (bitmap.Height == headAxisHeight)
            {
                return bitmap;
            }

            if (bitmap.Height != contentHeight)
            {
                throw new ArgumentException($"A composed element must be {contentHeight} or {headAxisHeight} dots tall - got one that's {bitmap.Height} tall.", nameof(bitmap));
            }

            var padded = new SKBitmap(bitmap.Width, headAxisHeight);
            using (var canvas = new SKCanvas(padded))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(bitmap, 0, 1, SKSamplingOptions.Default);
            }

            bitmap.Dispose();
            return padded;
        }

        /// <summary>
        /// Concatenates <paramref name="elementPngs"/> (each already a landscape PNG - see
        /// <see cref="RenderTextContentImage"/> and friends, or a Draw tab element's own stored
        /// PNG, which is already exactly this shape) left to right into one continuous strip, the
        /// same technique <see cref="RenderDinRailRowImage"/> already uses for text segments,
        /// generalized to any element type. Every element is padded up to the full 32-dot head
        /// axis first (see <see cref="PadToHeadAxis"/>) so they all line up at the same height
        /// regardless of which Content method produced them. Public (rather than kept private) so
        /// <see cref="ILetraHelper.CreateComposedJob"/> can build a job from it.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="elementPngs"/> is empty, isn't decodable, or isn't a shape <see cref="PadToHeadAxis"/> accepts.</exception>
        public SKBitmap ComposeElementImages(IReadOnlyList<byte[]> elementPngs)
        {
            if (elementPngs.Count == 0)
            {
                throw new ArgumentException("A composed label needs at least one element.", nameof(elementPngs));
            }

            const int targetHeight = 32;
            var bitmaps = new List<SKBitmap>(elementPngs.Count);
            try
            {
                int totalWidth = 0;
                foreach (byte[] png in elementPngs)
                {
                    var decoded = SKBitmap.Decode(png)
                        ?? throw new ArgumentException("One of the composed elements isn't a valid/decodable PNG.", nameof(elementPngs));
                    var bitmap = PadToHeadAxis(decoded);
                    bitmaps.Add(bitmap);
                    totalWidth += bitmap.Width;
                }

                var finalBitmap = new SKBitmap(Math.Max(totalWidth, 1), targetHeight);
                using (var canvas = new SKCanvas(finalBitmap))
                {
                    canvas.Clear(SKColors.White);
                    int x = 0;
                    foreach (var bitmap in bitmaps)
                    {
                        canvas.DrawBitmap(bitmap, x, 0, SKSamplingOptions.Default);
                        x += bitmap.Width;
                    }
                }

                return finalBitmap;
            }
            finally
            {
                foreach (var bitmap in bitmaps)
                {
                    bitmap.Dispose();
                }
            }
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="ILetraHelper.CreateComposedJob"/> would print
        /// for the same elements. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image.</returns>
        public byte[] PreviewComposedImage(IReadOnlyList<byte[]> elementPngs)
        {
            using (var finalBitmap = ComposeElementImages(elementPngs))
            using (var image = SKImage.FromBitmap(finalBitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                return PreviewImage(encoded.ToArray(), noCut: true, preRendered: true);
            }
        }
    }
}
