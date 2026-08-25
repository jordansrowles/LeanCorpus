using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

internal interface IFileChecksumAccumulator
{
    void Append(ReadOnlySpan<byte> data);

    ulong GetChecksum();
}

internal sealed class NoFileChecksumAccumulator : IFileChecksumAccumulator
{
    public void Append(ReadOnlySpan<byte> data)
    {
    }

    public ulong GetChecksum() => 0;
}

internal sealed class Crc32Accumulator : IFileChecksumAccumulator
{
    private static readonly uint[] Table = BuildTable();
    private uint _crc = uint.MaxValue;

    public void Append(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
            _crc = Table[(_crc ^ data[i]) & 0xff] ^ (_crc >> 8);
    }

    public ulong GetChecksum() => _crc ^ uint.MaxValue;

    private static uint[] BuildTable()
    {
        const uint polynomial = 0xedb8_8320;
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint entry = i;
            for (int bit = 0; bit < 8; bit++)
                entry = (entry & 1) != 0 ? (entry >> 1) ^ polynomial : entry >> 1;
            table[i] = entry;
        }
        return table;
    }
}

internal sealed class XxHash32Accumulator : IFileChecksumAccumulator
{
    private const uint Prime1 = 2654435761u;
    private const uint Prime2 = 2246822519u;
    private const uint Prime3 = 3266489917u;
    private const uint Prime4 = 668265263u;
    private const uint Prime5 = 374761393u;
    private const int StripeLength = 16;

    private readonly byte[] _remainder = new byte[StripeLength];
    private uint _lane1 = unchecked(Prime1 + Prime2);
    private uint _lane2 = Prime2;
    private uint _lane3;
    private uint _lane4 = unchecked(0u - Prime1);
    private ulong _totalLength;
    private int _remainderLength;

    public void Append(ReadOnlySpan<byte> data)
    {
        _totalLength += (uint)data.Length;
        if (_remainderLength + data.Length < StripeLength)
        {
            data.CopyTo(_remainder.AsSpan(_remainderLength));
            _remainderLength += data.Length;
            return;
        }

        if (_remainderLength > 0)
        {
            int needed = StripeLength - _remainderLength;
            data[..needed].CopyTo(_remainder.AsSpan(_remainderLength));
            ProcessStripe(_remainder);
            data = data[needed..];
            _remainderLength = 0;
        }

        while (data.Length >= StripeLength)
        {
            ProcessStripe(data[..StripeLength]);
            data = data[StripeLength..];
        }

        data.CopyTo(_remainder);
        _remainderLength = data.Length;
    }

    public ulong GetChecksum()
    {
        uint hash = _totalLength >= StripeLength
            ? RotateLeft(_lane1, 1) + RotateLeft(_lane2, 7) + RotateLeft(_lane3, 12) + RotateLeft(_lane4, 18)
            : Prime5;
        hash += (uint)_totalLength;

        var remaining = _remainder.AsSpan(0, _remainderLength);
        int offset = 0;
        while (remaining.Length - offset >= sizeof(uint))
        {
            hash += BinaryPrimitives.ReadUInt32LittleEndian(remaining[offset..]) * Prime3;
            hash = RotateLeft(hash, 17) * Prime4;
            offset += sizeof(uint);
        }

        while (offset < remaining.Length)
        {
            hash += remaining[offset] * Prime5;
            hash = RotateLeft(hash, 11) * Prime1;
            offset++;
        }

        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    private void ProcessStripe(ReadOnlySpan<byte> stripe)
    {
        _lane1 = Round(_lane1, BinaryPrimitives.ReadUInt32LittleEndian(stripe));
        _lane2 = Round(_lane2, BinaryPrimitives.ReadUInt32LittleEndian(stripe[4..]));
        _lane3 = Round(_lane3, BinaryPrimitives.ReadUInt32LittleEndian(stripe[8..]));
        _lane4 = Round(_lane4, BinaryPrimitives.ReadUInt32LittleEndian(stripe[12..]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Round(uint accumulator, uint input)
    {
        accumulator += input * Prime2;
        accumulator = RotateLeft(accumulator, 13);
        return accumulator * Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint value, int count)
        => (value << count) | (value >> (32 - count));
}
