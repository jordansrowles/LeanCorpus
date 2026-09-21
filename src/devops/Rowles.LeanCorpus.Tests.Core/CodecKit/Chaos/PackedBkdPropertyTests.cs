using FsCheck;
using FsCheck.Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Chaos)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdPropertyTests
{
    [Property(DisplayName = "Packed BKD intersections match a brute-force model", MaxTest = 200, StartSize = 1, EndSize = 96)]
    public void Intersect_MatchesReferenceModel(NonEmptyArray<byte> input)
    {
        byte[] seed = input.Get;
        var values = new List<ModelPoint>(Math.Min(48, seed.Length + 4));
        for (int i = 0; i < values.Capacity; i++)
        {
            float x = (seed[(i * 2) % seed.Length] - 128) / 8f;
            float y = (seed[(i * 2 + 1) % seed.Length] - 128) / 8f;
            int docId = (i * 3 + seed[i % seed.Length]) % 13;
            byte[] packed = new byte[8];
            XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
            values.Add(new ModelPoint(docId, packed));
        }

        float minimumX = (seed[0] - 128) / 8f;
        float maximumX = minimumX + seed[^1] / 16f;
        float minimumY = (seed[seed.Length / 2] - 128) / 8f;
        float maximumY = minimumY + seed[(seed.Length / 2 + 1) % seed.Length] / 16f;
        byte[] queryMinimum = new byte[8];
        byte[] queryMaximum = new byte[8];
        XYEncodingUtils.Encode(minimumX, queryMinimum.AsSpan(0, 4));
        XYEncodingUtils.Encode(minimumY, queryMinimum.AsSpan(4, 4));
        XYEncodingUtils.Encode(maximumX, queryMaximum.AsSpan(0, 4));
        XYEncodingUtils.Encode(maximumY, queryMaximum.AsSpan(4, 4));

        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd-property", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "points.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 3));
            for (int i = values.Count - 1; i >= 0; i--)
                buffer.Append(values[i].Packed, values[i].DocId);

            string memoryPath = Path.Combine(directory, "memory.pbkd");
            using var memoryBuffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 3));
            for (int i = values.Count - 1; i >= 0; i--)
                memoryBuffer.Append(values[i].Packed, values[i].DocId);
            PackedBkdWriter.Write(memoryPath, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = memoryBuffer
            });
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = buffer
            }, new PackedBkdBuildOptions(1024, directory, ForceSpill: true));
            Assert.Equal(File.ReadAllBytes(memoryPath), File.ReadAllBytes(path));

            using var reader = PackedBkdReader.Open(path);
            var visitor = new ReferenceVisitor(queryMinimum, queryMaximum);
            Assert.True(reader.Intersect("location", visitor));

            var expected = values
                .Where(value => IsInRange(value.Packed, queryMinimum, queryMaximum))
                .Select(static value => value.DocId)
                .ToHashSet();
            Assert.Equal(expected.Order(), visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool IsInRange(ReadOnlySpan<byte> value, ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        => value.Slice(0, 4).SequenceCompareTo(minimum.Slice(0, 4)) >= 0
            && value.Slice(0, 4).SequenceCompareTo(maximum.Slice(0, 4)) <= 0
            && value.Slice(4, 4).SequenceCompareTo(minimum.Slice(4, 4)) >= 0
            && value.Slice(4, 4).SequenceCompareTo(maximum.Slice(4, 4)) <= 0;

    private readonly record struct ModelPoint(int DocId, byte[] Packed);

    private sealed class ReferenceVisitor(byte[] minimum, byte[] maximum) : IPackedBkdIntersectVisitor
    {
        internal HashSet<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> cellMinimum, ReadOnlySpan<byte> cellMaximum)
        {
            if (cellMaximum.Slice(0, 4).SequenceCompareTo(minimum.AsSpan(0, 4)) < 0
                || cellMinimum.Slice(0, 4).SequenceCompareTo(maximum.AsSpan(0, 4)) > 0
                || cellMaximum.Slice(4, 4).SequenceCompareTo(minimum.AsSpan(4, 4)) < 0
                || cellMinimum.Slice(4, 4).SequenceCompareTo(maximum.AsSpan(4, 4)) > 0)
                return PackedBkdCellRelation.Outside;

            return cellMinimum.Slice(0, 4).SequenceCompareTo(minimum.AsSpan(0, 4)) >= 0
                && cellMaximum.Slice(0, 4).SequenceCompareTo(maximum.AsSpan(0, 4)) <= 0
                && cellMinimum.Slice(4, 4).SequenceCompareTo(minimum.AsSpan(4, 4)) >= 0
                && cellMaximum.Slice(4, 4).SequenceCompareTo(maximum.AsSpan(4, 4)) <= 0
                ? PackedBkdCellRelation.Inside
                : PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (IsInRange(packedValue, minimum, maximum))
                Documents.Add(docId);
        }
    }
}
