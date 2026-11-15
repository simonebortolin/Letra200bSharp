using SkiaSharp;

namespace Letra200bSharp.Tests;

/// <summary>
/// Font-independent measurements of rendered labels, so the same assertions hold on every OS
/// whatever fonts it ships.
/// </summary>
internal static class Ink
{
    public static int Count(LabelBitmap bitmap)
    {
        int count = 0;
        foreach (var dot in bitmap.Dots)
        {
            count += dot;
        }

        return count;
    }

    /// <summary>Whether any dot of head row <paramref name="head"/> is inked.</summary>
    public static bool InRow(LabelBitmap bitmap, int head) =>
        Enumerable.Range(0, bitmap.Length).Any(feed => bitmap[feed, head]);

    /// <summary>Inked dots across the head rows <paramref name="from"/> to <paramref name="to"/> (inclusive).</summary>
    public static int InRows(LabelBitmap bitmap, int from, int to) =>
        Enumerable.Range(0, bitmap.Length).Sum(feed => Enumerable.Range(from, to - from + 1).Count(head => bitmap[feed, head]));

    /// <summary>Number of head rows that carry any ink.</summary>
    public static int RowSpan(LabelBitmap bitmap)
    {
        var rows = Enumerable.Range(0, bitmap.HeadDots).Where(head => InRow(bitmap, head)).ToList();
        return rows.Count == 0 ? 0 : rows[^1] - rows[0] + 1;
    }

    /// <summary>The dots as a hex string - compares two bitmaps dot for dot, with a readable diff.</summary>
    public static string Hex(LabelBitmap bitmap) => Convert.ToHexString(bitmap.Dots);
}

internal static class Images
{
    public static byte[] Png(int width, int height, Action<SKCanvas> draw, SKColor? background = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background ?? SKColors.White);
            draw(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    public static SKBitmap Decode(byte[] png) => SKBitmap.Decode(png);
}
