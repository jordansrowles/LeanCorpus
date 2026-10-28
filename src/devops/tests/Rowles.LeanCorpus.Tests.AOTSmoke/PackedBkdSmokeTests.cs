using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Search.XY;
using Xunit;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public sealed class PackedBkdSmokeTests
{
    [Fact]
    public void SpatialPointQueriesAndNearestSortsRunUnderNativeAot()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"lc-aot-spatial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        try
        {
            using (var directory = new MMapDirectory(directoryPath))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 2 }))
            {
                for (int i = 0; i < 16; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new GeoPointField("location", 0, i));
                    document.Add(new XYPointField("position", i, 0));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(directoryPath);
            using var searcher = new IndexSearcher(searchDirectory);
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            var query = new MatchAllDocsQuery();
            TopDocs geoNearest = searcher.Search(query, 4, SortField.GeoDistance("location", new GeoPoint(0, 0)));
            TopDocs xyNearest = searcher.Search(query, 4, SortField.XYDistance("position", new XYPoint(0, 0)));
            TopDocs geoRadius = searcher.Search(new GeoDistanceQuery("location", 0, 0, 2_000_000), 20, cancellationToken);
            TopDocs xyRadius = searcher.Search(new XYDistanceQuery("position", new XYPoint(0, 0), 2), 20, cancellationToken);
            TopDocs geoBounds = searcher.Search(new GeoBoundingBoxQuery("location", -1, 1, 0, 3), 20, cancellationToken);
            TopDocs xyBounds = searcher.Search(new XYBoundingBoxQuery("position", new XYRectangle(0, -1, 3, 1)), 20, cancellationToken);

            Assert.Equal([0, 1, 2, 3], geoNearest.ScoreDocs.Select(static hit => hit.DocId));
            Assert.Equal([0, 1, 2, 3], xyNearest.ScoreDocs.Select(static hit => hit.DocId));
            Assert.Equal(16, geoRadius.TotalHits);
            Assert.Equal(3, xyRadius.TotalHits);
            Assert.Equal(4, geoBounds.TotalHits);
            Assert.Equal(4, xyBounds.TotalHits);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public void PackedBkdRoundTripsUnderNativeAot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc-aot-packed-bkd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "points.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            Append(buffer, 0, 0, 0);
            Append(buffer, 1, 1, 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var reader = PackedBkdReader.Open(path);
            var visitor = new VisitAllVisitor();
            Assert.True(reader.Intersect("location", ref visitor));
            Assert.Equal([0, 1], visitor.Documents.Order());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeoAndXYShapeIndexingAndRelationsRunUnderNativeAot()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"lc-aot-shapes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        try
        {
            var xyCollection = new XYGeometryCollection(
            [
                new XYRectangle(0, 0, 4, 4),
                new XYRectangle(6, 0, 10, 4),
            ]);
            var geoPolygon = new GeoPolygon(
            [
                new GeoPoint(-10, 170), new GeoPoint(-10, -170),
                new GeoPoint(10, -170), new GeoPoint(10, 170),
            ],
            [[
                new GeoPoint(-2, 175), new GeoPoint(-2, -175),
                new GeoPoint(2, -175), new GeoPoint(2, 175),
            ]]);

            using (var directory = new MMapDirectory(directoryPath))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                BKDMaxLeafSize = 2,
                UseCompoundFile = true,
            }))
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", "shape"));
                document.Add(new LatLonShapeField("geo", geoPolygon));
                document.Add(new XYShapeField("xy", xyCollection));
                writer.AddDocument(document);
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(directoryPath);
            using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            TopDocs geoPoint = searcher.Search(
                new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoPoint(5, -180)),
                10,
                cancellationToken);
            TopDocs geoCircle = searcher.Search(
                new GeoShapeQuery("geo", SpatialRelation.Intersects, new GeoCircle(5, 179.5, 100_000)),
                10,
                cancellationToken);
            TopDocs xyCollectionContains = searcher.Search(
                new XYShapeQuery("xy", SpatialRelation.Contains, xyCollection),
                10,
                cancellationToken);

            Assert.Equal(1, geoPoint.TotalHits);
            Assert.Equal(1, geoCircle.TotalHits);
            Assert.Equal(1, xyCollectionContains.TotalHits);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    [Fact]
    public void SpatialUtilitiesShapeMetadataAndAggregationsRunUnderNativeAot()
    {
        const string geoPolygonText = "POLYGON ((170 -10, -170 -10, -170 10, 170 10, 170 -10))";
        const string xyPolygonText = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))";
        GeoPolygon geoPolygon = Assert.IsType<GeoPolygon>(WktReader.ParseGeo(geoPolygonText));
        XYPolygon xyPolygon = Assert.IsType<XYPolygon>(WktReader.ParseXY(xyPolygonText));
        Assert.IsType<GeoPolygon>(WktReader.ParseGeo(WktWriter.Write(geoPolygon)));
        Assert.IsType<XYPolygon>(WktReader.ParseXY(WktWriter.Write(xyPolygon)));

        var geoLine = Assert.IsType<GeoLineString>(WktReader.ParseGeo("LINESTRING (179 0, 180 0.01, -179 0)"));
        var xyLine = Assert.IsType<XYLineString>(WktReader.ParseXY("LINESTRING (0 0, 1 0.1, 2 0)"));
        Assert.True(GeoSimplifier.Simplify(geoLine, 1_500).Points.Count >= 2);
        Assert.True(XYSimplifier.Simplify(xyLine, 0.2f).Points.Count >= 2);

        string directoryPath = Path.Combine(Path.GetTempPath(), $"lc-aot-spatial-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        try
        {
            using (var directory = new MMapDirectory(directoryPath))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                BKDMaxLeafSize = 2,
                UseCompoundFile = true,
            }))
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", "spatial"));
                document.Add(new NumericField("rank", 7));
                document.Add(new GeoPointField("location", 0, 179.9));
                document.Add(new XYPointField("position", 1, 1));
                document.Add(new LatLonShapeField("geo", geoPolygon));
                document.Add(new XYShapeField("xy", xyPolygon));
                writer.AddDocument(document);
                writer.Commit();
            }

            using var searchDirectory = new MMapDirectory(directoryPath);
            using var searcher = new IndexSearcher(searchDirectory, new IndexSearcherConfig { ParallelSearch = false });
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            var matchAll = new MatchAllDocsQuery();

            Assert.Equal(1, searcher.Search(
                matchAll, 1, SortField.GeoDistance("location", new GeoPoint(0, 180))).TotalHits);
            Assert.Equal(1, searcher.Search(
                matchAll, 1, SortField.XYDistance("position", new XYPoint(0, 0))).TotalHits);

            foreach (SpatialRelation relation in Enum.GetValues<SpatialRelation>())
            {
                long expectedHits = relation == SpatialRelation.Disjoint ? 0 : 1;
                TopDocs geoRelation = searcher.Search(new GeoShapeQuery("geo", relation, geoPolygon), 1, cancellationToken);
                TopDocs xyRelation = searcher.Search(new XYShapeQuery("xy", relation, xyPolygon), 1, cancellationToken);
                Assert.Equal(expectedHits, geoRelation.TotalHits);
                Assert.Equal(expectedHits, xyRelation.TotalHits);
            }

            ISearchAggregationRequest[] requests =
            [
                new AggregationRequest("rank", "rank"),
                new GeoDistanceAggregationRequest("distance", "location", new GeoPoint(0, 180),
                    [new GeoDistanceRange(0, 20_000)]),
                new GeoCentroidAggregationRequest("centroid", "geo"),
                new GeoBoundsAggregationRequest("bounds", "geo"),
            ];
            var (results, aggregations) = searcher.SearchWithAggregations(
                matchAll, 1, requests, cancellationToken);
            Assert.Equal(1, results.TotalHits);
            Assert.Collection(
                aggregations,
                numeric => Assert.Equal("rank", numeric.Name),
                distance => Assert.Equal("distance", distance.Name),
                centroid => Assert.Equal("centroid", centroid.Name),
                bounds => Assert.Equal("bounds", bounds.Name));
            Assert.Equal(1, Assert.IsType<AggregationResult>(aggregations[0]).Count);
            Assert.Equal(1, Assert.IsType<GeoDistanceAggregationResult>(aggregations[1]).Buckets[0].DocumentCount);
            Assert.Equal(1, Assert.IsType<GeoCentroidAggregationResult>(aggregations[2]).ContributingDocumentCount);
            Assert.Equal(1, Assert.IsType<GeoBoundsAggregationResult>(aggregations[3]).ContributingDocumentCount);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void Append(PackedBkdFieldBuffer buffer, float x, float y, int document)
    {
        byte[] packed = new byte[8];
        XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
        XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
        buffer.Append(packed, document);
    }

    private struct VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; }

        public VisitAllVisitor()
        {
            Documents = [];
        }

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Documents.Add(docId);
    }
}
