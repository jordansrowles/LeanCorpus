using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

/// <summary>
/// CRC-32/ISO-HDLC (IEEE 802.3) checksum provider.
/// Purely managed, thread-safe implementation using a precomputed 256-entry lookup table.
/// </summary>
internal sealed class Crc32Provider : IChecksumProvider
{
    private static readonly uint[] Table = BuildTable();

    public int ChecksumByteLength => 4;

    public byte[] Compute(ReadOnlySequence<byte> data)
    {
        uint crc = 0xFFFF_FFFF;

        foreach (ReadOnlyMemory<byte> segment in data)
        {
            ReadOnlySpan<byte> span = segment.Span;
            for (int i = 0; i < span.Length; i++)
            {
                crc = Table[(crc ^ span[i]) & 0xFF] ^ (crc >> 8);
            }
        }

        crc ^= 0xFFFF_FFFF;

        byte[] result = new byte[4];
        result[0] = (byte)crc;
        result[1] = (byte)(crc >> 8);
        result[2] = (byte)(crc >> 16);
        result[3] = (byte)(crc >> 24);
        return result;
    }

    public bool Verify(ReadOnlySequence<byte> data, ReadOnlySpan<byte> expectedChecksum)
    {
        if (expectedChecksum.Length != ChecksumByteLength)
            return false;

        byte[] computed = Compute(data);
        return computed.AsSpan().SequenceEqual(expectedChecksum);
    }

    private static uint[] BuildTable()
    {
        const uint polynomial = 0xEDB8_8320;
        uint[] table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint entry = i;
            for (int j = 0; j < 8; j++)
            {
                entry = (entry & 1) != 0
                    ? (entry >> 1) ^ polynomial
                    : entry >> 1;
            }
            table[i] = entry;
        }

        return table;
    }
}
