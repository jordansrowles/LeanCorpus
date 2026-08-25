using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

/// <summary>Incremental xxHash64 state used by streaming codec-file writes.</summary>
internal sealed class XxHash64Accumulator : IFileChecksumAccumulator
{
    private const ulong Prime1 = 11400714785074694791ul;
    private const ulong Prime2 = 14029467366897019727ul;
    private const ulong Prime3 = 1609587929392839161ul;
    private const ulong Prime4 = 9650029242287828579ul;
    private const ulong Prime5 = 2870177450012600261ul;
    private const int StripeLength = 32;

    private readonly byte[] _remainder = new byte[StripeLength];
    private ulong _lane1 = unchecked(Prime1 + Prime2);
    private ulong _lane2 = Prime2;
    private ulong _lane3;
    private ulong _lane4 = unchecked(0ul - Prime1);
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

    public ulong GetCurrentHash()
    {
        ulong hash;
        if (_totalLength >= StripeLength)
        {
            hash = RotateLeft(_lane1, 1)
                + RotateLeft(_lane2, 7)
                + RotateLeft(_lane3, 12)
                + RotateLeft(_lane4, 18);
            hash = MergeLane(hash, _lane1);
            hash = MergeLane(hash, _lane2);
            hash = MergeLane(hash, _lane3);
            hash = MergeLane(hash, _lane4);
        }
        else
        {
            hash = Prime5;
        }

        hash += _totalLength;
        var remaining = _remainder.AsSpan(0, _remainderLength);
        int offset = 0;
        while (remaining.Length - offset >= sizeof(ulong))
        {
            ulong lane = Round(0, BinaryPrimitives.ReadUInt64LittleEndian(remaining[offset..]));
            hash ^= lane;
            hash = RotateLeft(hash, 27) * Prime1 + Prime4;
            offset += sizeof(ulong);
        }

        if (remaining.Length - offset >= sizeof(uint))
        {
            hash ^= BinaryPrimitives.ReadUInt32LittleEndian(remaining[offset..]) * Prime1;
            hash = RotateLeft(hash, 23) * Prime2 + Prime3;
            offset += sizeof(uint);
        }

        while (offset < remaining.Length)
        {
            hash ^= remaining[offset] * Prime5;
            hash = RotateLeft(hash, 11) * Prime1;
            offset++;
        }

        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    public ulong GetChecksum() => GetCurrentHash();

    private void ProcessStripe(ReadOnlySpan<byte> stripe)
    {
        _lane1 = Round(_lane1, BinaryPrimitives.ReadUInt64LittleEndian(stripe));
        _lane2 = Round(_lane2, BinaryPrimitives.ReadUInt64LittleEndian(stripe[8..]));
        _lane3 = Round(_lane3, BinaryPrimitives.ReadUInt64LittleEndian(stripe[16..]));
        _lane4 = Round(_lane4, BinaryPrimitives.ReadUInt64LittleEndian(stripe[24..]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong accumulator, ulong input)
    {
        accumulator += input * Prime2;
        accumulator = RotateLeft(accumulator, 31);
        return accumulator * Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeLane(ulong hash, ulong lane)
    {
        hash ^= Round(0, lane);
        return hash * Prime1 + Prime4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong value, int count)
        => (value << count) | (value >> (64 - count));
}
