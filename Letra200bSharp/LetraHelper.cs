using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Letra200bSharp
{
    /// <summary>
    /// Builds the Dymo LetraTag 200B's BLE wire format for a print job - see
    /// <see cref="ILetraHelper"/>. Delegates all actual pixel rendering to an injected
    /// <see cref="IRenderHelper"/> (typically a <see cref="RenderHelper"/>) rather than doing it
    /// itself - see <see cref="RenderHelper"/> for that half, and for the type declarations
    /// referenced below (e.g. <see cref="LabelTextSize"/>) even though they're nested here rather
    /// than there, purely so every existing <c>LetraHelper.XXX</c> type reference keeps compiling.
    /// </summary>
    public class LetraHelper : ILetraHelper
    {
        private readonly IRenderHelper _render;

        public LetraHelper(IRenderHelper render)
        {
            _render = render;
        }

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
        /// last row to account for the printer's unprintable edges (see <see cref="RenderHelper.PrepareBitmap"/>).
        /// </param>
        /// <param name="preRendered">See <see cref="RenderHelper.PrepareBitmap"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public List<byte[]> CreateJob(byte[] imageBytes, bool noCut = false, bool preRendered = false)
        {
            var imageInfo = _render.PrepareImage(imageBytes, noCut, preRendered);
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
        /// <param name="noCut">See <see cref="RenderHelper.PrepareBitmap"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public List<byte[]> CreateJob(string text, string fontFamily = "Arial", LabelTextSize size = LabelTextSize.M, TextStyle style = TextStyle.Normal, bool upperCase = false, float widthScale = 1f, TextBoxStyle boxStyle = TextBoxStyle.None, TextAlign align = TextAlign.Left, bool noCut = false)
        {
            byte[] imageBytes = _render.RenderTextContentImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, align, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <param name="data">The barcode's content (digits only for <see cref="BarcodeSymbology.Ean13"/>/<see cref="BarcodeSymbology.Ean8"/>/<see cref="BarcodeSymbology.UpcA"/>/<see cref="BarcodeSymbology.UpcE"/>, with the exact digit count each of those symbologies requires).</param>
        /// <param name="symbology">Which barcode symbology to encode <paramref name="data"/> as.</param>
        /// <param name="noCut">See <see cref="RenderHelper.PrepareBitmap"/>.</param>
        /// <param name="caption">See <see cref="RenderHelper.RenderBarcodeContentImage"/>.</param>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> isn't valid for <paramref name="symbology"/>.</exception>
        public List<byte[]> CreateJob(string data, BarcodeSymbology symbology, bool noCut = false, CaptionOptions caption = default)
        {
            byte[] imageBytes = _render.RenderBarcodeContentImage(data, symbology, caption, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Renders <paramref name="data"/> as a 2D matrix code (see <see cref="RenderHelper.RenderTwoDContentImage"/>)
        /// and prints it, reusing the same <see cref="CreateJob(byte[], bool, bool)"/> pipeline
        /// via <c>preRendered</c>.
        /// </summary>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b.</returns>
        /// <exception cref="ArgumentException"><paramref name="data"/> can't be encoded as <paramref name="symbology"/> at a size that fits the printer.</exception>
        public List<byte[]> CreateJob(string data, TwoDSymbology symbology)
        {
            byte[] imageBytes = _render.RenderTwoDContentImage(data, symbology);
            return CreateJob(imageBytes, noCut: false, preRendered: true);
        }

        /// <summary>
        /// Renders <paramref name="rows"/> as one continuous DIN rail strip (see
        /// <see cref="RenderHelper.RenderDinRailRowImage"/>) and prints it, reusing the same
        /// <see cref="CreateJob(byte[], bool, bool)"/> pipeline via <c>preRendered</c>.
        /// </summary>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b</returns>
        public List<byte[]> CreateDinRailRowJob(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, TextStyle style, bool upperCase, TextAlign align, DinRailSizing sizing, bool showSeparators, bool noCut = false)
        {
            byte[] imageBytes = _render.RenderDinRailRowImage(rows, fontFamily, style, upperCase, align, sizing, showSeparators, noCut);
            return CreateJob(imageBytes, noCut, preRendered: true);
        }

        /// <summary>
        /// Composes <paramref name="elementPngs"/> (see <see cref="RenderHelper.ComposeElementImages"/>)
        /// into one continuous label and prints it, reusing the same
        /// <see cref="CreateJob(byte[], bool, bool)"/> pipeline via <c>preRendered</c>. Always
        /// <c>noCut: true</c> - see <see cref="RenderHelper"/>'s <c>PadToHeadAxis</c> for why
        /// every element already carries its own head-axis padding by this point.
        /// </summary>
        /// <returns>List of byte arrays containing the data to be sent to the Dymo Letra 200b.</returns>
        public List<byte[]> CreateComposedJob(IReadOnlyList<byte[]> elementPngs)
        {
            using (var finalBitmap = _render.ComposeElementImages(elementPngs))
            using (var image = SKImage.FromBitmap(finalBitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                return CreateJob(encoded.ToArray(), noCut: true, preRendered: true);
            }
        }

        /// <summary>Horizontal alignment of shorter lines relative to the widest line in a multi-line label.</summary>
        public enum TextAlign
        {
            Left,
            Center,
            Right
        }

        /// <summary>Width, in millimeters, of one DIN rail mounting module (EN 50022 terminal block pitch).</summary>
        public const float DinRailModuleWidthMm = 18f;

        /// <summary>
        /// How each DIN rail segment's text is sized - see <see cref="RenderHelper.RenderDinRailRowImage"/>.
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

        /// <summary>
        /// 1D barcode symbologies exposed for printing/preview - a curated subset of
        /// CodeGlyphX's <c>BarcodeType</c>. 2D symbologies (QR, Micro QR, rMQR, Data
        /// Matrix) have their own <see cref="TwoDSymbology"/> and rendering path - see
        /// <see cref="RenderHelper.RenderTwoDContentImage"/> - since fitting a matrix code into
        /// the printer's fixed ~30px head axis needs a completely different sizing approach than
        /// a 1D barcode.
        /// </summary>
        public enum BarcodeSymbology
        {
            /// <summary>
            /// Not a real symbology to encode with - resolved to a concrete one based on
            /// <c>data</c>'s shape, before rendering.
            /// </summary>
            Auto,
            Code128,
            Code39,
            Codabar,
            Itf,
            Ean13,
            Ean8,
            UpcA,
            UpcE
        }

        /// <summary>Where an optional caption prints relative to a barcode - see <see cref="CaptionOptions"/>.</summary>
        public enum CaptionPosition
        {
            /// <summary>No caption - bars only. Unlike the real Dymo app, this is the default.</summary>
            None,
            Below,
            Above
        }

        /// <summary>How much of the barcode's printable height an enabled caption takes up - see <see cref="RenderHelper.RenderBarcodeContentImage"/>.</summary>
        public enum CaptionSize
        {
            S,
            M,
            L
        }

        /// <summary>
        /// Styling for the optional caption printed alongside a barcode (see
        /// <see cref="RenderHelper.RenderBarcodeContentImage"/>) - always the barcode's own
        /// <c>data</c> as text, bold; what's configurable is where it goes, in what font, how
        /// much height it gets, and how it lines up against the (possibly narrower or wider) bars.
        /// </summary>
        /// <param name="Position">Where to print it, or <see cref="CaptionPosition.None"/> to omit it entirely.</param>
        /// <param name="FontFamily">Font family for the caption text.</param>
        /// <param name="Size">How much of the printable height the caption takes up.</param>
        /// <param name="Align">Horizontal alignment between the caption and the bars, whichever of the two ends up narrower.</param>
        public readonly record struct CaptionOptions(CaptionPosition Position, string FontFamily = "Arial", CaptionSize Size = CaptionSize.M, TextAlign Align = TextAlign.Center)
        {
            /// <summary>No caption - bars only, the only behavior before captions became configurable.</summary>
            public static readonly CaptionOptions None = new(CaptionPosition.None);
        }

        /// <summary>
        /// 2D matrix symbologies exposed for printing/preview - a curated subset of what
        /// CodeGlyphX can encode. Unlike a 1D barcode (see <see cref="BarcodeSymbology"/>), a
        /// matrix code's modules have to stay physically square, so sizing is driven entirely by
        /// how many modules fit the printer's head-axis dots - see
        /// <see cref="RenderHelper.RenderTwoDContentImage"/>.
        /// </summary>
        public enum TwoDSymbology
        {
            /// <summary>
            /// Not a real symbology to encode with - resolved to whichever concrete symbology
            /// prints this data on the least tape while keeping every module at least 2 printer
            /// dots (~0.32 mm) wide. Full QR is never chosen - its 21-module minimum guarantees 1
            /// dot/module on this printer - so <c>Auto</c> only ever yields Micro QR, rMQR or Data
            /// Matrix.
            /// </summary>
            Auto,
            QrCode,
            MicroQrCode,
            RectangularMicroQrCode,
            DataMatrix
        }

        /// <summary>
        /// How a chosen 2D symbol maps onto this printer: what symbol was picked, its module
        /// dimensions, how many printer dots each module gets on each axis, and - since the head
        /// axis is the constraint - the resulting physical module size, so the UI can warn before
        /// printing something too small to scan (<see cref="MayNotScanWell"/>).
        /// </summary>
        /// <param name="Symbology">The concrete symbology used (never <see cref="TwoDSymbology.Auto"/>).</param>
        /// <param name="SymbolName">Human-readable symbol name, e.g. "QR V2", "Micro QR M3", "rMQR R11x27", "Data Matrix 16×16".</param>
        /// <param name="ModulesShort">Module count along the symbol's shorter side (mapped to the printer's head axis).</param>
        /// <param name="ModulesLong">Module count along the symbol's longer side (mapped to the printer's feed axis).</param>
        /// <param name="DotsPerModuleShort">Printer dots per module on the head axis (the "v" in the 1×2 / 2×4 / 4×8 shorthand).</param>
        /// <param name="DotsPerModuleLong">Printer dots per module on the feed axis - always twice <see cref="DotsPerModuleShort"/>, since feed-axis dots are half the physical size of head-axis dots.</param>
        /// <param name="ModuleSizeMm">Physical size of one (square) module, in millimeters.</param>
        /// <param name="MayNotScanWell">
        /// <c>true</c> when each module is only one head-axis dot (~0.16 mm) - technically valid
        /// but at or below the practical limit for phone-camera scanning, and vulnerable to
        /// thermal dot gain. <see cref="TwoDSymbology.Auto"/> never returns a plan with this set.
        /// </param>
        public readonly record struct TwoDPlan(
            TwoDSymbology Symbology,
            string SymbolName,
            int ModulesShort,
            int ModulesLong,
            int DotsPerModuleShort,
            int DotsPerModuleLong,
            float ModuleSizeMm,
            bool MayNotScanWell);
    }
}
