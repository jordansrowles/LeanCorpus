using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

internal sealed class XxHash64Provider : IChecksumProvider
{
    public int ChecksumByteLength => 8;

    public byte[] Compute(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
            return XxHash64.ToBytes(data.FirstSpan);
        return XxHash64.ToBytes(CopyToTemp(data));
    }

    public bool Verify(ReadOnlySequence<byte> data, ReadOnlySpan<byte> expected)
    {
        if (expected.Length != 8) return false;
        ulong hash = data.IsSingleSegment ? XxHash64.Compute(data.FirstSpan) : ComputeThenHash(data);
        ulong exp = (ulong)expected[0] | ((ulong)expected[1] << 8) | ((ulong)expected[2] << 16) | ((ulong)expected[3] << 24)
                 | ((ulong)expected[4] << 32) | ((ulong)expected[5] << 40) | ((ulong)expected[6] << 48) | ((ulong)expected[7] << 56);
        return hash == exp;
    }

    private static byte[] CopyToTemp(ReadOnlySequence<byte> data)
    {
        var len = (int)data.Length;
        if (len <= 4096)
        {
            Span<byte> buf = stackalloc byte[len];
            data.CopyTo(buf);
            return buf.ToArray();
        }
        byte[] rented = ArrayPool<byte>.Shared.Rent(len);
        data.CopyTo(rented);
        var result = rented.AsSpan(0, len).ToArray();
        ArrayPool<byte>.Shared.Return(rented);
        return result;
    }

    private ulong ComputeThenHash(ReadOnlySequence<byte> data)
    {
        var len = (int)data.Length;
        if (len <= 4096)
        {
            Span<byte> buf = stackalloc byte[len];
            data.CopyTo(buf);
            return XxHash64.Compute(buf);
        }
        byte[] rented = ArrayPool<byte>.Shared.Rent(len);
        data.CopyTo(rented);
        ulong hash = XxHash64.Compute(rented.AsSpan(0, len));
        ArrayPool<byte>.Shared.Return(rented);
        return hash;
    }
}
