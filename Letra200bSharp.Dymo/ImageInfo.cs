namespace Letra200bSharp.Dymo
{
    /// <summary>The printer-ready dots produced by <see cref="RenderHelper.PrepareImage"/> - see <see cref="LabelBitmap"/> for the layout of <see cref="Data"/>.</summary>
    internal sealed class ImageInfo
    {
        /// <summary>Dots along the feed axis (label length).</summary>
        public int Width { get; init; }

        /// <summary>Dots across the print head.</summary>
        public int Height { get; init; }

        public required byte[] Data { get; init; }
    }
}
