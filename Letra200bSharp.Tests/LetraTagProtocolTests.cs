using Letra200bSharp.LetraTag200b;

namespace Letra200bSharp.Tests;

public class LetraTagProtocolTests
{
    private static LabelBitmap Blank(int length) => new(length, 32, new byte[length * 32]);

    [Fact]
    public void Header_carries_body_length_and_checksum()
    {
        var job = LetraTagProtocol.Encode(Blank(8));
        var header = job[0];

        Assert.Equal(new byte[] { 0xFF, 0xF0, 0x12, 0x34 }, header[..4]);
        int bodyLength = BitConverter.ToInt32(header, 4);
        Assert.Equal(job.Skip(1).Sum(chunk => chunk.Length - 1) - 2, bodyLength);
        Assert.Equal(header[..^1].Sum(b => b) & 0xFF, header[^1]);
    }

    [Fact]
    public void Body_starts_with_print_data_and_ends_with_feed_status_end()
    {
        var job = LetraTagProtocol.Encode(Blank(8));
        var body = job.Skip(1).SelectMany(chunk => chunk.Skip(1)).ToArray()[..^2];

        Assert.Equal(new byte[] { 0x1B, 0x73, 0x9A, 0x02, 0x00, 0x00, 0x1B, 0x44, 0x01, 0x02 }, body[..10]);
        Assert.Equal(8, BitConverter.ToInt32(body, 10));  // feed-axis length
        Assert.Equal(32, BitConverter.ToInt32(body, 14)); // head dots
        Assert.Equal(new byte[] { 0x1B, 0x45, 0x1B, 0x41, 0x1B, 0x51 }, body[^6..]);
    }

    [Fact]
    public void Chunk_sequence_numbers_skip_the_escape_byte()
    {
        // Long enough for well over 27 chunks of 300 bytes.
        var job = LetraTagProtocol.Encode(Blank(3000));
        var sequence = job.Skip(1).Select(chunk => chunk[0]).ToList();

        Assert.DoesNotContain((byte)0x1B, sequence);
        Assert.Equal(Enumerable.Range(0, 27).Select(i => (byte)i), sequence.Take(27));
        Assert.Equal((byte)0x1C, sequence[27]);
        Assert.Equal(new byte[] { 0x12, 0x34 }, job[^1][^2..]);
    }

    [Fact]
    public void Dots_are_packed_head_axis_major_least_significant_bit_first()
    {
        var dots = new byte[2 * 32];
        dots[0] = 1;      // feed 0, head 0
        dots[9] = 1;      // feed 0, head 9
        dots[32 + 31] = 1; // feed 1, head 31
        var job = LetraTagProtocol.Encode(new LabelBitmap(2, 32, dots));
        var body = job.Skip(1).SelectMany(chunk => chunk.Skip(1)).ToArray();
        var packed = body.AsSpan(18, 8).ToArray();

        Assert.Equal(new byte[] { 0x01, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, packed);
    }
}
