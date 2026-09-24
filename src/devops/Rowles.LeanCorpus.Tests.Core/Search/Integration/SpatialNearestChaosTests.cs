using System.Buffers.Binary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Codecs;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Core.Search.Sorting;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialNearestChaosTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_nearest_chaos_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact(DisplayName = "Nearest search rejects a corrupt packed leaf and another committed index remains readable")]
    public void CorruptPackedLeafFailsNearestSearchWithoutAffectingAnotherIndex()
    {
        string brokenPath = Path.Combine(_path, "broken");
        string intactPath = Path.Combine(_path, "intact");
        BuildIndex(brokenPath);
        BuildIndex(intactPath);
        CorruptFirstLeaf(System.IO.Directory.GetFiles(brokenPath, "*.pbkd").Single());

        using (var directory = new MMapDirectory(brokenPath))
        using (var searcher = new IndexSearcher(directory))
        {
            var sort = SortField.XYDistance("position", new XYPoint(0, 0));
            Assert.Throws<InvalidDataException>(
                () => searcher.Search(new MatchAllDocsQuery(), 1, sort));
        }

        using var intactDirectory = new MMapDirectory(intactPath);
        using var intactSearcher = new IndexSearcher(intactDirectory);
        TopDocs nearest = intactSearcher.Search(
            new MatchAllDocsQuery(),
            3,
            SortField.XYDistance("position", new XYPoint(0, 0)));
        Assert.Equal([0, 1, 2], nearest.ScoreDocs.Select(static hit => hit.DocId));
    }

    [Fact(DisplayName = "Cancelled nearest search leaves the next search complete")]
    public void CancelledSearchDoesNotLeakNearestCandidateState()
    {
        string path = Path.Combine(_path, "cancelled");
        BuildIndex(path);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var query = new MatchAllDocsQuery();
        var sort = SortField.XYDistance("position", new XYPoint(0, 0));
        Assert.Throws<OperationCanceledException>(
            () => searcher.Search(query, 3, sort, new SearchOptions { CancellationToken = cancellation.Token }));

        TopDocs nearest = searcher.Search(query, 3, sort);
        TopDocs exhaustive = searcher.Search(query, 3, [sort, SortField.DocId]);
        Assert.Equal(exhaustive.ScoreDocs.Select(static hit => hit.DocId), nearest.ScoreDocs.Select(static hit => hit.DocId));
    }

    [Fact(DisplayName = "Interrupted packed point flush leaves the prior committed spatial index readable")]
    public void InterruptedSpatialCommitFlushPreservesEarlierCommit()
    {
        string path = Path.Combine(_path, "interrupted-commit");
        using (var directory = new MMapDirectory(path))
        {
            var config = new IndexWriterConfig { BKDMaxLeafSize = 2 };
            using var writer = new IndexWriter(directory, config);
            for (int point = 1; point <= 4; point++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", $"committed-{point}"));
                document.Add(new XYPointField("position", point, 0));
                writer.AddDocument(document);
            }
            writer.Commit();

            config.PhysicalFlushStarted = () => throw new IOException("injected packed point flush interruption");
            var interrupted = new LeanDocument();
            interrupted.Add(new StringField("id", "uncommitted-nearest"));
            interrupted.Add(new XYPointField("position", 0, 0));
            writer.AddDocument(interrupted);

            Assert.Throws<IOException>(writer.Commit);
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        TopDocs nearest = searcher.Search(
            new MatchAllDocsQuery(),
            4,
            SortField.XYDistance("position", new XYPoint(0, 0)));
        Assert.Equal(4, nearest.TotalHits);
        Assert.DoesNotContain("uncommitted-nearest", nearest.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]));
        Assert.Equal("committed-1", searcher.GetStoredFields(nearest.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact(DisplayName = "Legacy Geo fallback remains usable when a separate packed segment is corrupt")]
    public void LegacyFallbackRemainsUsableBesideCorruptPackedSegment()
    {
        string path = Path.Combine(_path, "mixed-corrupt");
        SegmentInfo legacySegment;
        SegmentInfo packedSegment;
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 2,
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            var legacy = new LeanDocument();
            legacy.Add(new StringField("id", "legacy"));
            legacy.Add(new NumericField("location_lat", 0, stored: false));
            legacy.Add(new NumericField("location_lon", 0.01, stored: false));
            writer.AddDocument(legacy);
            writer.Commit();
            legacySegment = Assert.Single(writer.GetNrtSegments());
        }

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 2,
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            var packed = new LeanDocument();
            packed.Add(new StringField("id", "packed"));
            packed.Add(new GeoPointField("location", 0, 0));
            writer.AddDocument(packed);
            writer.Commit();
            packedSegment = Assert.Single(
                writer.GetNrtSegments(),
                segment => segment.SegmentId != legacySegment.SegmentId);
        }

        using (var directory = new MMapDirectory(path))
        using (var legacySearcher = new IndexSearcher(directory, [legacySegment]))
        {
            TopDocs nearest = legacySearcher.Search(
                new MatchAllDocsQuery(),
                1,
                SortField.GeoDistance("location", new GeoPoint(0, 0)));
            Assert.Equal(1, nearest.TotalHits);
            Assert.Equal("legacy", legacySearcher.GetStoredFields(nearest.ScoreDocs[0].DocId)["id"][0]);
        }

        CorruptFirstLeaf(Path.Combine(path, packedSegment.SegmentId + ".pbkd"));
        using (var directory = new MMapDirectory(path))
        using (var mixedSearcher = new IndexSearcher(directory))
        {
            Assert.Throws<InvalidDataException>(() => mixedSearcher.Search(
                new MatchAllDocsQuery(),
                2,
                SortField.GeoDistance("location", new GeoPoint(0, 0))));
        }

        using var fallbackDirectory = new MMapDirectory(path);
        using var fallbackSearcher = new IndexSearcher(fallbackDirectory, [legacySegment]);
        TopDocs fallbackNearest = fallbackSearcher.Search(
            new MatchAllDocsQuery(),
            1,
            SortField.GeoDistance("location", new GeoPoint(0, 0)));
        Assert.Equal("legacy", fallbackSearcher.GetStoredFields(fallbackNearest.ScoreDocs[0].DocId)["id"][0]);
    }

    private static void BuildIndex(string path)
    {
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 });
        for (int point = 0; point < 32; point++)
        {
            var document = new LeanDocument();
            document.Add(new XYPointField("position", point + 1, 0));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    private static void CorruptFirstLeaf(string path)
    {
        byte[] body = PackedBkdTestSupport.ReadBody(path);
        int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
        int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
        int leafDataStart = checked(32 + 2 * 4 * 2 + splitCount * (1 + 4) + (leafCount + 1) * sizeof(long));
        body[leafDataStart + 3] = byte.MaxValue;
        PackedBkdTestSupport.RewriteBody(path, body);
    }
}
