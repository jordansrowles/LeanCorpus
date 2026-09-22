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

    [Property(DisplayName = "Packed BKD bytes are invariant under input permutation", MaxTest = 200, StartSize = 1, EndSize = 96)]
    public void Writer_IsDeterministicAcrossPermutations(NonEmptyArray<byte> input)
    {
        byte[] seed = input.Get;
        int count = Math.Min(48, seed.Length + 4);
        var values = new List<ModelPoint>(count);
        for (int i = 0; i < count; i++)
        {
            float x = (seed[(i * 2) % seed.Length] - 128) / 8f;
            float y = (seed[(i * 2 + 1) % seed.Length] - 128) / 8f;
            byte[] packed = new byte[8];
            XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
            values.Add(new ModelPoint(i % 13, packed));
        }

        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd-permutation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (int leafSize in new[] { 1, 3, 7 })
            {
                string firstPath = Path.Combine(directory, $"first-{leafSize}.pbkd");
                string secondPath = Path.Combine(directory, $"second-{leafSize}.pbkd");
                using var first = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(leafSize));
                using var second = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(leafSize));
                foreach (var value in values)
                    first.Append(value.Packed, value.DocId);

                var permutation = values.ToArray();
                for (int i = permutation.Length - 1; i > 0; i--)
                {
                    int swap = seed[i % seed.Length] % (i + 1);
                    (permutation[i], permutation[swap]) = (permutation[swap], permutation[i]);
                }
                foreach (var value in permutation)
                    second.Append(value.Packed, value.DocId);

                PackedBkdWriter.Write(firstPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = first });
                PackedBkdWriter.Write(secondPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = second });
                Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Property(DisplayName = "Packed BKD flush and reopen sequences match a live model", MaxTest = 100, StartSize = 1, EndSize = 96)]
    public void Lifecycle_FlushReopenSequenceMatchesModel(NonEmptyArray<byte> input)
    {
        byte[] seed = input.Get;
        var model = new List<ModelPoint>();
        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd-lifecycle-property", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? currentPath = null;
        ModelPoint[] persistedModel = [];
        int nextDocument = 0;
        int flushOrdinal = 0;
        try
        {
            for (int operation = 0; operation < Math.Min(24, seed.Length + 4); operation++)
            {
                switch (seed[operation % seed.Length] & 3)
                {
                    case 0:
                        AddPoint(nextDocument++);
                        break;
                    case 1:
                        int document = nextDocument++;
                        AddPoint(document);
                        AddPoint(document);
                        break;
                    case 2:
                        if (model.Count > 0)
                            FlushAndAssert();
                        break;
                    default:
                        if (currentPath is not null)
                            ReopenAndAssert();
                        break;
                }
            }

            if (model.Count > 0)
                FlushAndAssert();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        void AddPoint(int document)
        {
            int valueIndex = model.Count;
            float x = (seed[(valueIndex * 2) % seed.Length] - 128) / 8f;
            float y = (seed[(valueIndex * 2 + 1) % seed.Length] - 128) / 8f;
            byte[] packed = new byte[8];
            XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
            model.Add(new ModelPoint(document, packed));
        }

        void FlushAndAssert()
        {
            currentPath = Path.Combine(directory, $"state-{flushOrdinal++}.pbkd");
            persistedModel = model.ToArray();
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 3));
            foreach (var value in persistedModel)
                buffer.Append(value.Packed, value.DocId);
            PackedBkdWriter.Write(currentPath!, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = buffer
            });
            ReopenAndAssert();
        }

        void ReopenAndAssert()
        {
            using var reader = PackedBkdReader.Open(currentPath!);
            byte[] queryMinimum = new byte[8];
            byte[] queryMaximum = new byte[8];
            XYEncodingUtils.Encode(-8, queryMinimum.AsSpan(0, 4));
            XYEncodingUtils.Encode(-8, queryMinimum.AsSpan(4, 4));
            XYEncodingUtils.Encode(8, queryMaximum.AsSpan(0, 4));
            XYEncodingUtils.Encode(8, queryMaximum.AsSpan(4, 4));
            var visitor = new ReferenceVisitor(queryMinimum, queryMaximum);
            Assert.True(reader.Intersect("location", visitor));
            var expected = persistedModel
                .Where(value => IsInRange(value.Packed, queryMinimum, queryMaximum))
                .Select(static value => value.DocId)
                .ToHashSet();
            Assert.Equal(expected.Order(), visitor.Documents.Order());
        }
    }

    [Property(DisplayName = "Seven-dimensional Packed BKD spill preserves the reference points", MaxTest = 200, StartSize = 1, EndSize = 96)]
    public void SevenDimensional_SpillMatchesMemory(NonEmptyArray<byte> input)
    {
        byte[] seed = input.Get;
        int count = Math.Min(24, seed.Length + 3);
        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd-seven", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var memory = new PackedBkdFieldBuffer(PackedBkdConfig.SevenDimensional(3));
            using var spill = new PackedBkdFieldBuffer(PackedBkdConfig.SevenDimensional(3));
            for (int point = 0; point < count; point++)
            {
                byte[] packed = new byte[28];
                for (int dimension = 0; dimension < 7; dimension++)
                {
                    float value = (seed[(point + dimension) % seed.Length] - 128) / 4f + dimension;
                    XYEncodingUtils.Encode(value, packed.AsSpan(dimension * 4, 4));
                }
                int docId = point % 7;
                memory.Append(packed, docId);
                spill.Append(packed, docId);
            }

            string memoryPath = Path.Combine(directory, "memory.pbkd");
            string spillPath = Path.Combine(directory, "spill.pbkd");
            PackedBkdWriter.Write(memoryPath, new Dictionary<string, PackedBkdFieldBuffer> { ["shape"] = memory });
            PackedBkdWriter.Write(spillPath, new Dictionary<string, PackedBkdFieldBuffer> { ["shape"] = spill },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true));
            Assert.Equal(File.ReadAllBytes(memoryPath), File.ReadAllBytes(spillPath));

            using var reader = PackedBkdReader.Open(spillPath);
            var visitor = new VisitAllVisitor();
            Assert.True(reader.Intersect("shape", visitor));
            Assert.Equal(Enumerable.Range(0, 7).Where(doc => Enumerable.Range(0, count).Any(point => point % 7 == doc)), visitor.Documents.Distinct().Order());
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

    private sealed class VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Documents.Add(docId);
    }
}
