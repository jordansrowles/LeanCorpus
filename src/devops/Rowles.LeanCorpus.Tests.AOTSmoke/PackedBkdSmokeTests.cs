using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
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
