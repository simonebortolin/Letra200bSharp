namespace Letra200bSharp.LetraTag200b
{
    /// <summary>
    /// The Dymo LetraTag 200B's BLE wire format for a print job (header, chunked body, checksum),
    /// reverse-engineered as documented at https://github.com/ysfchn/dymo-bluetooth. Knows nothing
    /// about rendering - it encodes an already-rendered <see cref="LabelBitmap"/>.
    /// </summary>
    internal static class LetraTagProtocol
    {
        /// <summary>Form feed command.</summary>
        private static readonly byte[] FormFeed = { 0x1B, 0x45 };

        /// <summary>Status command.</summary>
        private static readonly byte[] Status = { 0x1B, 0x41 };

        /// <summary>End of data.</summary>
        private static readonly byte[] End = { 0x1B, 0x51 };

        /// <summary>Start of the job.</summary>
        private static readonly byte[] StartJob = { 0x1B, 0x73, 0x9A, 0x02, 0x00, 0x00 };

        /// <summary>Body bytes per chunk (before the 1-byte sequence number).</summary>
        private const int ChunkSize = 300;

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
        /// <returns>The packets to write, in order, to the printer's "print request" characteristic.</returns>
        public static List<byte[]> Encode(LabelBitmap bitmap)
        {
            var dots = bitmap.Dots;
            byte[] packedData = new byte[(dots.Length + 7) / 8];
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == 1)
                {
                    packedData[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            var body = new List<byte>();
            body.AddRange(StartJob);
            body.AddRange(GetPrintData(packedData, bitmap.Length, bitmap.HeadDots));
            body.AddRange(FormFeed);
            body.AddRange(Status);
            body.AddRange(End);

            byte[] header = GetHeaderBytes(body.Count);
            var chunks = SplitChunks(body.ToArray());

            var result = new List<byte[]> { header };
            result.AddRange(chunks);
            return result;
        }

        private static int CalculateChecksum(ReadOnlySpan<byte> data)
        {
            var checksum = 0;
            foreach (byte b in data)
            {
                checksum += b;
            }
            return checksum & 0xFF;
        }

        private static List<byte[]> SplitChunks(byte[] data)
        {
            var chunks = new List<byte[]>();

            // The 1-byte sequence number prefixing each chunk must never be 0x1B (27):
            // that's the same byte the printer's firmware uses to recognize the start of
            // an ESC command, so a chunk sequenced 27 gets misread as a command instead of
            // data, stalling the job. Skip 27 (and, once the counter wraps past 255, 283,
            // etc.) the same way the reference protocol does.
            byte sequence = 0;

            for (int i = 0; i < data.Length; i += ChunkSize)
            {
                int end = Math.Min(i + ChunkSize, data.Length);
                bool isLast = end == data.Length;

                // The last chunk carries 2 extra trailing bytes.
                byte[] chunk = new byte[end - i + (isLast ? 3 : 1)];

                if (sequence == 0x1B) // 27
                {
                    sequence++;
                }
                chunk[0] = sequence;
                sequence++;

                Array.Copy(data, i, chunk, 1, end - i);

                if (isLast)
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
            header[header.Length - 1] = (byte)CalculateChecksum(header.AsSpan(0, header.Length - 1));
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
    }
}
