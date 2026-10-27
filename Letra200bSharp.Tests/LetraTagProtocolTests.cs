using Letra200bSharp.LetraTag200b;

namespace Letra200bSharp.Tests;

public class LetraTagProtocolTests
{
    private static LabelBitmap Blank(int length) => new(length, 32, new byte[length * 32]);

    /// <summary>The chunked body with each chunk's sequence byte (and the last chunk's 2-byte trailer) stripped.</summary>
    private static byte[] Body(List<byte[]> job) => job.Skip(1).SelectMany(chunk => chunk.Skip(1)).ToArray()[..^2];

    /// <summary>
    /// The shortest feed-axis length whose body needs exactly <paramref name="chunks"/> 300-byte
    /// chunks: the body is 24 bytes of commands plus 4 bytes per 32-dot column.
    /// </summary>
    private static int LengthForChunks(int chunks) => ((chunks - 1) * 300 - 20) / 4;

    [Test]
    public async Task Header_carries_body_length_and_checksum()
    {
        var job = LetraTagProtocol.Encode(Blank(8));
        var header = job[0];

        await Assert.That(Convert.ToHexString(header[..4])).IsEqualTo("FFF01234");
        await Assert.That(BitConverter.ToInt32(header, 4)).IsEqualTo(Body(job).Length);
        await Assert.That((int)header[^1]).IsEqualTo(header[..^1].Sum(b => b) & 0xFF);
    }

    [Test]
    public async Task Body_starts_with_print_data_and_ends_with_feed_status_end()
    {
        var body = Body(LetraTagProtocol.Encode(Blank(8)));

        await Assert.That(Convert.ToHexString(body[..10])).IsEqualTo("1B739A0200001B440102");
        await Assert.That(BitConverter.ToInt32(body, 10)).IsEqualTo(8);  // feed-axis length
        await Assert.That(BitConverter.ToInt32(body, 14)).IsEqualTo(32); // head dots
        await Assert.That(Convert.ToHexString(body[^6..])).IsEqualTo("1B451B411B51");
    }

    [Test]
    public async Task Dots_are_packed_head_axis_major_least_significant_bit_first()
    {
        var dots = new byte[2 * 32];
        dots[0] = 1;       // feed 0, head 0
        dots[9] = 1;       // feed 0, head 9
        dots[32 + 31] = 1; // feed 1, head 31
        var body = Body(LetraTagProtocol.Encode(new LabelBitmap(2, 32, dots)));

        await Assert.That(Convert.ToHexString(body[18..26])).IsEqualTo("0102000000000080");
    }

    [Test]
    public async Task Body_is_split_into_300_byte_chunks_and_the_last_one_carries_the_trailer()
    {
        var chunks = LetraTagProtocol.Encode(Blank(400)).Skip(1).ToList();

        foreach (var chunk in chunks[..^1])
        {
            await Assert.That(chunk.Length).IsEqualTo(301);
        }

        await Assert.That(Convert.ToHexString(chunks[^1][^2..])).IsEqualTo("1234");
    }

    // ---- The ESC (0x1B) sequence-number bug ----------------------------------------------
    // Each chunk starts with a 1-byte sequence number. The printer's firmware reads 0x1B (ESC,
    // 27) as the start of a command, so a chunk numbered 27 was misread and the job stalled as
    // soon as a label needed 28 or more chunks. The counter must skip 27 - also after it wraps
    // past 255.

    /// <summary>The reference protocol's numbering: 0, 1, ..., 26, 28, ..., 255, 0, ..., 26, 28, ...</summary>
    private static IEnumerable<byte> ExpectedSequence()
    {
        byte sequence = 0;
        while (true)
        {
            if (sequence == 0x1B)
            {
                sequence++;
            }

            yield return sequence++;
        }
    }

    [Test]
    [Arguments(26)]
    [Arguments(27)]
    [Arguments(28)]
    [Arguments(29)]
    [Arguments(255)]
    [Arguments(256)]
    [Arguments(283)]
    [Arguments(284)]
    [Arguments(285)]
    public async Task Sequence_numbers_never_use_the_escape_byte(int chunkCount)
    {
        var job = LetraTagProtocol.Encode(Blank(LengthForChunks(chunkCount)));
        var sequence = job.Skip(1).Select(chunk => chunk[0]).ToArray();

        await Assert.That(sequence.Length).IsEqualTo(chunkCount);
        await Assert.That(sequence.Contains((byte)0x1B)).IsFalse();
        await Assert.That(Convert.ToHexString(sequence)).IsEqualTo(Convert.ToHexString(ExpectedSequence().Take(chunkCount).ToArray()));
    }

    [Test]
    public async Task Skipping_the_escape_byte_neither_loses_nor_duplicates_data()
    {
        var random = new Random(27);
        var dots = new byte[LengthForChunks(300) * 32];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i] = (byte)random.Next(2);
        }

        var body = Body(LetraTagProtocol.Encode(new LabelBitmap(dots.Length / 32, 32, dots)));

        // Unpack the print data back into dots: it must be exactly what went in.
        var packed = body[18..^6];
        var unpacked = Enumerable.Range(0, dots.Length).Select(i => (byte)((packed[i / 8] >> (i % 8)) & 1)).ToArray();
        await Assert.That(Convert.ToHexString(unpacked)).IsEqualTo(Convert.ToHexString(dots));
    }

    // ---- Print reply status codes --------------------------------------------------------

    [Test]
    [Arguments((byte)0, true)]
    [Arguments((byte)1, true)]
    [Arguments((byte)2, false)]
    [Arguments((byte)3, true)]
    [Arguments((byte)4, false)]
    [Arguments((byte)5, false)]
    [Arguments((byte)6, false)]
    [Arguments((byte)7, false)]
    [Arguments((byte)42, false)]
    public async Task Status_codes_map_to_printed_or_not(byte status, bool printed)
    {
        var result = LetraTagBluetoothTransport.InterpretStatusCode(status);

        await Assert.That(result.Printed).IsEqualTo(printed);
        await Assert.That(result.StatusCode).IsEqualTo(status);
        await Assert.That(string.IsNullOrWhiteSpace(result.Message)).IsFalse();
    }
}
