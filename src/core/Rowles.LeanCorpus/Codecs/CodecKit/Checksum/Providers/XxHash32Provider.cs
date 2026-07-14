using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

internal sealed class XxHash32Provider : IChecksumProvider
{
    public int ChecksumByteLength => 4;

    public byte[] Compute(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return XxHash32.ToBytes(data.FirstSpan);
        var buf = new byte[(int)data.Length];
        data.CopyTo(buf);
        return XxHash32.ToBytes(buf);
    }

    public bool Verify(ReadOnlySequence<byte> data, ReadOnlySpan<byte> expected)
    {
        if (expected.Length != 4) return false;
        uint hash = data.IsSingleSegment ? XxHash32.Compute(data.FirstSpan) : ComputeThenHash(data);
        uint exp = (uint)expected[0] | ((uint)expected[1] << 8) | ((uint)expected[2] << 16) | ((uint)expected[3] << 24);
        return hash == exp;
    }

    private uint ComputeThenHash(ReadOnlySequence<byte> data)
    {
        var buf = new byte[(int)data.Length];
        data.CopyTo(buf);
        return XxHash32.Compute(buf);
    }
}
