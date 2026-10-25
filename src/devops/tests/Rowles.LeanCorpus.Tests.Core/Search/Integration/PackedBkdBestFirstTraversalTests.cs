using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class PackedBkdBestFirstTraversalTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_best_first_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void TraversalVisitsNearestFrontierFirstAndPrunesStrictlyWorseCells()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
        {
            for (int i = 0; i < 64; i++)
            {
                var document = new LeanDocument();
                document.Add(new XYPointField("position", i, 0));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory);
        var reader = Assert.Single(searcher.GetSegmentReaders());
        var visitor = new FirstPointVisitor();
        Assert.True(reader.TraversePackedBkdBestFirst("position", ref visitor, out PackedBkdTraversalStats stats));

        Assert.True(visitor.FirstPointSeen);
        Assert.True(stats.CellsPruned > 0);
        Assert.True(stats.DocumentsVisited < reader.MaxDoc);
    }

    private struct FirstPointVisitor : IPackedBkdBestFirstVisitor
    {
        private double _worstCandidateDistance;

        internal bool FirstPointSeen { get; private set; }

        public double WorstCandidateDistance => _worstCandidateDistance;

        public bool HasFullCandidateSet => FirstPointSeen;

        public bool ShouldStop => false;

        public int CurrentCandidateCount => FirstPointSeen ? 1 : 0;

        public int PeakCandidateCount => CurrentCandidateCount;

        public long ExactDistanceCalculations => 0;

        public long FilterCandidatesRejected => 0;

        public long CandidateUpdates => CurrentCandidateCount;

        public double GetLowerBoundDistance(uint minimumX, uint maximumX, uint minimumY, uint maximumY)
            => minimumX;

        public void Visit(int documentId, ReadOnlySpan<byte> packedValue)
        {
            if (FirstPointSeen)
                return;
            _worstCandidateDistance = BinaryPrimitives.ReadUInt32BigEndian(packedValue);
            FirstPointSeen = true;
        }
    }
}
