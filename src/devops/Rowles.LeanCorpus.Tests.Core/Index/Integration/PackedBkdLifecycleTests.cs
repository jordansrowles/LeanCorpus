using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Codecs;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PackedBkdLifecycleTests
{
    [Fact(DisplayName = "Packed BKD survives deletion and forced segment merge")]
    public void PackedBkd_MergeDropsDeletedPointsAndPreservesLivePoints()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd-lifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        try
        {
            SegmentInfo first = FlushSegment(directoryPath, 0);
            SegmentInfo second = FlushSegment(directoryPath, 1);

            var liveDocs = new LiveDocs(2);
            liveDocs.Delete(0);
            string deletionPath = Path.Combine(directoryPath, "seg_0.del");
            LiveDocs.Serialise(deletionPath, liveDocs);
            first.LiveDocCount = liveDocs.LiveCount;

            using var directory = new MMapDirectory(directoryPath);
            using (var firstReader = new SegmentReader(directory, first))
                Assert.False(firstReader.IsLive(0));
            int nextOrdinal = 2;
            var merger = new SegmentMerger(directory, mergeThreshold: 2);
            SegmentInfo? merged = merger.MergeAll([first, second], ref nextOrdinal);
            Assert.NotNull(merged);
            Assert.True(File.Exists(Path.Combine(directoryPath, merged!.SegmentId + ".pbkd")));

            using var reader = new SegmentReader(directory, merged);
            var visitor = new VisitAllVisitor();
            Assert.True(reader.IntersectPackedBkd("location", ref visitor));
            Assert.Equal(3, merged.DocCount);
            Assert.Equal([0, 1, 2], visitor.Documents.Order());
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD survives detached flush and compound reopen")]
    public void FlushAndCompoundReader_RoundTripPackedField()
    {
        string directoryPath = PackedBkdTestSupport.CreateDirectory();
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
        var packedConfig = PackedBkdConfig.Point2D(maxPointsPerLeaf: 2);
        const int documentCount = 5;
        byte[] packed = new byte[8];
        for (int documentId = 0; documentId < documentCount; documentId++)
        {
            dwpt.AddDocument(new LeanDocument());
            XYEncodingUtils.Encode(documentId, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(documentId, packed.AsSpan(4, 4));
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
            var visitor = new PackedBkdTestSupport.RangeVisitor(new XYPoint(1, 1), new XYPoint(3, 3));
            Assert.True(reader.IntersectPackedBkd("location", ref visitor));
            Assert.Equal([1, 2, 3], visitor.Documents.Distinct().Order());
        }
        finally
        {
            snapshot.Dispose();
            dwpt.Dispose();
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD corruption aborts merge without dropping an earlier segment")]
    public void CorruptPackedBkd_AbortsMergeAndPreservesEarlierSegment()
    {
        string directoryPath = PackedBkdTestSupport.CreateDirectory();
        try
        {
            SegmentInfo first = FlushSegment(directoryPath, 0);
            SegmentInfo second = FlushSegment(directoryPath, 1);
            string corruptPath = Path.Combine(directoryPath, second.SegmentId + ".pbkd");
            byte[] body = PackedBkdTestSupport.ReadBody(corruptPath);
            int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
            int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
            int leafDataStart = checked(32 + 2 * 4 * 2 + splitCount * (1 + 4) + (leafCount + 1) * sizeof(long));
            body[leafDataStart + 3] = byte.MaxValue;
            PackedBkdTestSupport.RewriteBody(corruptPath, body);

            using var directory = new MMapDirectory(directoryPath);
            var merger = new SegmentMerger(directory, mergeThreshold: 2);
            int nextOrdinal = 2;
            Assert.Throws<InvalidDataException>(() => merger.MergeAll([first, second], ref nextOrdinal));

            Assert.True(File.Exists(Path.Combine(directoryPath, first.SegmentId + ".pbkd")));
            using var reader = new SegmentReader(directory, first);
            var visitor = new PackedBkdTestSupport.VisitAllVisitor();
            Assert.True(reader.IntersectPackedBkd("location", ref visitor));
            Assert.Equal([0, 1], visitor.Documents.Order());
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD merge rejects incompatible indexed dimensions without losing sources")]
    public void Merge_RejectsIncompatibleIndexedDimensions()
    {
        string directoryPath = PackedBkdTestSupport.CreateDirectory();
        try
        {
            SegmentInfo first = FlushSegment(directoryPath, 0);
            SegmentInfo second = FlushSegment(directoryPath, 1, new PackedBkdConfig(2, 1, 4, 2));
            using var directory = new MMapDirectory(directoryPath);
            var merger = new SegmentMerger(directory, mergeThreshold: 2);
            int nextOrdinal = 2;
            Assert.Throws<InvalidDataException>(() => merger.MergeAll([first, second], ref nextOrdinal));

            foreach (SegmentInfo source in new[] { first, second })
            {
                Assert.True(File.Exists(Path.Combine(directoryPath, source.SegmentId + ".pbkd")));
                using var reader = new SegmentReader(directory, source);
                var visitor = new PackedBkdTestSupport.VisitAllVisitor();
                Assert.True(reader.IntersectPackedBkd("location", ref visitor));
                Assert.Equal([0, 1], visitor.Documents.Order());
            }
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD merge rejects plausible values with a stale source checksum")]
    public void Merge_RejectsStaleChecksumWithoutReplacingSources()
    {
        string directoryPath = PackedBkdTestSupport.CreateDirectory();
        try
        {
            var config = new PackedBkdConfig(2, 1, 4, 2);
            SegmentInfo first = FlushSegment(directoryPath, 0, config);
            SegmentInfo second = FlushSegment(directoryPath, 1, config);
            string path = Path.Combine(directoryPath, second.SegmentId + ".pbkd");
            byte[] bytes = File.ReadAllBytes(path);
            int bodyStart = 16 + bytes[5];
            int bodyLength = bytes.Length - bodyStart - 16;
            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(bodyStart + bodyLength - sizeof(long), sizeof(long)));
            bytes[checked(bodyStart + (int)directoryOffset - 1)] ^= 1;
            File.WriteAllBytes(path, bytes);

            using var directory = new MMapDirectory(directoryPath);
            using (var sourceReader = new SegmentReader(directory, second))
            {
                var visitor = new PackedBkdTestSupport.VisitAllVisitor();
                Assert.True(sourceReader.IntersectPackedBkd("location", ref visitor));
                Assert.Equal([0, 1], visitor.Documents.Order());
            }

            var merger = new SegmentMerger(directory, mergeThreshold: 2);
            int nextOrdinal = 2;
            Assert.Throws<InvalidDataException>(() => merger.MergeAll([first, second], ref nextOrdinal));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            using var firstReader = new SegmentReader(directory, first);
            var firstVisitor = new PackedBkdTestSupport.VisitAllVisitor();
            Assert.True(firstReader.IntersectPackedBkd("location", ref firstVisitor));
            Assert.Equal([0, 1], firstVisitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD reader disposal waits for an active traversal")]
    public async Task ReaderDispose_WaitsForActivePackedBkdTraversal()
    {
        string directoryPath = PackedBkdTestSupport.CreateDirectory();
        try
        {
            SegmentInfo info = FlushSegment(directoryPath, 0);
            using (var directory = new MMapDirectory(directoryPath))
            {
                var reader = new SegmentReader(directory, info);
                var gate = new TraversalGate();
                Task? traversal = null;
                Task? dispose = null;
                try
                {
                    var visitor = new BlockingVisitor(gate);
                    traversal = Task.Run(
                        () => RunTraversal(reader, visitor),
                        TestContext.Current.CancellationToken);

                    await gate.Entered.Task.WaitAsync(
                        TimeSpan.FromSeconds(5),
                        TestContext.Current.CancellationToken);
                    dispose = Task.Run(reader.Dispose, TestContext.Current.CancellationToken);
                    await Task.Delay(25, TestContext.Current.CancellationToken);
                    Assert.False(dispose.IsCompleted);
                }
                finally
                {
                    gate.Release.TrySetResult(true);
                    if (traversal is not null)
                        await traversal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                    if (dispose is not null)
                        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                    else
                        reader.Dispose();
                }
            }
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static SegmentInfo FlushSegment(string directoryPath, int ordinal, PackedBkdConfig? packedConfig = null)
    {
        var config = new IndexWriterConfig
        {
            DurableCommits = false,
            MergePolicy = NoMergePolicy.Instance,
        };
        var dwpt = new DocumentsWriterPerThread(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            config);
        try
        {
            for (int document = 0; document < 2; document++)
            {
                dwpt.AddDocument(new LeanDocument());
                byte[] packed = new byte[8];
                XYEncodingUtils.Encode(document, packed.AsSpan(0, 4));
                XYEncodingUtils.Encode(document, packed.AsSpan(4, 4));
                dwpt.AddPackedBkdValue("location", packedConfig ?? PackedBkdConfig.Point2D(maxPointsPerLeaf: 2), packed, document);
            }

            DwptFlushSnapshot snapshot;
            lock (dwpt)
                snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);
            try
            {
                return SegmentFlusher.FlushFromSnapshot(
                    snapshot,
                    config,
                    directoryPath,
                    ordinal,
                    commitGeneration: 0,
                    seqStart: ordinal * 2L,
                    seqEnd: ordinal * 2L + 2L);
            }
            finally
            {
                snapshot.Dispose();
            }
        }
        finally
        {
            dwpt.Dispose();
        }
    }

    private static bool RunTraversal(SegmentReader reader, BlockingVisitor visitor)
        => reader.IntersectPackedBkd("location", ref visitor);

    private sealed class TraversalGate
    {
        internal TaskCompletionSource<bool> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private struct BlockingVisitor : IPackedBkdIntersectVisitor
    {
        private readonly TraversalGate _gate;

        internal BlockingVisitor(TraversalGate gate) => _gate = gate;

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        {
            _gate.Entered.TrySetResult(true);
            _gate.Release.Task.GetAwaiter().GetResult();
            return PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId)
        {
        }

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
        }
    }

    private struct VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; }

        public VisitAllVisitor()
        {
            Documents = [];
        }

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Crosses;

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Documents.Add(docId);
    }
}
