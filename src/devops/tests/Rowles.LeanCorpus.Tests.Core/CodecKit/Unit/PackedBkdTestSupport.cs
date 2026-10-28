using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

internal static class PackedBkdTestSupport
{
    internal static PackedBkdFieldBuffer CreateBuffer(bool reverse)
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
        byte[] packed = new byte[8];
        IEnumerable<int> ids = reverse ? Enumerable.Range(0, 10).Reverse() : Enumerable.Range(0, 10);
        foreach (int id in ids)
        {
            XYEncodingUtils.Encode(id, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(id, packed.AsSpan(4, 4));
            buffer.Append(packed, id);
        }
        return buffer;
    }

    internal static void AppendPoint(PackedBkdFieldBuffer buffer, float x, float y, int document)
    {
        byte[] packed = new byte[8];
        XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
        XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
        buffer.Append(packed, document);
    }

    internal static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static byte[] ReadBody(string path)
    {
        using var input = new IndexInput(path);
        using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
        session.ValidateChecksum();
        using var body = session.OpenBodyInput();
        byte[] bytes = new byte[checked((int)body.Length)];
        body.ReadBytes(bytes);
        return bytes;
    }

    internal static void RewriteBody(string path, byte[] body)
    {
        string replacement = path + ".rewrite";
        CodecFileWriter.WriteAtomically(
            replacement,
            PackedBkdCodecFiles.Descriptor,
            durable: false,
            output => output.WriteBytes(body));
        File.Move(replacement, path, overwrite: true);
    }

    internal static LeafHeader ReadFirstLeafHeader(string path)
    {
        byte[] body = ReadBody(path);
        int indexedDimensions = body[5];
        int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
        int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
        int offsetsStart = checked(32 + indexedDimensions * 4 * 2 + splitCount * 5);
        int leafDataStart = checked(offsetsStart + (leafCount + 1) * sizeof(long));
        return new LeafHeader(
            body[leafDataStart + 2],
            body[leafDataStart + 3],
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(leafDataStart + 4, sizeof(int))));
    }

    internal readonly record struct LeafHeader(int DocumentWidth, byte Encoding, int MinimumDocument);

    internal struct RangeVisitor(XYPoint minimum, XYPoint maximum) : IPackedBkdIntersectVisitor
    {
        private readonly byte[] _minimum = Pack(minimum);
        private readonly byte[] _maximum = Pack(maximum);
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        {
            if (IsOutside(minimum, maximum))
                return PackedBkdCellRelation.Outside;
            return IsInside(minimum, maximum)
                ? PackedBkdCellRelation.Inside
                : PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (IsValueInRange(packedValue))
                Documents.Add(docId);
        }

        private bool IsOutside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => maximum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) < 0
                || minimum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) > 0
                || maximum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) < 0
                || minimum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) > 0;

        private bool IsInside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => minimum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && maximum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && minimum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && maximum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private bool IsValueInRange(ReadOnlySpan<byte> packedValue)
            => packedValue[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && packedValue[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && packedValue[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && packedValue[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private static byte[] Pack(XYPoint point)
        {
            byte[] result = new byte[8];
            XYEncodingUtils.Encode(point.X, result.AsSpan(0, 4));
            XYEncodingUtils.Encode(point.Y, result.AsSpan(4, 4));
            return result;
        }
    }

    internal struct VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; }

        public VisitAllVisitor()
        {
            Documents = [];
        }

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);
        public void Visit(int docId, ReadOnlySpan<byte> packedValue) => Documents.Add(docId);
    }
}
