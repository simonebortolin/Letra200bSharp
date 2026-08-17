using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using ZXing;
using ZXing.Common;

namespace Letra200bSharp
{
    public class LetraHelper
    {
        /// <summary>
        /// Byte array represeinting the form feed command
        /// </summary>
        private static readonly byte[] FormFeed = new byte[2] { 0x1B, 0x45 };

        /// <summary>
        /// Byte array representing the status command
        /// </summary>
        private static readonly byte[] Status = new byte[2] { 0x1B, 0x41 };

        /// <summary>
        /// Byte array indicating the end of data
        /// </summary>
        private static readonly byte[] End = new byte[2] { 0x1B, 0x51 };

        /// <summary>
        /// Byte array representing the start of the job
        /// </summary>
        private static readonly byte[] StartJob = new byte[6] { 0x1B, 0x73, 0x9A, 0x02, 0x00, 0x00 };

        private static int CalculateChecksum(byte[] data)
        {
            var checksum = 0;
            foreach (byte b in data)
            {
                checksum += b;
            }
            return checksum & 0xFF;
        }

        /// <summary>
        /// Split data in chunks
        /// </summary>
        /// <param name="data"></param>
        /// <param name="chunkSize"></param>
        /// <returns></returns>
        private static List<byte[]> SplitChunks(byte[] data, int chunkSize = 300)
        {
            var chunks = new List<byte[]>();

            // The 1-byte sequence number prefixing each chunk must never be 0x1B (27):
            // that's the same byte the printer's firmware uses to recognize the start of
            // an ESC command, so a chunk sequenced 27 gets misread as a command instead of
            // data, stalling the job. Skip 27 (and, once the counter wraps past 255, 283,
            // etc.) the same way the reference protocol does.
            byte sequence = 0;

            for (int i = 0; i < data.Length; i += chunkSize)
            {
                int end = Math.Min(i + chunkSize, data.Length);
                byte[] chunk;

                // Determine if we need to append extra bytes
                if (end == data.Length) // Last chunk
                {
                    chunk = new byte[end - i + 3]; // 2 extra bytes
                }
                else
                {
                    chunk = new byte[end - i + 1]; // No extra bytes
                }

                if (sequence == 0x1B) // 27
                {
                    sequence++;
                }
                chunk[0] = sequence;
                sequence++;

                // Copy the relevant bytes to the chunk
                for (int j = 0; j < end - i; j++)
                {
                    chunk[j + 1] = data[i + j];
                }

                // Append extra bytes to the last chunk
                if (end == data.Length)
                {
                    chunk[chunk.Length - 2] = 0x12;
                    chunk[chunk.Length - 1] = 0x34;
                }

                chunks.Add(chunk);
            }

            return chunks;
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
        private static SKBitmap PrepareBitmap(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            // Load image from byte array
            using (var stream = new System.IO.MemoryStream(imageBytes))
            {
                using (var skiaImage = SKBitmap.Decode(stream))
                {
                    // Convert to 1-bit monochrome
                    var monoBitmap = new SKBitmap(skiaImage.Width, skiaImage.Height);
                    for (int x = 0; x < skiaImage.Width; x++)
                    {
                        for (int y = 0; y < skiaImage.Height; y++)
                        {
                            var color = skiaImage.GetPixel(x, y);
                            // Composite over a white background according to alpha (a
                            // transparent pixel is blank label, not whatever its RGB
                            // happens to be), then set pixel black or white by brightness
                            float alpha = color.Alpha / 255f;
                            float effectiveRed = color.Red * alpha + 255 * (1 - alpha);
                            monoBitmap.SetPixel(x, y, effectiveRed < 128 ? SKColors.Black : SKColors.White);
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
        /// into an array of 1s and 0s.
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="noCut">See <see cref="PrepareBitmap"/>.</param>
        /// <param name="preRendered">See <see cref="PrepareBitmap"/>.</param>
        /// <returns></returns>
        private static ImageInfo PrepareImage(byte[] imageBytes, bool noCut = false, bool preRendered = false)
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
        /// <see cref="CreateJob(byte[], bool, bool)"/>: a black pixel is exactly a dot that
        /// is sent to the printer as "print". Regardless of <paramref name="noCut"/>, the
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
        public static byte[] PreviewImage(byte[] imageBytes, bool noCut = false, bool preRendered = false)
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
        private static SKBitmap RotateBitmap(SKBitmap bitmap, float degrees)
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

        private static byte[] GetHeaderBytes(int length)
        {
            var lengthBytes = BitConverter.GetBytes(length);
            var header = new byte[5 + lengthBytes.Length];
            header[0] = 0xFF; // preamble
            header[1] = 0xF0; // flags
            header[2] = 0x12; // magic
            header[3] = 0x34; // magic
            Array.Copy(lengthBytes, 0, header, 4, lengthBytes.Length);
            header[header.Length - 1] = (byte)CalculateChecksum(header.Take(header.Length - 1).ToArray());
            return header;
        }

        private static byte[] GetPrintData(byte[] data, int width, int height)
        {
            if (width * height != data.Length * 8)
            {
                throw new ArgumentException($"Data does not match dimensions ({width}*{height}!={data.Length * 8})");
            }

            var printData = new List<byte> { 0x1B, 0x44, 0x01, 0x02 };
            printData.AddRange(BitConverter.GetBytes(width));
            printData.AddRange(BitConverter.GetBytes(height));
            printData.AddRange(data);
            return printData.ToArray();
        }

        /// <summary>
        /// A job always consists of the following parts:
        /// - header bytes
        /// - chunked body consisting of:
        ///   - start job
        ///   - print data
        ///   - form feed
        ///   - status
        ///   - end
        /// </summary>
        /// <param name="imageBytes"></param>
        /// <param name="noCut">
        /// Set to <c>true</c> if <paramref name="imageBytes"/> already has a blank first and
        /// last row to account for the printer's unprintable edges (see <see cref="PrepareBitmap"/>).
        /// </param>
        /// <param name="preRendered">See <see cref="PrepareBitmap"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public static List<byte[]> CreateJob(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            var imageInfo = PrepareImage(imageBytes, noCut, preRendered);
            byte[] packedData = new byte[(int)MathF.Ceiling(imageInfo.Data.Length / 8f)];
            for (int i = 0; i < imageInfo.Data.Length; i++)
            {
                if (imageInfo.Data[i] == 1)
                    packedData[i / 8] |= (byte)(1 << (i % 8));
            }

            var body = new List<byte>();
            body.AddRange(StartJob);
            body.AddRange(GetPrintData(packedData, imageInfo.Width, imageInfo.Height));
            body.AddRange(FormFeed);
            body.AddRange(Status);
            body.AddRange(End);

            byte[] header = GetHeaderBytes(body.Count);
            var chunks = SplitChunks(body.ToArray());

            var result = new List<byte[]> { header };
            result.AddRange(chunks);
            return result;
        }

        /// <summary>
        /// Renders <paramref name="text"/> to a black-on-white label image (à la
        /// https://github.com/ysfchn/dymo-bluetooth's <c>convert -background white -fill
        /// black -font ... label:"text" -resize x30 ...</c> recipe) and prints it, reusing
        /// the same <see cref="CreateJob(byte[], bool, bool)"/> pipeline via <c>preRendered</c>.
        /// </summary>
        /// <param name="text">The text to print</param>
        /// <param name="fontFamily">Name of the font family to use, e.g. "Arial"</param>
        /// <param name="size">
        /// Since the printed text is always scaled to the fixed printable height (30, or 32
        /// pixels with <paramref name="noCut"/>), an absolute point size wouldn't mean
        /// anything - what actually changes is how much of that fixed height the glyphs fill
        /// versus surrounding blank margin. This picks a preset for that, from the most
        /// margin/smallest-looking text (<see cref="LabelTextSize.XS"/>) to the glyphs
        /// filling almost the entire height (<see cref="LabelTextSize.XL"/>).
        /// </param>
        /// <param name="style">
        /// Font weight/effect, matching the options offered by the real Dymo app. <see cref="TextStyle.Vertical"/>
        /// prints each character on its own line, so the text reads top-to-bottom instead of
        /// left-to-right (e.g. "HI" becomes "H" then "I" stacked); existing line breaks in
        /// <paramref name="text"/> (e.g. from a second line of input) are preserved as their
        /// own (blank) line.
        /// </param>
        /// <param name="upperCase">Whether to print <paramref name="text"/> in all caps</param>
        /// <param name="widthScale">
        /// Horizontal stretch factor for the glyphs (1 = normal, 2 = twice as wide, 0.5 =
        /// half as wide), independent of <paramref name="size"/> which only affects how much
        /// of the fixed height is filled. Unlike height, width isn't constrained by the
        /// printer, so this directly controls how wide the printed text ends up.
        /// </param>
        /// <param name="boxStyle">Decorative border/underline framing the text, matching a subset of the real Dymo app's options.</param>
        /// <param name="align">Horizontal alignment of shorter lines relative to the widest one (only visible when lines differ in length).</param>
        /// <param name="noCut">See <see cref="PrepareBitmap"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public static List<byte[]> CreateJob(string text, string fontFamily = "Arial", LabelTextSize size = LabelTextSize.M, TextStyle style = TextStyle.Normal, bool upperCase = false, float widthScale = 1f, TextBoxStyle boxStyle = TextBoxStyle.None, TextAlign align = TextAlign.Left, bool noCut = false)
        {
            byte[] imageBytes = RenderTextImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="CreateJob(string, string, LabelTextSize, TextStyle, bool, float, TextBoxStyle, TextAlign, bool)"/>
        /// would print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image</returns>
        public static byte[] PreviewImage(string text, string fontFamily = "Arial", LabelTextSize size = LabelTextSize.M, TextStyle style = TextStyle.Normal, bool upperCase = false, float widthScale = 1f, TextBoxStyle boxStyle = TextBoxStyle.None, TextAlign align = TextAlign.Left, bool noCut = false)
        {
            byte[] imageBytes = RenderTextImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, noCut);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }

        /// <summary>Horizontal alignment of shorter lines relative to the widest line in a multi-line label.</summary>
        public enum TextAlign
        {
            Left,
            Center,
            Right
        }

        /// <summary>How far a narrower element sits from a wider one's left edge, as a fraction of the leftover space - 0 for Left, 0.5 for Center, 1 for Right.</summary>
        private static float AlignFactor(TextAlign align) => align switch
        {
            TextAlign.Center => 0.5f,
            TextAlign.Right => 1f,
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
        public static float DinRailLengthMm(decimal modules) => (float)modules * DinRailModuleWidthMm;

        /// <summary>Feed-axis pixel width corresponding to <see cref="DinRailLengthMm"/>.</summary>
        public static int DinRailWidthPixels(decimal modules) => Math.Max(1, (int)MathF.Round(DinRailLengthMm(modules) * FeedAxisPixelsPerMm));

        /// <summary>
        /// How each DIN rail segment's text is sized - see <see cref="RenderDinRailRowImage"/>.
        /// Neither mode ever stretches or squeezes glyphs non-uniformly (no distortion) - both
        /// only ever apply a single uniform scale factor (equally in X and Y), same as scaling a
        /// photo without changing its aspect ratio.
        /// </summary>
        public enum DinRailSizing
        {
            /// <summary>Every segment in the row uses the same font scale - whichever is the smallest scale any single segment actually needs - so the whole strip reads at one consistent size.</summary>
            Uniform,
            /// <summary>Each segment independently uses the largest scale that fits its own text.</summary>
            MaxPerLabel
        }

        private static int DecodeWidth(byte[] image)
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
        /// Renders at <see cref="LabelTextSize.XL"/> (edge-to-edge, no blank margin around the
        /// glyphs) rather than the Text tab's usual size presets - the padding ratio doesn't
        /// change how wide the natural rendering ends up (it scales height and width the same
        /// way, so it cancels out once everything is fit to the target height), but it does
        /// change how much of that width is actual glyph ink versus blank margin; DIN segments
        /// are already tightly space-constrained, so giving the glyphs the full share keeps them
        /// bolder/more legible once a segment needs to be scaled down (and separators, when on,
        /// already carve out their own small gap - see <see cref="DinRailSeparatorWidthPx"/>). A
        /// blank/whitespace-only row (used as a spacer) renders as a minimal blank image.
        /// </summary>
        private static byte[] RenderDinRailSegmentNatural(string text, string fontFamily, TextStyle style, bool upperCase, TextAlign align, int wrapTargetWidthPx, bool noCut)
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

            byte[] oneLineImage = RenderTextImage(text, fontFamily, LabelTextSize.XL, style, upperCase, 1f, TextBoxStyle.None, align, noCut);
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
                byte[] candidateImage = RenderTextImage(candidate, fontFamily, LabelTextSize.XL, style, upperCase, 1f, TextBoxStyle.None, align, noCut);
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
        public static float DinRailRequiredScale(string text, string fontFamily, TextStyle style, bool upperCase, TextAlign align, decimal modules, bool noCut = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 1f;
            }

            int targetWidthPx = DinRailWidthPixels(modules);
            byte[] naturalImage = RenderDinRailSegmentNatural(text, fontFamily, style, upperCase, align, targetWidthPx, noCut);
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
        /// whatever factor <paramref name="sizing"/> calls for (see <see cref="DinRailSizing"/>),
        /// then centered vertically and aligned horizontally (<paramref name="align"/>) within its
        /// segment. When <paramref name="showSeparators"/> is set, a thin vertical line is printed
        /// at the boundary between each pair of adjacent segments, carved out of the earlier
        /// segment's own available width rather than added on top of it, so segment widths always
        /// still sum to the requested total physical length.
        /// </summary>
        private static byte[] RenderDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, TextStyle style, bool upperCase, TextAlign align, DinRailSizing sizing, bool showSeparators, bool noCut)
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

                naturalImages[i] = RenderDinRailSegmentNatural(rows[i].Text, fontFamily, style, upperCase, align, textWidthPx, noCut);
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
                    float scale = sizing == DinRailSizing.Uniform ? uniformScale : requiredScales[i];
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
        /// Renders <paramref name="rows"/> as one continuous DIN rail strip (see <see cref="RenderDinRailRowImage"/>)
        /// and prints it, reusing the same <see cref="CreateJob(byte[], bool, bool)"/> pipeline via <c>preRendered</c>.
        /// </summary>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public static List<byte[]> CreateDinRailRowJob(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, TextStyle style, bool upperCase, TextAlign align, DinRailSizing sizing, bool showSeparators, bool noCut = false)
        {
            byte[] imageBytes = RenderDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="CreateDinRailRowJob"/> would print for the
        /// same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the rendered label</returns>
        public static byte[] PreviewDinRailRowImage(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, TextStyle style, bool upperCase, TextAlign align, DinRailSizing sizing, bool showSeparators, bool noCut = false)
        {
            byte[] imageBytes = RenderDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, noCut);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Font weight/effect options offered by the real Dymo LetraTag app.
        /// </summary>
        public enum TextStyle
        {
            Normal,
            Bold,
            Italic,
            Outline,
            Shadow,
            Vertical
        }

        /// <summary>
        /// Decorative border/underline framing the text, matching the geometric subset of the
        /// real Dymo app's "box and underline styles" (the illustrated ones - Train, Sweet
        /// Hearts, Flowers - aren't included).
        /// </summary>
        public enum TextBoxStyle
        {
            None,
            Underline,
            Square,
            Pointed,
            Rounded,
            Edged,
            Crocodile
        }

        /// <summary>
        /// How much of the fixed printable height the rendered text glyphs fill (and, since
        /// this release, how much horizontal margin surrounds them too), from mostly blank
        /// margin (<see cref="XS"/>) to nearly edge-to-edge (<see cref="XL"/>).
        /// </summary>
        public enum LabelTextSize
        {
            XS,
            S,
            M,
            L,
            XL
        }

        private static float GetPaddingRatio(LabelTextSize size)
        {
            switch (size)
            {
                case LabelTextSize.XS: return 0.6f;
                case LabelTextSize.S: return 0.35f;
                case LabelTextSize.M: return 0.15f;
                case LabelTextSize.L: return 0.05f;
                case LabelTextSize.XL: return 0f;
                default: return 0.15f;
            }
        }

        /// <summary>
        /// Target per-line pixel height (out of the default 30px printable height) for the
        /// two-line case: how much of the fixed height each of the two lines gets once the
        /// gap between them (see <see cref="RenderTextImage"/>) is subtracted out.
        /// </summary>
        private static int GetTwoLineHeight(LabelTextSize size)
        {
            switch (size)
            {
                case LabelTextSize.XS: return 12;
                case LabelTextSize.S: return 13;
                case LabelTextSize.M: return 14;
                case LabelTextSize.L: return 15;
                case LabelTextSize.XL: return 15;
                default: return 14;
            }
        }

        /// <summary>
        /// Renders <paramref name="text"/> as black text on a white background (one or more
        /// lines, stacked), then scales it so its height matches the target printable pixel
        /// height (30, or 32 if <paramref name="noCut"/> is <c>true</c>), keeping the aspect
        /// ratio - equivalent to ImageMagick's <c>-resize x30</c>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the rendered label</returns>
        private static byte[] RenderTextImage(string text, string fontFamily, LabelTextSize size, TextStyle style, bool upperCase, float widthScale, TextBoxStyle boxStyle, TextAlign align, bool noCut)
        {
            if (upperCase)
            {
                text = text.ToUpperInvariant();
            }

            bool rotate90 = style == TextStyle.Vertical;
            if (rotate90)
            {
                text = string.Join(Environment.NewLine, text.ToCharArray());
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');

            const float renderFontSize = 96;
            int targetHeight = noCut ? 32 : 30;
            var fontStyle = style switch
            {
                TextStyle.Bold => SKFontStyle.Bold,
                TextStyle.Italic => SKFontStyle.Italic,
                _ => SKFontStyle.Normal
            };
            float paddingRatio = GetPaddingRatio(size);

            using (var typeface = SKTypeface.FromFamilyName(fontFamily, fontStyle))
            using (var font = new SKFont(typeface, renderFontSize) { ScaleX = widthScale })
            // No antialiasing: this gets downscaled to a handful of pixels tall (a couple of
            // lines can be well under 10px each), and antialiased gray edges plus a smooth
            // resize filter would get hard-thresholded to black/white afterwards anyway
            // (see PrepareBitmap) - producing noisy, near-random pixels instead of clean
            // glyph shapes. Rendering with hard edges from the start keeps it legible.
            using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
            {
                if (style == TextStyle.Outline)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = renderFontSize * 0.06f;
                }

                float shadowOffset = style == TextStyle.Shadow ? renderFontSize * 0.08f : 0f;

                font.GetFontMetrics(out SKFontMetrics metrics);
                float ascent = -metrics.Ascent;
                float lineHeight = ascent + metrics.Descent;
                float[] lineWidths = lines.Select(line => font.MeasureText(line, paint)).ToArray();
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
                    if (size == LabelTextSize.XL)
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
                float unpaddedContentTop = float.MaxValue;
                float unpaddedContentBottom = float.MinValue;
                for (int i = 0; i < lines.Length; i++)
                {
                    float lineBaseline = ascent + i * (lineHeight + lineGap);
                    font.MeasureText(lines[i], out SKRect inkBounds, paint);
                    unpaddedContentTop = Math.Min(unpaddedContentTop, lineBaseline + inkBounds.Top);
                    unpaddedContentBottom = Math.Max(unpaddedContentBottom, lineBaseline + inkBounds.Bottom);
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
                                canvas.DrawText(lines[i], x + shadowOffset, y + shadowOffset, SKTextAlign.Left, font, paint);
                            }
                            canvas.DrawText(lines[i], x, y, SKTextAlign.Left, font, paint);
                        }

                        if (boxStyle != TextBoxStyle.None)
                        {
                            // Aim for a border that's still ~1.5px wide once this raw canvas
                            // gets scaled down to targetHeight.
                            float borderStrokeWidth = Math.Max(bitmap.Height / (float)targetHeight * 1.5f, 1f);
                            // Keep the border off the printer's unprintable top/bottom row
                            // (see PrepareBitmap): without this, the border sits exactly at
                            // the edge of the 30 printable rows instead of visibly inside
                            // them. ~1.5px of margin once scaled down to targetHeight.
                            float verticalMargin = bitmap.Height / (float)targetHeight * 1.5f;
                            DrawTextBox(canvas, boxStyle, bitmap.Width, bitmap.Height, borderStrokeWidth, verticalMargin);
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
        private static void DrawTextBox(SKCanvas canvas, TextBoxStyle boxStyle, float width, float height, float strokeWidth, float verticalMargin)
        {
            using (var borderPaint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth })
            {
                float inset = strokeWidth / 2f;
                var rect = new SKRect(inset, inset + verticalMargin, width - inset, height - inset - verticalMargin);

                switch (boxStyle)
                {
                    case TextBoxStyle.Underline:
                        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Bottom, borderPaint);
                        break;
                    case TextBoxStyle.Square:
                        canvas.DrawRect(rect, borderPaint);
                        break;
                    case TextBoxStyle.Rounded:
                        float radius = rect.Height * 0.35f;
                        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
                        break;
                    case TextBoxStyle.Pointed:
                        DrawPointedBox(canvas, borderPaint, rect);
                        break;
                    case TextBoxStyle.Edged:
                        DrawZigzagBox(canvas, borderPaint, rect, toothSpan: rect.Height * 0.7f, amplitude: rect.Height * 0.12f);
                        break;
                    case TextBoxStyle.Crocodile:
                        DrawZigzagBox(canvas, borderPaint, rect, toothSpan: rect.Height, amplitude: rect.Height * 0.3f);
                        break;
                }
            }
        }

        /// <summary>A box with the left and right edges drawn to a point instead of a flat side, like a tag/pennant.</summary>
        private static void DrawPointedBox(SKCanvas canvas, SKPaint paint, SKRect rect)
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
        private static void DrawZigzagBox(SKCanvas canvas, SKPaint paint, SKRect rect, float toothSpan, float amplitude)
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
        /// 1D barcode symbologies exposed for printing/preview - a curated subset of
        /// ZXing.Net's <see cref="BarcodeFormat"/>. No 2D symbologies (QR, Data Matrix, ...):
        /// squeezed into the printer's fixed ~30px printable height they'd come out as a tiny,
        /// unreadable smudge instead of a scannable code.
        /// </summary>
        public enum BarcodeSymbology
        {
            Code128,
            Code39,
            Codabar,
            Itf,
            Ean13,
            Ean8,
            UpcA,
            UpcE
        }

        private static BarcodeFormat ToZXingFormat(BarcodeSymbology symbology) => symbology switch
        {
            BarcodeSymbology.Code128 => BarcodeFormat.CODE_128,
            BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
            BarcodeSymbology.Codabar => BarcodeFormat.CODABAR,
            BarcodeSymbology.Itf => BarcodeFormat.ITF,
            BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
            BarcodeSymbology.Ean8 => BarcodeFormat.EAN_8,
            BarcodeSymbology.UpcA => BarcodeFormat.UPC_A,
            BarcodeSymbology.UpcE => BarcodeFormat.UPC_E,
            _ => throw new ArgumentOutOfRangeException(nameof(symbology))
        };

        /// <summary>
        /// How many printer dots wide the narrowest barcode module (bar/space unit) gets
        /// scaled to. Encoding at the natural 1 dot per module (see <see cref="RenderBarcodeImage"/>)
        /// would make the narrowest bars a single pixel wide - technically correct but too thin
        /// to print/scan reliably.
        /// </summary>
        private const int BarcodeModuleScale = 2;

        /// <summary>
        /// Font the optional barcode-number caption (see <see cref="RenderBarcodeImage"/>) is
        /// always printed in - deliberately fixed rather than exposed as a picker like the Text
        /// tab's font combo, since it's meant to stay a small, secondary caption rather than
        /// become the label's main content.
        /// </summary>
        private const string BarcodeNumberFontFamily = "Arial";

        /// <summary>
        /// Fraction of the printable height given to the optional barcode-number caption (see
        /// <see cref="RenderBarcodeImage"/>) - kept small ("the smallest size possible") since
        /// the bars are the actual scannable payload and the number is just a human-readable
        /// aid; the rest of the height goes to the bars themselves.
        /// </summary>
        private const float BarcodeNumberHeightRatio = 0.2f;

        /// <summary>
        /// Blank row(s) between the bars and the number caption (see <see cref="RenderBarcodeImage"/>) -
        /// scoped to just that boundary, not extra margin around the whole barcode.
        /// </summary>
        private const int BarcodeNumberGapPx = 1;

        /// <summary>
        /// Renders <paramref name="data"/> at the smallest legible height, bold, in
        /// <see cref="BarcodeNumberFontFamily"/> - the caption printed under the bars when
        /// <see cref="RenderBarcodeImage"/>'s <c>showNumber</c> is set.
        /// </summary>
        /// <param name="numberHeight">Final pixel height of the caption (see <see cref="BarcodeNumberHeightRatio"/>).</param>
        private static SKBitmap RenderBarcodeNumberBitmap(string data, int numberHeight)
        {
            // Rendered through the normal text pipeline at full quality/height first (XL - no
            // padding, glyphs get the full share of the height - and Bold), then scaled down as
            // a whole to the small height actually reserved for it, same trick
            // RenderDinRailSegmentNatural uses: keeps the digits' proportions correct instead of
            // asking SkiaSharp's font layout to work at a height too small to be meaningful.
            byte[] fullHeightPng = RenderTextImage(data, BarcodeNumberFontFamily, LabelTextSize.XL, TextStyle.Bold, upperCase: false, widthScale: 1f, TextBoxStyle.None, TextAlign.Center, noCut: false);
            using (var fullHeightBitmap = SKBitmap.Decode(fullHeightPng))
            {
                int width = Math.Max(1, (int)MathF.Round(fullHeightBitmap.Width * (numberHeight / (float)fullHeightBitmap.Height)));
                return fullHeightBitmap.Resize(new SKImageInfo(width, numberHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
        }

        /// <summary>
        /// Renders <paramref name="data"/> as a black-on-white barcode using ZXing.Net, sized so
        /// its module rows exactly match the printer's fixed printable height (30, or 32 if
        /// <paramref name="noCut"/>). Reuses <see cref="CreateJob(byte[], bool, bool)"/>'s
        /// <c>preRendered</c> path the same way <see cref="RenderTextImage"/> does.
        /// </summary>
        /// <param name="showNumber">
        /// Whether to print <paramref name="data"/> itself as a small caption below the bars
        /// (see <see cref="RenderBarcodeNumberBitmap"/>) - unlike the real Dymo app, this is off
        /// by default (bars only).
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/> (e.g. non-numeric EAN/UPC data, or the wrong digit count).</exception>
        /// <returns>PNG-encoded bytes of the rendered barcode</returns>
        private static byte[] RenderBarcodeImage(string data, BarcodeSymbology symbology, bool showNumber, bool noCut)
        {
            int targetHeight = noCut ? 32 : 30;
            // How tall a notch to leave for the number, once its column range is known below -
            // not a height the bars themselves are ever encoded/resized to (see below: bars
            // always render at the full targetHeight; only the width directly under the number
            // gets cut down to make room for it, everywhere else stays full height).
            int numberHeight = showNumber ? Math.Max(4, (int)MathF.Round(targetHeight * BarcodeNumberHeightRatio)) : 0;
            int notchStartY = targetHeight - numberHeight;

            var hints = new Dictionary<EncodeHintType, object> { { EncodeHintType.PURE_BARCODE, true } };

            BitMatrix matrix;
            try
            {
                // width=1 forces ZXing's internal multiple (pixels-per-module) to its minimum
                // of 1, i.e. the natural, unstretched module width - scaled back up ourselves
                // below via a crisp nearest-neighbor resize instead of letting ZXing do it, so
                // that step matches the rest of this file's approach to keeping bars/edges sharp.
                // Always the full targetHeight - see notchStartY above.
                matrix = new MultiFormatWriter().encode(data, ToZXingFormat(symbology), 1, targetHeight, hints);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"'{data}' isn't valid {symbology} barcode data: {ex.Message}", nameof(data), ex);
            }

            using (var barsBitmap = new SKBitmap(matrix.Width, matrix.Height))
            {
                for (int x = 0; x < matrix.Width; x++)
                {
                    for (int y = 0; y < matrix.Height; y++)
                    {
                        barsBitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
                    }
                }

                int barsWidth = matrix.Width * BarcodeModuleScale;
                using (var scaledBars = barsBitmap.Resize(new SKImageInfo(barsWidth, targetHeight), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
                {
                    if (!showNumber)
                    {
                        using (var image = SKImage.FromBitmap(scaledBars))
                        using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
                        {
                            return encoded.ToArray();
                        }
                    }

                    // A blank row between the bars and the number, carved out of the number's
                    // own reserved height rather than added on top of it - only the number's own
                    // area gets tighter to make room for the gap above it.
                    int gapHeight = Math.Min(BarcodeNumberGapPx, numberHeight - 1);
                    int textHeight = numberHeight - gapHeight;

                    using (var numberBitmap = RenderBarcodeNumberBitmap(data, textHeight))
                    {
                        int finalWidth = Math.Max(barsWidth, numberBitmap.Width);
                        float numberX = (finalWidth - numberBitmap.Width) / 2f;

                        using (var finalBitmap = new SKBitmap(finalWidth, targetHeight))
                        using (var canvas = new SKCanvas(finalBitmap))
                        {
                            canvas.Clear(SKColors.White);
                            canvas.DrawBitmap(scaledBars, (finalWidth - barsWidth) / 2f, 0, SKSamplingOptions.Default);

                            // Cut a notch only under the number's own width, so the bars stay
                            // full height everywhere else - blank out whatever bars would
                            // otherwise show through there before drawing the number over the
                            // clear space.
                            using (var notchPaint = new SKPaint { Color = SKColors.White, IsAntialias = false })
                            {
                                canvas.DrawRect(new SKRect(numberX, notchStartY, numberX + numberBitmap.Width, targetHeight), notchPaint);
                            }
                            canvas.DrawBitmap(numberBitmap, numberX, notchStartY + gapHeight, SKSamplingOptions.Default);

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

        /// <param name="data">The barcode's content (digits only for <see cref="BarcodeSymbology.Ean13"/>/<see cref="BarcodeSymbology.Ean8"/>/<see cref="BarcodeSymbology.UpcA"/>/<see cref="BarcodeSymbology.UpcE"/>, with the exact digit count each of those symbologies requires).</param>
        /// <param name="symbology">Which barcode symbology to encode <paramref name="data"/> as.</param>
        /// <param name="noCut">See <see cref="PrepareBitmap"/>.</param>
        /// <param name="showNumber">See <see cref="RenderBarcodeImage"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/>.</exception>
        public static List<byte[]> CreateJob(string data, BarcodeSymbology symbology, bool noCut = false, bool showNumber = false)
        {
            byte[] imageBytes = RenderBarcodeImage(data, symbology, showNumber, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Renders a PNG preview of what <see cref="CreateJob(string, BarcodeSymbology, bool, bool)"/>
        /// would print for the same arguments. See <see cref="PreviewImage(byte[], bool, bool)"/>.
        /// </summary>
        /// <returns>PNG-encoded bytes of the preview image</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/>.</exception>
        public static byte[] PreviewImage(string data, BarcodeSymbology symbology, bool noCut = false, bool showNumber = false)
        {
            byte[] imageBytes = RenderBarcodeImage(data, symbology, showNumber, noCut);
            return PreviewImage(imageBytes, noCut, preRendered: true);
        }
    }
}
