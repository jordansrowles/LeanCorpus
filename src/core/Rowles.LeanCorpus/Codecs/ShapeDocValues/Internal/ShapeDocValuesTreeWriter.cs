using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal static class ShapeDocValuesTreeWriter
{
    private const int LeafCapacity = 16;
    private const int NodePrefixLength = 24;
    private const int PrimitiveLength = ShapePrimitiveCodec.PackedValueLength;

    internal static byte[] Build(ReadOnlyMemory<byte> primitives)
    {
        if (primitives.Length == 0 || primitives.Length % PrimitiveLength != 0)
            throw new InvalidDataException("Shape DocValues tree input is empty or has an invalid primitive length.");
        int primitiveCount = primitives.Length / PrimitiveLength;
        int leafCount = CountLeaves(primitiveCount);
        int capacity = checked((primitiveCount * PrimitiveLength) + (leafCount * 56) - 28);
        using var stream = new MemoryStream(capacity);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        int[] ordinals = Enumerable.Range(0, primitiveCount).ToArray();
        var comparer = new PrimitivePartitionComparer(primitives);
        WriteNode(writer, stream, ordinals, 0, primitiveCount, depth: 0, comparer);
        if (stream.Length != capacity)
            throw new InvalidDataException("Shape DocValues tree length does not match its computed size.");
        return stream.ToArray();
    }

    private static int WriteNode(
        BinaryWriter writer,
        MemoryStream stream,
        int[] ordinals,
        int start,
        int count,
        int depth,
        PrimitivePartitionComparer comparer)
    {
        if (depth > 64)
            throw new InvalidDataException("Shape DocValues tree exceeds the maximum depth of 64.");

        long nodeStart = stream.Position;
        NodeBounds bounds = GetBounds(comparer.Primitives, ordinals, start, count);
        writer.Write(0);
        bool leaf = count <= LeafCapacity;
        writer.Write(leaf ? (byte)1 : (byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        Span<byte> boundBytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes, bounds.MinimumD0);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[4..], bounds.MinimumD1);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[8..], bounds.MaximumD2);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[12..], bounds.MaximumD3);
        writer.Write(boundBytes);

        if (leaf)
        {
            comparer.Axis = -1;
            Array.Sort(ordinals, start, count, comparer);
            writer.Write(checked((ushort)count));
            writer.Write((ushort)0);
            ReadOnlySpan<byte> bytes = comparer.Primitives.Span;
            for (int i = start; i < start + count; i++)
                writer.Write(bytes.Slice(ordinals[i] * PrimitiveLength, PrimitiveLength));
        }
        else
        {
            comparer.Axis = depth % 2;
            Array.Sort(ordinals, start, count, comparer);
            writer.Write(0u);
            int leftCount = count / 2;
            int leftLength = WriteNode(writer, stream, ordinals, start, leftCount, depth + 1, comparer);
            long leftLengthPosition = nodeStart + NodePrefixLength;
            long returnPosition = stream.Position;
            stream.Position = leftLengthPosition;
            writer.Write(checked((uint)leftLength));
            stream.Position = returnPosition;
            int rightCount = count - leftCount;
            _ = WriteNode(writer, stream, ordinals, start + leftCount, rightCount, depth + 1, comparer);
        }

        int nodeLength = checked((int)(stream.Position - nodeStart));
        long nodeEnd = stream.Position;
        stream.Position = nodeStart;
        writer.Write(nodeLength);
        stream.Position = nodeEnd;
        return nodeLength;
    }

    private static NodeBounds GetBounds(
        ReadOnlyMemory<byte> primitives,
        int[] ordinals,
        int start,
        int count)
    {
        ReadOnlySpan<byte> bytes = primitives.Span;
        uint minimumD0 = uint.MaxValue;
        uint minimumD1 = uint.MaxValue;
        uint maximumD2 = uint.MinValue;
        uint maximumD3 = uint.MinValue;
        for (int i = start; i < start + count; i++)
        {
            ReadOnlySpan<byte> primitive = bytes.Slice(ordinals[i] * PrimitiveLength, PrimitiveLength);
            minimumD0 = Math.Min(minimumD0, BinaryPrimitives.ReadUInt32BigEndian(primitive));
            minimumD1 = Math.Min(minimumD1, BinaryPrimitives.ReadUInt32BigEndian(primitive[4..]));
            maximumD2 = Math.Max(maximumD2, BinaryPrimitives.ReadUInt32BigEndian(primitive[8..]));
            maximumD3 = Math.Max(maximumD3, BinaryPrimitives.ReadUInt32BigEndian(primitive[12..]));
        }
        return new NodeBounds(minimumD0, minimumD1, maximumD2, maximumD3);
    }

    private static int CountLeaves(int count)
        => count <= LeafCapacity ? 1 : checked(CountLeaves(count / 2) + CountLeaves(count - (count / 2)));

    private readonly record struct NodeBounds(uint MinimumD0, uint MinimumD1, uint MaximumD2, uint MaximumD3);

    private sealed class PrimitivePartitionComparer(ReadOnlyMemory<byte> primitives) : IComparer<int>
    {
        internal ReadOnlyMemory<byte> Primitives => primitives;
        internal int Axis { get; set; } = -1;

        public int Compare(int leftOrdinal, int rightOrdinal)
        {
            ReadOnlySpan<byte> bytes = primitives.Span;
            ReadOnlySpan<byte> left = bytes.Slice(leftOrdinal * PrimitiveLength, PrimitiveLength);
            ReadOnlySpan<byte> right = bytes.Slice(rightOrdinal * PrimitiveLength, PrimitiveLength);
            if (Axis >= 0)
            {
                int minDimension = Axis == 0 ? 4 : 0;
                int maxDimension = Axis == 0 ? 12 : 8;
                ulong leftCentre = (ulong)BinaryPrimitives.ReadUInt32BigEndian(left.Slice(minDimension, 4))
                    + BinaryPrimitives.ReadUInt32BigEndian(left.Slice(maxDimension, 4));
                ulong rightCentre = (ulong)BinaryPrimitives.ReadUInt32BigEndian(right.Slice(minDimension, 4))
                    + BinaryPrimitives.ReadUInt32BigEndian(right.Slice(maxDimension, 4));
                int centreComparison = leftCentre.CompareTo(rightCentre);
                if (centreComparison != 0)
                    return centreComparison;
            }

            int bytesComparison = left.SequenceCompareTo(right);
            return bytesComparison != 0 ? bytesComparison : leftOrdinal.CompareTo(rightOrdinal);
        }
    }
}
