using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Search.Internal;

/// <summary>Shared endian-independent sortable coordinate primitives.</summary>
internal static class SortableCoordinateEncoding
{
    internal static void WriteSortableInt32(int value, Span<byte> destination)
    {
        if (destination.Length < sizeof(int))
            throw new ArgumentException("The destination needs four bytes.", nameof(destination));
        BinaryPrimitives.WriteUInt32BigEndian(destination, unchecked((uint)(value ^ int.MinValue)));
    }

    internal static int ReadSortableInt32(ReadOnlySpan<byte> source)
    {
        if (source.Length < sizeof(int))
            throw new ArgumentException("The source needs four bytes.", nameof(source));
        uint sortable = BinaryPrimitives.ReadUInt32BigEndian(source);
        return unchecked((int)(sortable ^ 0x8000_0000u));
    }

    internal static uint SortableFloatBits(float value)
    {
        if (value == 0f)
            value = 0f;
        int bits = BitConverter.SingleToInt32Bits(value);
        uint raw = unchecked((uint)bits);
        return (raw & 0x8000_0000u) != 0 ? ~raw : raw ^ 0x8000_0000u;
    }

    internal static float UnsortableFloatBits(uint sortable)
    {
        uint raw = (sortable & 0x8000_0000u) != 0
            ? sortable ^ 0x8000_0000u
            : ~sortable;
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    internal static void WriteSortableFloat(float value, Span<byte> destination)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "The coordinate must be finite.");
        if (destination.Length < sizeof(float))
            throw new ArgumentException("The destination needs four bytes.", nameof(destination));
        BinaryPrimitives.WriteUInt32BigEndian(destination, SortableFloatBits(value));
    }

    internal static float ReadSortableFloat(ReadOnlySpan<byte> source)
    {
        if (source.Length < sizeof(float))
            throw new ArgumentException("The source needs four bytes.", nameof(source));
        return UnsortableFloatBits(BinaryPrimitives.ReadUInt32BigEndian(source));
    }
}
