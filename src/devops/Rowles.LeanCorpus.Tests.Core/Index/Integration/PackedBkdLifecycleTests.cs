using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

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
            var packed = reader.PackedBkd;
            Assert.NotNull(packed);
            var visitor = new VisitAllVisitor();
            Assert.True(packed!.Intersect("location", visitor));
            Assert.Equal(3, merged.DocCount);
            Assert.Equal([0, 1, 2], visitor.Documents.Order());
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static SegmentInfo FlushSegment(string directoryPath, int ordinal)
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
                dwpt.AddPackedBkdValue("location", PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2), packed, document);
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

    private sealed class VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Crosses;

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Documents.Add(docId);
    }
}
