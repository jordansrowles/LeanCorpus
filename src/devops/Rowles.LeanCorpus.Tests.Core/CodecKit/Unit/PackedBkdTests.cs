using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdTests
{
    [Fact(DisplayName = "Packed BKD writes deterministic multidimensional fields")]
    public void WriterReader_RoundTripsAndIsDeterministic()
    {
        string directory = CreateDirectory();
        try
        {
            var first = CreateBuffer(reverse: false);
            var second = CreateBuffer(reverse: true);
            string firstPath = Path.Combine(directory, "first.pbkd");
            string secondPath = Path.Combine(directory, "second.pbkd");
            try
            {
                PackedBkdWriter.Write(firstPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = first });
                PackedBkdWriter.Write(secondPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = second });
                Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

                using var reader = PackedBkdReader.Open(firstPath);
                var metadata = reader.GetFieldMetadata("location");
                Assert.Equal(10, metadata.PointCount);
                Assert.Equal(10, metadata.DocumentCount);
                Assert.Equal(2, metadata.Config.Dimensions);
                Assert.True(metadata.LeafCount > 1);

                var visitor = new RangeVisitor(new XYPoint(2, 2), new XYPoint(7, 7));
                Assert.True(reader.Intersect("location", visitor));
                Assert.Equal(Enumerable.Range(2, 6), visitor.Documents.Order());
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD spill files are cleaned after success and failure")]
    public void Writer_SpillPathIsCleaned()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "spill.pbkd");
            using var buffer = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD in-memory and spill builds are byte-identical")]
    public void Writer_SpillBuildMatchesInMemoryBuild()
    {
        string directory = CreateDirectory();
        try
        {
            string memoryPath = Path.Combine(directory, "memory.pbkd");
            string spillPath = Path.Combine(directory, "spill.pbkd");
            using var memory = CreateBuffer(reverse: false);
            using var spill = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(memoryPath, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = memory
            });
            PackedBkdWriter.Write(spillPath, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = spill
            }, new PackedBkdBuildOptions(1024, directory, ForceSpill: true));

            Assert.Equal(File.ReadAllBytes(memoryPath), File.ReadAllBytes(spillPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD validates malformed field metadata")]
    public void Reader_RejectsCorruptFieldData()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt.pbkd");
            using (var buffer = CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] bytes = File.ReadAllBytes(path);
            int footerOffset = bytes.Length - 16;
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(footerOffset + 8, sizeof(long)), long.MaxValue);
            File.WriteAllBytes(path, bytes);

            Assert.Throws<CodecFileException>(() => PackedBkdReader.Open(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD writes the v1 footer and relative leaf offsets")]
    public void Writer_WritesV1FooterAndRelativeLeafOffsets()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "layout.pbkd");
            using var buffer = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = buffer
            });

            using var input = new IndexInput(path);
            using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            session.ValidateChecksum();
            using var body = session.OpenBodyInput();

            body.Seek(body.Length - 16);
            Assert.Equal(unchecked((int)PackedBkdWriter.FooterMagic), body.ReadInt32());
            Assert.Equal(1, body.ReadInt32());
            long directoryOffset = body.ReadInt64();

            body.Seek(directoryOffset);
            Assert.Equal(1, body.ReadInt32());
            Assert.Equal("location", body.ReadLengthPrefixedString());
            long sectionOffset = body.ReadInt64();
            long sectionLength = body.ReadInt64();
            Assert.Equal(0, sectionOffset);

            body.Seek(sectionOffset);
            Assert.Equal(unchecked((int)0x3146_4250), body.ReadInt32());
            int dimensions = body.ReadByte();
            int indexedDimensions = body.ReadByte();
            int bytesPerDimension = body.ReadByte();
            Assert.Equal(2, dimensions);
            Assert.Equal(2, indexedDimensions);
            Assert.Equal(4, bytesPerDimension);
            Assert.Equal(0, body.ReadByte());
            int maxPointsPerLeaf = body.ReadInt32();
            int leafCount = body.ReadInt32();
            _ = body.ReadInt64();
            _ = body.ReadInt32();
            int splitCount = body.ReadInt32();
            body.Seek(body.Position + indexedDimensions * bytesPerDimension * 2L + splitCount * (1L + bytesPerDimension));
            long leafDataOffset = body.Position + (leafCount + 1L) * sizeof(long);
            long firstLeafOffset = body.ReadInt64();
            body.Seek(body.Position + leafCount * sizeof(long) - sizeof(long));
            long finalLeafOffset = body.ReadInt64();

            Assert.Equal(2, maxPointsPerLeaf);
            Assert.Equal(leafCount - 1, splitCount);
            Assert.Equal(0, firstLeafOffset);
            Assert.Equal(sectionLength - leafDataOffset, finalLeafOffset);
            Assert.True(finalLeafOffset > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD reads the required seven-dimensional configuration")]
    public void Reader_HandlesSevenDimensionsAndInsideVisits()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "seven.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.SevenDimensional(maxPointsPerLeaf: 2));
            for (int docId = 0; docId < 3; docId++)
            {
                Span<byte> packed = stackalloc byte[28];
                for (int dimension = 0; dimension < 7; dimension++)
                    XYEncodingUtils.Encode(docId + dimension, packed.Slice(dimension * 4, 4));
                buffer.Append(packed, docId);
            }

            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["shape"] = buffer });
            using var reader = PackedBkdReader.Open(path);
            var metadata = reader.GetFieldMetadata("shape");
            Assert.Equal(7, metadata.Config.Dimensions);
            Assert.Equal(4, metadata.Config.IndexedDimensions);
            var visitor = new VisitAllVisitor();
            Assert.True(reader.Intersect("shape", visitor));
            Assert.Equal([0, 1, 2], visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD survives detached flush and compound reopen")]
    public void FlushAndCompoundReader_RoundTripPackedField()
    {
        string directoryPath = CreateDirectory();
        var config = new IndexWriterConfig
        {
            DurableCommits = false,
            UseCompoundFile = true,
            MergePolicy = NoMergePolicy.Instance
        };
        var dwpt = new DocumentsWriterPerThread(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            config);
        var packedConfig = PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2);
        const int documentCount = 5;
        for (int documentId = 0; documentId < documentCount; documentId++)
        {
            dwpt.AddDocument(new LeanDocument());
            Span<byte> packed = stackalloc byte[8];
            XYEncodingUtils.Encode(documentId, packed[..4]);
            XYEncodingUtils.Encode(documentId, packed[4..]);
            dwpt.AddPackedBkdValue("location", packedConfig, packed, documentId);
        }

        DwptFlushSnapshot snapshot;
        lock (dwpt)
            snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);

        try
        {
            SegmentInfo info = SegmentFlusher.FlushFromSnapshot(
                snapshot,
                config,
                directoryPath,
                ordinal: 0,
                commitGeneration: 0,
                seqStart: 0,
                seqEnd: documentCount);

            Assert.True(info.IsCompoundFile);
            Assert.False(File.Exists(Path.Combine(directoryPath, "seg_0.pbkd")));

            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            var packedReader = reader.PackedBkd;
            Assert.NotNull(packedReader);
            var visitor = new RangeVisitor(new XYPoint(1, 1), new XYPoint(3, 3));
            Assert.True(packedReader.Intersect("location", visitor));
            Assert.Equal([1, 2, 3], visitor.Documents.Distinct().Order());
        }
        finally
        {
            snapshot.Dispose();
            dwpt.Dispose();
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static PackedBkdFieldBuffer CreateBuffer(bool reverse)
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
        IEnumerable<int> ids = reverse ? Enumerable.Range(0, 10).Reverse() : Enumerable.Range(0, 10);
        foreach (int id in ids)
        {
            Span<byte> packed = stackalloc byte[8];
            XYEncodingUtils.Encode(id, packed[..4]);
            XYEncodingUtils.Encode(id, packed[4..]);
            buffer.Append(packed, id);
        }
        return buffer;
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RangeVisitor(XYPoint minimum, XYPoint maximum) : IPackedBkdIntersectVisitor
    {
        private readonly byte[] _minimum = Pack(minimum);
        private readonly byte[] _maximum = Pack(maximum);
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        {
            if (maximum.SequenceCompareTo(_minimum) < 0 || minimum.SequenceCompareTo(_maximum) > 0)
                return PackedBkdCellRelation.Outside;
            return minimum.SequenceCompareTo(_minimum) >= 0 && maximum.SequenceCompareTo(_maximum) <= 0
                ? PackedBkdCellRelation.Inside
                : PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (packedValue.SequenceCompareTo(_minimum) >= 0 && packedValue.SequenceCompareTo(_maximum) <= 0)
                Documents.Add(docId);
        }

        private static byte[] Pack(XYPoint point)
        {
            byte[] result = new byte[8];
            XYEncodingUtils.Encode(point.X, result.AsSpan(0, 4));
            XYEncodingUtils.Encode(point.Y, result.AsSpan(4, 4));
            return result;
        }
    }

    private sealed class VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);
        public void Visit(int docId, ReadOnlySpan<byte> packedValue) => Documents.Add(docId);
    }
}
