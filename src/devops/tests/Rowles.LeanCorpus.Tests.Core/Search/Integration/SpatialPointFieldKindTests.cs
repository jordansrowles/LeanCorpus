using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search.Spatial;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialPointFieldKindTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_spatial_kinds_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
        => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void GeoAndXYQueriesDoNotInterpretTheOtherCoordinateEncoding()
    {
        Span<byte> geoBytes = stackalloc byte[8];
        Span<byte> xyBytes = stackalloc byte[8];
        GeoEncodingUtils.WriteLonSortable(0, geoBytes);
        GeoEncodingUtils.WriteLatSortable(0, geoBytes[4..]);
        XYEncodingUtils.Encode(0, xyBytes);
        XYEncodingUtils.Encode(0, xyBytes[4..]);
        for (int offset = 0; offset < 8; offset += 4)
        {
            long geoCode = BinaryPrimitives.ReadUInt32BigEndian(geoBytes.Slice(offset, 4));
            long xyCode = BinaryPrimitives.ReadUInt32BigEndian(xyBytes.Slice(offset, 4));
            Assert.InRange(xyCode - geoCode, -1, 1);
        }

        string geoPath = Path.Combine(_path, "geo");
        using (var directory = new MMapDirectory(geoPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddGeo(writer, "location", 0, 0);
            writer.Commit();
        }

        using (var directory = new MMapDirectory(geoPath))
        using (var searcher = new IndexSearcher(directory))
        {
            Assert.Equal(0, searcher.Search(
                new XYBoundingBoxQuery("location", new XYRectangle(-0.01f, -0.01f, 0.01f, 0.01f)), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, searcher.Search(
                new XYDistanceQuery("location", new XYPoint(0, 0), 1), 10, TestContext.Current.CancellationToken).TotalHits);
        }

        string xyPath = Path.Combine(_path, "xy");
        using (var directory = new MMapDirectory(xyPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddXY(writer, "location", 0, 0);
            writer.Commit();
        }

        using (var directory = new MMapDirectory(xyPath))
        using (var searcher = new IndexSearcher(directory))
        {
            Assert.Equal(0, searcher.Search(
                new GeoBoundingBoxQuery("location", -0.01, 0.01, -0.01, 0.01), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, searcher.Search(
                new GeoDistanceQuery("location", 0, 0, 1), 10, TestContext.Current.CancellationToken).TotalHits);
        }
    }

    [Fact]
    public void ShapeFieldIsNotAdmittedAsAPointEvenWithLegacyNamedNumericFields()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            var document = new LeanDocument();
            document.Add(new LatLonShapeField("location", new GeoPoint(0, 0)));
            document.Add(new NumericField("location_lat", 0, stored: false));
            document.Add(new NumericField("location_lon", 0, stored: false));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.Equal(0, searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new GeoDistanceQuery("location", 0, 0, 100), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(
            new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new XYDistanceQuery("location", new XYPoint(0, 0), 100), 10,
            TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact]
    public void MixedPointKindsAreResolvedPerSegmentAcrossRepeatedKinds()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig(maxBufferedDocs: 1)))
        {
            AddGeo(writer, "location", 0, 0);
            AddXY(writer, "location", 0, 0);
            AddGeo(writer, "location", 10, 10);
            AddXY(writer, "location", 10, 10);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory);
        AssertDocIds([0], searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10,
            TestContext.Current.CancellationToken));
        AssertDocIds([1], searcher.Search(
            new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, searcher.Search(new GeoBoundingBoxQuery("location", 40, 50, 40, 50), 10,
            TestContext.Current.CancellationToken).TotalHits);

        SegmentReader[] readers = searcher.GetSegmentReaders().ToArray();
        Assert.Equal(2, readers.Count(static reader => reader.Info.SpatialFields.Single().Kind == SpatialFieldKind.GeoPoint));
        Assert.Equal(2, readers.Count(static reader => reader.Info.SpatialFields.Single().Kind == SpatialFieldKind.XYPoint));
    }

    [Fact]
    public void LegacyGeoUsesOnlyThePairedNumericCompatibilityFields()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig(maxBufferedDocs: 1)))
        {
            AddLegacyGeo(writer, 0, 0);
            writer.AddDocument(CreateSearchDocument(eligible: false));
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory);
        SegmentReader[] readers = searcher.GetSegmentReaders().ToArray();
        Assert.Equal(2, readers.Length);
        Assert.Equal(
            SpatialPointFieldResolution.LegacyGeo,
            SpatialPointFieldCompatibility.Resolve(Assert.Single(readers, static reader => reader.DocBase == 0), "location"));
        Assert.Equal(
            SpatialPointFieldResolution.None,
            SpatialPointFieldCompatibility.Resolve(Assert.Single(readers, static reader => reader.DocBase == 1), "location"));
        Assert.Equal(1, searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new GeoDistanceQuery("location", 0, 0, 1), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(
            new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new XYDistanceQuery("location", new XYPoint(0, 0), 1), 10,
            TestContext.Current.CancellationToken).TotalHits);

        var geoSort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs geoSorted = searcher.Search(new MatchAllDocsQuery(), 10, geoSort);
        Assert.Equal(new[] { 0, 1 }, geoSorted.ScoreDocs.Select(static hit => hit.DocId));
        Assert.False(searcher.CaptureSortValues(geoSorted.ScoreDocs[0], [geoSort])[0].IsMissing);
        Assert.True(searcher.CaptureSortValues(geoSorted.ScoreDocs[1], [geoSort])[0].IsMissing);
        Assert.All(searcher.Search(
            new MatchAllDocsQuery(), 10, SortField.XYDistance("location", new XYPoint(0, 0))).ScoreDocs,
            hit => Assert.True(searcher.CaptureSortValues(
                hit, [SortField.XYDistance("location", new XYPoint(0, 0))])[0].IsMissing));
    }

    [Fact]
    public void LegacyGeoRequiresBothCoordinateFieldsToBeNumeric()
    {
        string textFieldsPath = Path.Combine(_path, "text-coordinate-fields");
        using (var directory = new MMapDirectory(textFieldsPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("location_lat", "51.5"));
            document.Add(new StringField("location_lon", "-0.1"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using (var directory = new MMapDirectory(textFieldsPath))
        using (var searcher = new IndexSearcher(directory))
        {
            SegmentReader reader = Assert.Single(searcher.GetSegmentReaders());
            Assert.Equal(SpatialPointFieldResolution.None, SpatialPointFieldCompatibility.Resolve(reader, "location"));
            Assert.Equal(0, searcher.Search(
                new GeoBoundingBoxQuery("location", -90, 90, -180, 180), 10,
                TestContext.Current.CancellationToken).TotalHits);
        }

        string mixedFieldsPath = Path.Combine(_path, "mixed-coordinate-fields");
        using (var directory = new MMapDirectory(mixedFieldsPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            var document = new LeanDocument();
            document.Add(new NumericField("location_lat", 51.5, stored: false));
            document.Add(new StringField("location_lon", "-0.1"));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var mixedDirectory = new MMapDirectory(mixedFieldsPath);
        using var mixedSearcher = new IndexSearcher(mixedDirectory);
        SegmentReader mixedReader = Assert.Single(mixedSearcher.GetSegmentReaders());
        Assert.Equal(SpatialPointFieldResolution.None, SpatialPointFieldCompatibility.Resolve(mixedReader, "location"));
        Assert.Equal(0, mixedSearcher.Search(
            new GeoDistanceQuery("location", 51.5, -0.1, 10), 10,
            TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact]
    public void MissingMetadataDoesNotAdmitPackedXYAndExplicitXYOverridesLegacySideFields()
    {
        string unclassifiedPath = Path.Combine(_path, "unclassified");
        using (var directory = new MMapDirectory(unclassifiedPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddXY(writer, "location", 0, 0);
            writer.Commit();
        }

        string metadataPath = Assert.Single(Directory.GetFiles(unclassifiedPath, "seg_*.seg"));
        JsonObject segmentJson = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        segmentJson["SpatialFields"] = new JsonArray();
        File.WriteAllText(metadataPath, segmentJson.ToJsonString());

        using (var directory = new MMapDirectory(unclassifiedPath))
        using (var searcher = new IndexSearcher(directory))
        {
            Assert.Equal(0, searcher.Search(
                new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, searcher.Search(new GeoDistanceQuery("location", 0, 0, 1), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, searcher.Search(
                new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10,
                TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, searcher.Search(new XYDistanceQuery("location", new XYPoint(0, 0), 1), 10,
                TestContext.Current.CancellationToken).TotalHits);
        }

        string explicitPath = Path.Combine(_path, "explicit");
        using (var directory = new MMapDirectory(explicitPath))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            var document = new LeanDocument();
            document.Add(new XYPointField("location", 0, 0));
            document.Add(new NumericField("location_lat", 0, stored: false));
            document.Add(new NumericField("location_lon", 0, stored: false));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var explicitDirectory = new MMapDirectory(explicitPath);
        using var explicitSearcher = new IndexSearcher(explicitDirectory);
        Assert.Equal(
            SpatialPointFieldResolution.XYPoint,
            SpatialPointFieldCompatibility.Resolve(Assert.Single(explicitSearcher.GetSegmentReaders()), "location"));
        Assert.Equal(0, explicitSearcher.Search(new GeoBoundingBoxQuery("location", -1, 1, -1, 1), 10).TotalHits);
        Assert.Equal(0, explicitSearcher.Search(new GeoDistanceQuery("location", 0, 0, 1), 10).TotalHits);
        Assert.Equal(1, explicitSearcher.Search(
            new XYBoundingBoxQuery("location", new XYRectangle(-1, -1, 1, 1)), 10).TotalHits);
        Assert.Equal(1, explicitSearcher.Search(new XYDistanceQuery("location", new XYPoint(0, 0), 1), 10).TotalHits);
    }

    [Fact]
    public void SpatialDistanceSortsKeepWrongKindDocumentsAsMissingAcrossExecutionRoutes()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig(maxBufferedDocs: 1)))
        {
            AddGeo(writer, "location", 0, 1, eligible: false);
            AddXY(writer, "location", 0, 0, eligible: true);
            AddGeo(writer, "location", 0, 2, eligible: true);
            AddXY(writer, "location", 1, 0, eligible: true);
            writer.AddDocument(CreateSearchDocument(eligible: false));
            writer.Commit();
        }

        using var directoryReader = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directoryReader);
        var all = new MatchAllDocsQuery();
        var geoSort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        var xySort = SortField.XYDistance("location", new XYPoint(0, 0));

        TopDocs geoBestFirst = searcher.Search(all, 10, geoSort);
        TopDocs geoFallback = searcher.Search(new TermQuery("scope", "all"), 10, geoSort);
        TopDocs xyBestFirst = searcher.Search(all, 10, xySort);
        TopDocs xyFallback = searcher.Search(new TermQuery("scope", "all"), 10, xySort);
        AssertDocIds([0, 2, 1, 3, 4], geoBestFirst);
        AssertDocIds(geoBestFirst.ScoreDocs.Select(static hit => hit.DocId), geoFallback);
        AssertDocIds([1, 3, 0, 2, 4], xyBestFirst);
        AssertDocIds(xyBestFirst.ScoreDocs.Select(static hit => hit.DocId), xyFallback);
        Assert.Equal(5, geoBestFirst.TotalHits);
        Assert.Equal(5, xyBestFirst.TotalHits);
        Assert.True(searcher.CaptureSortValues(geoBestFirst.ScoreDocs[2], [geoSort])[0].IsMissing);
        Assert.True(searcher.CaptureSortValues(xyBestFirst.ScoreDocs[2], [xySort])[0].IsMissing);

        var filtered = new ConstantScoreQuery(new TermQuery("eligible", "yes"));
        AssertDocIds([2, 1, 3], searcher.Search(filtered, 10, geoSort));
        AssertDocIds([1, 3, 2], searcher.Search(filtered, 10, xySort));
        Assert.Equal(3, searcher.Search(filtered, 10, geoSort).TotalHits);

        TopDocs multiSort = searcher.Search(all, 10, new[] { SortField.Numeric("rank"), geoSort });
        AssertDocIds([0, 2, 1, 3, 4], multiSort);
        SearchAfterValue[] wrongKindBoundary = searcher.CaptureSortValues(geoBestFirst.ScoreDocs[2], [geoSort]);
        Assert.True(wrongKindBoundary[0].IsMissing);
        TopDocs afterScoreDocBoundary = searcher.SearchAfter(geoBestFirst.ScoreDocs[2], all, 2, geoSort);
        AssertDocIds([3, 4], afterScoreDocBoundary);

        SortField[] cursorSorts = [geoSort, SortField.DocId];
        TopDocs cursorFull = searcher.Search(all, 10, cursorSorts);
        SearchAfterValue[] explicitBoundary = searcher.CaptureSortValues(cursorFull.ScoreDocs[2], cursorSorts);
        Assert.True(explicitBoundary[0].IsMissing);
        TopDocs afterBoundary = searcher.SearchAfter(explicitBoundary, all, 2, cursorSorts);
        AssertDocIds([3, 4], afterBoundary);
        Assert.Equal(5, afterBoundary.TotalHits);
    }

    [Fact]
    public void CurrentWritersPersistPointKindsAndRejectOneDwptKindConflictBeforeMutation()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            var conflicting = new LeanDocument();
            conflicting.Add(new StringField("body", "rejected"));
            conflicting.Add(new GeoPointField("location", 0, 0));
            conflicting.Add(new XYPointField("location", 0, 0));
            Assert.Throws<InvalidOperationException>(() => writer.AddDocument(conflicting));

            AddGeo(writer, "location", 0, 0);
            writer.Commit();
        }

        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, CreateConfig()))
        {
            AddXY(writer, "location", 0, 0);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "rejected"), 10,
            TestContext.Current.CancellationToken).TotalHits);
        Assert.Contains(searcher.GetSegmentReaders(), static reader =>
            reader.Info.SpatialFields.Single().Kind == SpatialFieldKind.GeoPoint);
        Assert.Contains(searcher.GetSegmentReaders(), static reader =>
            reader.Info.SpatialFields.Single().Kind == SpatialFieldKind.XYPoint);
    }

    private static IndexWriterConfig CreateConfig(int maxBufferedDocs = 10)
        => new()
        {
            MaxBufferedDocs = maxBufferedDocs,
            MergeThreshold = 100,
            MergePolicy = NoMergePolicy.Instance
        };

    private static void AddGeo(IndexWriter writer, string field, double latitude, double longitude, bool eligible = true)
    {
        var document = CreateSearchDocument(eligible);
        document.Add(new GeoPointField(field, latitude, longitude));
        writer.AddDocument(document);
    }

    private static void AddXY(IndexWriter writer, string field, float x, float y, bool eligible = true)
    {
        var document = CreateSearchDocument(eligible);
        document.Add(new XYPointField(field, x, y));
        writer.AddDocument(document);
    }

    private static void AddLegacyGeo(IndexWriter writer, double latitude, double longitude)
    {
        var document = new LeanDocument();
        document.Add(new NumericField("location_lat", latitude, stored: false));
        document.Add(new NumericField("location_lon", longitude, stored: false));
        writer.AddDocument(document);
    }

    private static LeanDocument CreateSearchDocument(bool eligible)
    {
        var document = new LeanDocument();
        document.Add(new StringField("scope", "all"));
        document.Add(new NumericField("rank", 1));
        if (eligible)
            document.Add(new StringField("eligible", "yes"));
        return document;
    }

    private static void AssertDocIds(IEnumerable<int> expected, TopDocs actual)
        => Assert.Equal(expected, actual.ScoreDocs.Select(static hit => hit.DocId));
}
