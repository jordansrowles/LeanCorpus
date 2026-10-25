using System.Text.Json.Nodes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SpatialPointFieldMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "leancorpus_spatial_merge_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_root);

    [Fact]
    public void ForceMerge_RejectsModernGeoAndXYBeforeCreatingDestinationFiles()
        => AssertForceMergeRejected(legacyGeo: false);

    [Fact]
    public void ForceMerge_RejectsLegacyGeoAndModernXYBeforeCreatingDestinationFiles()
        => AssertForceMergeRejected(legacyGeo: true);

    [Fact]
    public void ForceMerge_PreservesLegacyAndModernGeoWithGeoMetadataAndQuerySortValues()
    {
        string path = Path.Combine(_root, nameof(ForceMerge_PreservesLegacyAndModernGeoWithGeoMetadataAndQuerySortValues));
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddLegacyGeo(writer, 0, 0);
            AddGeo(writer, 0, 1);
            writer.Commit();
            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        SegmentReader segment = Assert.Single(searcher.GetSegmentReaders());
        Assert.Equal(SpatialFieldKind.GeoPoint, Assert.Single(segment.Info.SpatialFields).Kind);
        Assert.Equal(2, searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 2), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new GeoDistanceQuery("location", 0, 0, 1), 10,
            TestContext.Current.CancellationToken).TotalHits);

        var geoSort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs sorted = searcher.Search(new MatchAllDocsQuery(), 10, geoSort);
        Assert.Equal(new[] { 0, 1 }, sorted.ScoreDocs.Select(static hit => hit.DocId));
        Assert.All(sorted.ScoreDocs, hit =>
            Assert.False(searcher.CaptureSortValues(hit, [geoSort])[0].IsMissing));
    }

    [Theory]
    [InlineData(SpatialFieldKind.GeoPoint)]
    [InlineData(SpatialFieldKind.XYPoint)]
    public void ForceMerge_RetainsCompatibleModernPointKind(SpatialFieldKind kind)
    {
        string path = Path.Combine(_root, $"same-kind-{kind}");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            if (kind == SpatialFieldKind.GeoPoint)
            {
                AddGeo(writer, 0, 0);
                AddGeo(writer, 0, 1);
            }
            else
            {
                AddXY(writer, 0, 0);
                AddXY(writer, 1, 0);
            }

            writer.Commit();
            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory);
        SegmentReader segment = Assert.Single(searcher.GetSegmentReaders());
        Assert.Equal(kind, Assert.Single(segment.Info.SpatialFields).Kind);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact]
    public void Merge_RejectsUnclassifiableMetadataLessPackedPointWithModernGeo()
    {
        string path = Path.Combine(_root, nameof(Merge_RejectsUnclassifiableMetadataLessPackedPointWithModernGeo));
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddGeo(writer, 0, 0);
            var xyDocument = new LeanDocument();
            xyDocument.Add(new XYPointField("location", 0, 0));
            xyDocument.Add(new StringField("location_lat", "not-a-number"));
            xyDocument.Add(new StringField("location_lon", "not-a-number"));
            writer.AddDocument(xyDocument);
            writer.Commit();
        }

        string xyMetadataPath = Directory.GetFiles(path, "seg_*.seg")
            .Single(file => SegmentInfo.ReadFrom(file).SpatialFields.Any(static field => field.Kind == SpatialFieldKind.XYPoint));
        JsonObject segmentJson = JsonNode.Parse(File.ReadAllText(xyMetadataPath))!.AsObject();
        segmentJson["SpatialFields"] = new JsonArray();
        File.WriteAllText(xyMetadataPath, segmentJson.ToJsonString());

        List<SegmentInfo> segments = ReadSegments(path);
        string[] filesBeforeMerge = GetIndexFiles(path);
        int nextOrdinal = segments.Max(static segment => SegmentOrdinal(segment.SegmentId)) + 1;
        int originalNextOrdinal = nextOrdinal;
        using var targetDirectory = new MMapDirectory(path);
        var merger = new SegmentMerger(targetDirectory, mergeThreshold: 100, softDeleteRetentionSeconds: 0);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => merger.MergeAll(segments, ref nextOrdinal));

        Assert.Contains("unclassifiable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalNextOrdinal, nextOrdinal);
        Assert.Equal(filesBeforeMerge, GetIndexFiles(path));
        AssertOriginalSegmentsRemainReadable(targetDirectory, segments, expectedGeoHits: 1, expectedXYHits: 0);
    }

    private void AssertForceMergeRejected(bool legacyGeo)
    {
        string caseName = legacyGeo ? "legacy-geo-xy" : "modern-geo-xy";
        string path = Path.Combine(_root, caseName);
        string[] filesBeforeMerge;
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            if (legacyGeo)
                AddLegacyGeo(writer, 0, 0);
            else
                AddGeo(writer, 0, 0);
            AddXY(writer, 0, 0);
            writer.Commit();
            filesBeforeMerge = GetIndexFiles(path);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => writer.ForceMerge(1));
            Assert.Contains("location", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(filesBeforeMerge, GetIndexFiles(path));
        }

        Assert.Equal(filesBeforeMerge, GetIndexFiles(path));
        using var searchDirectory = new MMapDirectory(path);
        List<SegmentInfo> segments = ReadSegments(path);
        AssertOriginalSegmentsRemainReadable(
            searchDirectory,
            segments,
            expectedGeoHits: 1,
            expectedXYHits: 1);
    }

    private static void AssertOriginalSegmentsRemainReadable(
        MMapDirectory directory,
        IReadOnlyList<SegmentInfo> segments,
        int expectedGeoHits,
        int expectedXYHits)
    {
        using var searcher = new IndexSearcher(directory, segments);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(expectedGeoHits,
            searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10,
                TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(expectedXYHits,
            searcher.Search(new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10,
                TestContext.Current.CancellationToken).TotalHits);
    }

    private static IndexWriterConfig CreateConfig()
        => new()
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 100,
            MergePolicy = NoMergePolicy.Instance
        };

    private static void AddGeo(IndexWriter writer, double latitude, double longitude)
    {
        var document = new LeanDocument();
        document.Add(new GeoPointField("location", latitude, longitude));
        writer.AddDocument(document);
    }

    private static void AddXY(IndexWriter writer, float x, float y)
    {
        var document = new LeanDocument();
        document.Add(new XYPointField("location", x, y));
        writer.AddDocument(document);
    }

    private static void AddLegacyGeo(IndexWriter writer, double latitude, double longitude)
    {
        var document = new LeanDocument();
        document.Add(new NumericField("location_lat", latitude, stored: false));
        document.Add(new NumericField("location_lon", longitude, stored: false));
        writer.AddDocument(document);
    }

    private static List<SegmentInfo> ReadSegments(string path)
        => Directory.GetFiles(path, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
            .ToList();

    private static string[] GetIndexFiles(string path)
        => Directory.GetFiles(path)
            .Select(Path.GetFileName)
            .Where(static name => name is not null
                && (name.StartsWith("seg_", StringComparison.Ordinal)
                    || name.StartsWith("segments_", StringComparison.Ordinal)))
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static int SegmentOrdinal(string segmentId)
        => int.Parse(segmentId.AsSpan("seg_".Length));
}
