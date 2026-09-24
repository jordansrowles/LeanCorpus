using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SpatialShapeFieldTests
{
    [Fact(DisplayName = "Shape fields write stable Packed BKD values and persist spatial kinds")]
    public void ShapeFields_WriteShapePrimitivesAndFieldMetadata()
    {
        string directoryPath = CreateDirectoryPath();
        try
        {
            using (var directory = new MMapDirectory(directoryPath))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                BKDMaxLeafSize = 2,
                MergePolicy = NoMergePolicy.Instance,
            }))
            {
                var document = new LeanDocument();
                document.Add(new LatLonShapeField("geo", new GeoRectangle(-2, -3, 4, 5)));
                document.Add(new LatLonShapeField("geo", new GeoPoint(10, 20)));
                document.Add(new XYShapeField("xy", new XYGeometryCollection(
                [
                    new XYPoint(1, 2),
                    new XYLineString([new XYPoint(3, 4), new XYPoint(5, 6)]),
                ])));
                writer.AddDocument(document);
                writer.Commit();
            }

            string packedPath = Assert.Single(Directory.GetFiles(directoryPath, "*.pbkd"));
            using var packedReader = PackedBkdReader.Open(packedPath);
            Assert.Equal(PackedBkdConfig.Shape7D4Indexed(2), packedReader.GetFieldMetadata("geo").Config);
            Assert.Equal(PackedBkdConfig.Shape7D4Indexed(2), packedReader.GetFieldMetadata("xy").Config);
            Assert.Equal(3, packedReader.GetFieldMetadata("geo").PointCount);
            Assert.Equal(2, packedReader.GetFieldMetadata("xy").PointCount);

            var geoValues = new ShapeValueCollector(SpatialFieldKind.GeoShape);
            Assert.True(packedReader.Intersect("geo", ref geoValues));
            Assert.Collection(
                geoValues.Values.OrderBy(static value => value.Primitive.ValueOrdinal),
                value => Assert.Equal((uint)0, value.Primitive.ValueOrdinal),
                value => Assert.Equal((uint)0, value.Primitive.ValueOrdinal),
                value => Assert.Equal((uint)1, value.Primitive.ValueOrdinal));
            Assert.All(geoValues.Values, static value => Assert.True(
                value.Primitive.Kind is ShapePrimitiveKind.Triangle or ShapePrimitiveKind.Point));

            using var searchDirectory = new MMapDirectory(directoryPath);
            using var searcher = new IndexSearcher(searchDirectory);
            SegmentReader segment = Assert.Single(searcher.GetSegmentReaders());
            Assert.Equal(
                new[] { SpatialFieldKind.GeoShape, SpatialFieldKind.XYShape },
                segment.Info.SpatialFields.OrderBy(static field => field.FieldName).Select(static field => field.Kind));
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(directoryPath);
        }
    }

    [Fact(DisplayName = "Spatial field kind conflicts fail before packed buffers are changed")]
    public void AddDocument_RejectsSpatialKindConflictAndKeepsPriorValues()
    {
        string directoryPath = CreateDirectoryPath();
        try
        {
            using (var directory = new MMapDirectory(directoryPath))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
            {
                var first = new LeanDocument();
                first.Add(new XYPointField("shape", 1, 2));
                writer.AddDocument(first);

                var conflict = new LeanDocument();
                conflict.Add(new LatLonShapeField("shape", new GeoPoint(1, 2)));
                Assert.Throws<InvalidOperationException>(() => writer.AddDocument(conflict));

                var next = new LeanDocument();
                next.Add(new XYPointField("shape", 3, 4));
                writer.AddDocument(next);
                writer.Commit();
            }

            using var packedReader = PackedBkdReader.Open(Assert.Single(Directory.GetFiles(directoryPath, "*.pbkd")));
            Assert.Equal(2, packedReader.GetFieldMetadata("shape").PointCount);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(directoryPath);
        }
    }

    [Fact(DisplayName = "Shape values survive NRT, update, delete, force merge and compound reopen")]
    public void ShapeValues_SurviveWriterLifecycleAndPreserveOrdinals()
    {
        string directoryPath = CreateDirectoryPath();
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            var config = new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                MergePolicy = NoMergePolicy.Instance,
                UseCompoundFile = true,
            };
            using (var writer = new IndexWriter(directory, config))
            {
                writer.AddDocument(CreateDocument("nrt", new XYRectangle(40, 40, 50, 50)));
                using (var nrtSearcher = new IndexSearcher(
                    directory,
                    writer.GetNrtSegments(),
                    new IndexSearcherConfig { ParallelSearch = false }))
                {
                    Assert.Equal(
                        ["nrt"],
                        SearchIds(nrtSearcher, new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(45, 45))));
                }
                writer.Commit();

                var multiValue = CreateDocument(
                    "multi",
                    new XYRectangle(0, 0, 4, 4),
                    new XYRectangle(6, 0, 10, 4));
                writer.AddDocument(multiValue);
                writer.AddDocument(CreateDocument("deleted", new XYRectangle(20, 20, 30, 30)));
                writer.AddDocument(CreateDocument("updated", new XYRectangle(100, 100, 110, 110)));
                writer.Commit();

                writer.UpdateDocument("id", "updated", CreateDocument("updated", new XYRectangle(200, 200, 210, 210)));
                writer.DeleteDocuments(new TermQuery("id", "deleted"));
                writer.Commit();
                using (var beforeMerge = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = false }))
                    Assert.Empty(SearchIds(beforeMerge, new TermQuery("id", "deleted")));
                _ = writer.ForceMerge(1);
                writer.Commit();
            }

            using var reopened = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = false });
            Assert.Empty(SearchIds(reopened, new TermQuery("id", "deleted")));
            Assert.Equal(
                ["multi"],
                SearchIds(reopened, new XYShapeQuery("area", SpatialRelation.Contains, new XYPoint(2, 2))));
            Assert.Equal(
                ["nrt"],
                SearchIds(reopened, new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(45, 45))));
            Assert.Equal(
                ["multi", "nrt", "updated"],
                SearchIds(reopened, new XYShapeQuery("area", SpatialRelation.Within, new XYRectangle(-1, -1, 220, 220))));
            Assert.Empty(SearchIds(
                reopened,
                new XYShapeQuery("area", SpatialRelation.Contains, new XYRectangle(3, 1, 7, 3))));
            Assert.Empty(SearchIds(
                reopened,
                new XYShapeQuery("area", SpatialRelation.Intersects, new XYPoint(105, 105))));

            SegmentInfo segment = Assert.Single(Directory.GetFiles(directoryPath, "seg_*.seg").Select(SegmentInfo.ReadFrom));
            Assert.True(segment.IsCompoundFile);
            Assert.Equal(SpatialFieldKind.XYShape, Assert.Single(segment.SpatialFields).Kind);
            Assert.Contains(Directory.GetFiles(directoryPath), static path => path.EndsWith(".cfs", StringComparison.Ordinal));
            Assert.Empty(Directory.GetFiles(directoryPath, "*.pbkd"));

            using var segmentReader = new SegmentReader(directory, segment);
            var values = new ShapeValueCollector(SpatialFieldKind.XYShape);
            Assert.True(segmentReader.IntersectPackedBkd("area", ref values));
            int multiDocId = Enumerable.Range(0, segment.DocCount)
                .Single(docId => segmentReader.GetStoredFields(docId)["id"][0] == "multi");
            Assert.Equal(
                [0u, 1u],
                values.Values
                    .Where(value => value.DocId == multiDocId)
                    .Select(static value => value.Primitive.ValueOrdinal)
                    .Distinct()
                    .Order());
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(directoryPath);
        }
    }

    [Fact(DisplayName = "Incompatible spatial kinds abort force merge without publishing partial segments")]
    public void ForceMerge_IncompatibleSpatialKindsPreservesCommittedSegments()
    {
        string directoryPath = CreateDirectoryPath();
        try
        {
            using var directory = new MMapDirectory(directoryPath);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                MergePolicy = NoMergePolicy.Instance,
            }))
            {
                var point = new LeanDocument();
                point.Add(new StringField("id", "point"));
                point.Add(new XYPointField("mixed", 1, 2));
                writer.AddDocument(point);

                var shape = new LeanDocument();
                shape.Add(new StringField("id", "shape"));
                shape.Add(new XYShapeField("mixed", new XYRectangle(0, 0, 1, 1)));
                writer.AddDocument(shape);
                writer.Commit();

                string[] segments = writer.GetNrtSegments()
                    .Select(static segment => segment.SegmentId)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(2, segments.Length);
                Assert.Throws<InvalidDataException>(() => writer.ForceMerge(1));
                Assert.Equal(
                    segments,
                    writer.GetNrtSegments()
                        .Select(static segment => segment.SegmentId)
                        .Order(StringComparer.Ordinal)
                        .ToArray());
                Assert.Empty(Directory.GetDirectories(directoryPath, ".merge-*"));
            }

            using var reopened = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = false });
            Assert.Equal(1, reopened.Search(new TermQuery("id", "point"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, reopened.Search(new TermQuery("id", "shape"), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(directoryPath);
        }
    }

    private static LeanDocument CreateDocument(string id, params XYRectangle[] shapes)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        foreach (XYRectangle shape in shapes)
            document.Add(new XYShapeField("area", shape));
        return document;
    }

    private static string[] SearchIds(IndexSearcher searcher, Query query)
        => searcher.Search(query, 100, TestContext.Current.CancellationToken).ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string CreateDirectoryPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-spatial-shapes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private struct ShapeValueCollector : IPackedBkdIntersectVisitor
    {
        private readonly SpatialFieldKind _fieldKind;
        internal List<(int DocId, ShapePrimitive Primitive)> Values;

        internal ShapeValueCollector(SpatialFieldKind fieldKind)
        {
            _fieldKind = fieldKind;
            Values = [];
        }

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Crosses;

        public void Visit(int docId)
            => throw new InvalidDataException("Shape index traversal must return primitive values.");

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
            => Values.Add((docId, ShapePrimitiveCodec.Decode(packedValue, _fieldKind)));
    }
}
