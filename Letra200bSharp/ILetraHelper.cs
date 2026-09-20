using System.Collections.Generic;

namespace Letra200bSharp
{
    /// <summary>
    /// Builds the Dymo LetraTag 200B's BLE wire format (header, chunked body, checksum) for a
    /// print job - the "communication" half. Rendering content into pixels is delegated to an
    /// injected <see cref="IRenderHelper"/> rather than done here - see <see cref="IRenderHelper"/>
    /// for that half.
    /// </summary>
    public interface ILetraHelper
    {
        /// <summary>See <see cref="LetraHelper.CreateJob(byte[], bool, bool)"/>.</summary>
        List<byte[]> CreateJob(byte[] imageBytes, bool noCut = false, bool preRendered = false);

        /// <summary>See <see cref="LetraHelper.CreateJob(string, string, LetraHelper.LabelTextSize, LetraHelper.TextStyle, bool, float, LetraHelper.TextBoxStyle, LetraHelper.TextAlign, bool)"/>.</summary>
        List<byte[]> CreateJob(string text, string fontFamily = "Arial", LetraHelper.LabelTextSize size = LetraHelper.LabelTextSize.M, LetraHelper.TextStyle style = LetraHelper.TextStyle.Normal, bool upperCase = false, float widthScale = 1f, LetraHelper.TextBoxStyle boxStyle = LetraHelper.TextBoxStyle.None, LetraHelper.TextAlign align = LetraHelper.TextAlign.Left, bool noCut = false);

        /// <summary>See <see cref="LetraHelper.CreateJob(string, LetraHelper.BarcodeSymbology, bool, LetraHelper.CaptionOptions)"/>.</summary>
        List<byte[]> CreateJob(string data, LetraHelper.BarcodeSymbology symbology, bool noCut = false, LetraHelper.CaptionOptions caption = default);

        /// <summary>See <see cref="LetraHelper.CreateJob(string, LetraHelper.TwoDSymbology)"/>.</summary>
        List<byte[]> CreateJob(string data, LetraHelper.TwoDSymbology symbology);

        /// <summary>See <see cref="LetraHelper.CreateDinRailRowJob"/>.</summary>
        List<byte[]> CreateDinRailRowJob(IReadOnlyList<(string Text, decimal Modules)> rows, string fontFamily, LetraHelper.TextStyle style, bool upperCase, LetraHelper.TextAlign align, LetraHelper.DinRailSizing sizing, bool showSeparators, bool noCut = false);

        /// <summary>See <see cref="LetraHelper.CreateComposedJob"/>.</summary>
        List<byte[]> CreateComposedJob(IReadOnlyList<byte[]> elementPngs);
    }
}
