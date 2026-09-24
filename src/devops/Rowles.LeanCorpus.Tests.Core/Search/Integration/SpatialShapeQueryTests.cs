using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialShapeQueryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lc_shape_query_{Guid.NewGuid():N}");

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact(DisplayName = "Shape relations use document values and preserve Contains ordinals")]
    public void XYRelations_RespectDocumentLevelValueSemantics()
    {
        Directory.CreateDirectory(_path);
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            var first = new LeanDocument();
            first.Add(new XYShapeField("area", new XYRectangle(0, 0, 10, 10), boost: 2));
            writer.AddDocument(first);

            var outside = new LeanDocument();
            outside.Add(new XYShapeField("area", new XYRectangle(20, 20, 30, 30)));
            writer.AddDocument(outside);

            var separateValues = new LeanDocument();
            separateValues.Add(new XYShapeField("area", new XYRectangle(0, 0, 4, 4)));
            separateValues.Add(new XYShapeField("area", new XYRectangle(6, 0, 10, 4)));
            writer.AddDocument(separateValues);

            writer.AddDocument(new LeanDocument());
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });

        Assert.Equal(
            new[] { 0 },
            SearchIds(searcher, new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(5, 2))));
        Assert.Equal(
            new[] { 0, 2 },
            SearchIds(searcher, new XYShapeQuery("area", SpatialRelation.Within, new XYRectangle(-1, -1, 11, 11))));
        Assert.Equal(
            new[] { 0 },
            SearchIds(searcher, new XYShapeQuery("area", SpatialRelation.Contains, new XYRectangle(3, 1, 7, 3))));
        Assert.Equal(
            new[] { 1 },
            SearchIds(searcher, new XYShapeQuery("area", SpatialRelation.Disjoint, new XYRectangle(-1, -1, 11, 11))));

        var boosted = new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(1, 1)) { Boost = 3 };
        ScoreDoc hit = Assert.Single(
            searcher.Search(boosted, 4, TestContext.Current.CancellationToken).ScoreDocs,
            static result => result.DocId == 0);
        Assert.Equal(6, hit.Score);
    }

    [Fact(DisplayName = "Shape relation queries treat circles analytically and handle the Geo Date Line")]
    public void CircleAndDatelineRelations_AreHandledByShapeExecution()
    {
        Directory.CreateDirectory(_path);
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            var xy = new LeanDocument();
            xy.Add(new XYShapeField("xy", new XYRectangle(0, 0, 10, 10)));
            writer.AddDocument(xy);

            var geo = new LeanDocument();
            geo.Add(new LatLonShapeField("geo", new GeoRectangle(-1, 179, 1, -179)));
            writer.AddDocument(geo);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });
        Assert.Equal(
            new[] { 0 },
            SearchIds(searcher, new XYShapeQuery("xy", SpatialRelation.Contains, new XYCircle(5, 5, 1))));
        Assert.Equal(
            new[] { 0 },
            SearchIds(searcher, new XYShapeQuery("xy", SpatialRelation.Within, new XYCircle(5, 5, 20))));
        Assert.Equal(
            new[] { 1 },
            SearchIds(searcher, new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoPoint(0, -180))));
    }

    [Fact(DisplayName = "Shape relation matrix includes boundary contact, holes and collection value unions")]
    public void XYRelationMatrix_HandlesBoundariesHolesAndCollectionUnions()
    {
        Directory.CreateDirectory(_path);
        var collection = new XYGeometryCollection(
        [
            new XYRectangle(0, 0, 4, 4),
            new XYRectangle(6, 0, 10, 4),
        ]);
        var hole = new XYPolygon(
            [new XYPoint(0, 0), new XYPoint(10, 0), new XYPoint(10, 10), new XYPoint(0, 10)],
            [[new XYPoint(3, 3), new XYPoint(3, 7), new XYPoint(7, 7), new XYPoint(7, 3)]]);

        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(CreateShapeDocument("large", new XYRectangle(0, 0, 10, 10)));
            writer.AddDocument(CreateShapeDocument("small", new XYRectangle(2, 2, 3, 3)));
            writer.AddDocument(CreateShapeDocument("line", new XYLineString([new XYPoint(0, 0), new XYPoint(10, 0)])));
            writer.AddDocument(CreateShapeDocument("hole", hole));
            writer.AddDocument(CreateShapeDocument("collection", collection));

            var separate = new LeanDocument();
            separate.Add(new StringField("id", "separate"));
            separate.Add(new XYShapeField("area", new XYRectangle(0, 0, 4, 4)));
            separate.Add(new XYShapeField("area", new XYRectangle(6, 0, 10, 4)));
            writer.AddDocument(separate);
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });

        Assert.Equal(
            ["hole", "large"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(10, 5))));
        Assert.Equal(
            ["large"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(5, 5))));
        Assert.Equal(
            ["collection", "hole", "large", "separate", "small"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Contains, new XYPoint(2, 2))));
        Assert.Equal(
            ["collection", "large"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Contains, collection)));
        Assert.Equal(
            ["large"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Contains, new XYRectangle(3, 4, 7, 6))));
        Assert.Equal(
            ["collection", "hole", "line", "separate", "small"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Disjoint, new XYPoint(5, 5))));
        Assert.Equal(
            ["collection", "hole", "large", "line", "separate", "small"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Within, new XYRectangle(-1, -1, 11, 11))));
    }

    [Fact(DisplayName = "Within accepts a shape covered collectively by a union of query circles")]
    public void XYWithin_UsesCollectiveCoverageByQueryCircles()
    {
        Directory.CreateDirectory(_path);
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(CreateShapeDocument("covered", new XYRectangle(0, 0, 10, 10)));
            writer.AddDocument(CreateGeoShapeDocument("geo-covered", new GeoRectangle(-5, 0, 5, 10)));
            writer.AddDocument(CreateGeoShapeDocument(
                "geo-line",
                new GeoLineString([new GeoPoint(0, -1), new GeoPoint(0, 1)])));
            writer.Commit();
        }

        var query = new XYGeometryCollection(
        [
            new XYCircle(2.5f, 5, 5.7f),
            new XYCircle(7.5f, 5, 5.7f),
        ]);
        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });

        Assert.Equal(
            ["covered"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Within, query)));

        var mixedQuery = new XYGeometryCollection(
        [
            new XYRectangle(0, 0, 5, 10),
            new XYCircle(7.5f, 5, 5.7f),
        ]);
        Assert.Equal(
            ["covered"],
            SearchNames(searcher, new XYShapeQuery("area", SpatialRelation.Within, mixedQuery)));

        var geoQuery = new GeoGeometryCollection(
        [
            new GeoCircle(0, 2.5, 650_000),
            new GeoCircle(0, 7.5, 650_000),
        ]);
        Assert.Equal(
            ["geo-covered", "geo-line"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Within, geoQuery)));

        var mixedGeoQuery = new GeoGeometryCollection(
        [
            new GeoRectangle(-5, 0, 5, 5),
            new GeoCircle(0, 7.5, 650_000),
        ]);
        Assert.Equal(
            ["geo-covered"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Within, mixedGeoQuery)));
        Assert.Equal(
            ["geo-line"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Within, new GeoCircle(0, 0, 500_000))));
    }

    [Fact(DisplayName = "Geo circle relations cross the Date Line and include pole distances")]
    public void GeoCircles_HandleDatelineGreenwichAndPoles()
    {
        Directory.CreateDirectory(_path);
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(CreateGeoShapeDocument("seam", new GeoPoint(0, -180)));
            writer.AddDocument(CreateGeoShapeDocument("greenwich", new GeoPoint(0, 0)));
            writer.AddDocument(CreateGeoShapeDocument("pole", new GeoPoint(90, -120)));
            writer.AddDocument(CreateGeoShapeDocument("near-pole", new GeoPoint(89.5, 30)));
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });
        Assert.Equal(
            ["seam"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoCircle(0, 179.5, 100_000))));
        Assert.Equal(
            ["pole"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoCircle(90, 0, 1))));
        Assert.Equal(
            ["greenwich", "near-pole", "pole"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Disjoint, new GeoCircle(0, 179.5, 100_000))));
    }

    [Fact(DisplayName = "Geo shape traversal treats the positive and negative Date Line as one seam")]
    public void GeoShapeQueries_DoNotPruneEquivalentDateLineEnvelopes()
    {
        Directory.CreateDirectory(_path);
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 2,
            MergePolicy = NoMergePolicy.Instance,
        }))
        {
            writer.AddDocument(CreateGeoShapeDocument("east", new GeoPoint(0, 180)));
            writer.AddDocument(CreateGeoShapeDocument("west", new GeoPoint(0, -180)));
            writer.AddDocument(CreateGeoShapeDocument("inner-east", new GeoPoint(0, 90)));
            writer.AddDocument(CreateGeoShapeDocument("inner-west", new GeoPoint(0, -90)));
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });

        Assert.Equal(
            ["east", "west"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoPoint(0, -180))));
        Assert.Equal(
            ["east", "west"],
            SearchNames(searcher, new GeoShapeQuery("geo", SpatialRelation.Within, new GeoRectangle(-1, 170, 1, 180))));
    }

    [Fact(DisplayName = "Generated XY rectangle cases match the independent relation oracle")]
    public void GeneratedRectangles_MatchIndependentRelationOracle()
    {
        Directory.CreateDirectory(_path);
        var random = new Random(0x7032);
        var values = new List<Box>[48];
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            for (int docId = 0; docId < values.Length; docId++)
            {
                int valueCount = docId % 7 == 0 ? 0 : 1 + random.Next(2);
                var document = new LeanDocument();
                values[docId] = [];
                for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                {
                    Box box = RandomBox(random);
                    values[docId].Add(box);
                    document.Add(new XYShapeField("generated", new XYRectangle(box.MinX, box.MinY, box.MaxX, box.MaxY)));
                }
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searchDirectory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });
        for (int queryIndex = 0; queryIndex < 12; queryIndex++)
        {
            Box queryBox = RandomBox(random);
            var query = new XYRectangle(queryBox.MinX, queryBox.MinY, queryBox.MaxX, queryBox.MaxY);
            foreach (SpatialRelation relation in Enum.GetValues<SpatialRelation>())
            {
                int[] expected = Enumerable.Range(0, values.Length)
                    .Where(docId => OracleMatches(values[docId], queryBox, relation))
                    .ToArray();
                int[] actual = SearchIds(searcher, new XYShapeQuery("generated", relation, query));
                Assert.True(
                    expected.SequenceEqual(actual),
                    $"Seed 0x7032, query {queryIndex}, box {queryBox}, relation {relation}: expected [{string.Join(',', expected)}], actual [{string.Join(',', actual)}]. Doc 27 values: [{string.Join(';', values[27])}].");
            }
        }
    }

    private static int[] SearchIds(IndexSearcher searcher, Query query)
        => searcher.Search(query, 1_000, TestContext.Current.CancellationToken).ScoreDocs
            .Select(static hit => hit.DocId)
            .OrderBy(static id => id)
            .ToArray();

    private static string[] SearchNames(IndexSearcher searcher, Query query)
        => searcher.Search(query, 1_000, TestContext.Current.CancellationToken).ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .OrderBy(static id => id)
            .ToArray();

    private static LeanDocument CreateShapeDocument(string id, IXYGeometry geometry)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new XYShapeField("area", geometry));
        return document;
    }

    private static LeanDocument CreateGeoShapeDocument(string id, IGeoGeometry geometry)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new LatLonShapeField("geo", geometry));
        return document;
    }

    private static Box RandomBox(Random random)
    {
        int minX = random.Next(-20, 21);
        int minY = random.Next(-20, 21);
        return new Box(minX, minY, minX + random.Next(0, 13), minY + random.Next(0, 13));
    }

    private static bool OracleMatches(IReadOnlyList<Box> values, Box query, SpatialRelation relation)
    {
        if (values.Count == 0)
            return false;
        return relation switch
        {
            SpatialRelation.Intersects => values.Any(value => OracleIntersects(value, query)),
            SpatialRelation.Within => values.All(value => OracleContains(query, value)),
            SpatialRelation.Contains => values.Any(value => OracleContains(value, query)),
            SpatialRelation.Disjoint => values.All(value => !OracleIntersects(value, query)),
            _ => throw new ArgumentOutOfRangeException(nameof(relation)),
        };
    }

    private static bool OracleIntersects(Box first, Box second)
        => first.MinX <= second.MaxX && first.MaxX >= second.MinX
            && first.MinY <= second.MaxY && first.MaxY >= second.MinY;

    private static bool OracleContains(Box container, Box contained)
        => container.MinX <= contained.MinX && container.MinY <= contained.MinY
            && container.MaxX >= contained.MaxX && container.MaxY >= contained.MaxY;

    private readonly record struct Box(float MinX, float MinY, float MaxX, float MaxY);
}
