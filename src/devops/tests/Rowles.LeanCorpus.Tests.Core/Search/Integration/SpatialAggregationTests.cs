using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search.Aggregations;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialAggregationTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(Path.GetTempPath(), "spatial_agg_" + Guid.NewGuid().ToString("N"));

    public SpatialAggregationTests() => Directory.CreateDirectory(_directoryPath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directoryPath))
                Directory.Delete(_directoryPath, recursive: true);
        }
        catch
        {
            // Mapped files can remain open briefly on Windows.
        }
    }

    [Fact(DisplayName = "Heterogeneous aggregations preserve request order and exact multi-valued Geo points")]
    public void SearchWithAggregations_CombinesNumericAndSpatialRequestsInRequestOrder()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "dateline"));
            document.Add(new NumericField("votes", 3));
            document.Add(new GeoPointField("location", 0, 179));
            document.Add(new GeoPointField("location", 0, -179));
            writer.AddDocument(document);

            var missing = new LeanDocument();
            missing.Add(new StringField("tag", "dateline"));
            missing.Add(new NumericField("votes", 5));
            writer.AddDocument(missing);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        ISearchAggregationRequest[] requests =
        [
            new AggregationRequest("votes", "votes"),
            new GeoDistanceAggregationRequest(
                "near", "location", new GeoPoint(0, 180),
                [new GeoDistanceRange(null, 120_000), new GeoDistanceRange(100_000, 130_000)]),
            new GeoCentroidAggregationRequest("centre", "location"),
            new GeoBoundsAggregationRequest("wrapped", "location"),
        ];

        var (hits, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "dateline"), 10, requests, TestContext.Current.CancellationToken);

        Assert.Equal(2, hits.TotalHits);
        Assert.Collection(results,
            numeric => Assert.Equal(8, Assert.IsType<AggregationResult>(numeric).Sum),
            distance =>
            {
                GeoDistanceAggregationResult result = Assert.IsType<GeoDistanceAggregationResult>(distance);
                Assert.Equal("near", result.Name);
                Assert.Equal(2, result.Buckets.Count);
                Assert.Equal(1, result.Buckets[0].DocumentCount);
                Assert.Equal(1, result.Buckets[1].DocumentCount);
            },
            centroid =>
            {
                GeoCentroidAggregationResult result = Assert.IsType<GeoCentroidAggregationResult>(centroid);
                Assert.Equal(SpatialDimension.Point, result.Dimension);
                Assert.Equal(1, result.ContributingDocumentCount);
                Assert.NotNull(result.Centroid);
                Assert.InRange(Math.Abs(Math.Abs(result.Centroid.Value.Longitude) - 180), 0, 0.01);
            },
            bounds =>
            {
                GeoBoundsAggregationResult result = Assert.IsType<GeoBoundsAggregationResult>(bounds);
                Assert.Equal(1, result.ContributingDocumentCount);
                Assert.NotNull(result.Bounds);
                Assert.True(result.Bounds.Value.CrossesDateline);
                Assert.InRange(360 - (result.Bounds.Value.West - result.Bounds.Value.East), 1.9, 2.1);
            });

        GeoBoundsAggregationRequest nonWrapping = new("plain", "location", wrapLongitude: false);
        var (_, plainResults) = searcher.SearchWithAggregations(
            new TermQuery("tag", "dateline"), 0, new ISearchAggregationRequest[] { nonWrapping },
            TestContext.Current.CancellationToken);
        GeoRectangle plain = Assert.IsType<GeoBoundsAggregationResult>(Assert.Single(plainResults)).Bounds!.Value;
        Assert.False(plain.CrossesDateline);
        Assert.Equal(-179, plain.West, precision: 4);
        Assert.Equal(179, plain.East, precision: 4);
    }

    [Fact(DisplayName = "Legacy Geo numeric fields remain a spatial aggregation fallback")]
    public void SearchWithAggregations_UsesLegacyLatitudeAndLongitudeFields()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "legacy"));
            document.Add(new NumericField("place_lat", 51.5));
            document.Add(new NumericField("place_lon", -0.12));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        ISearchAggregationRequest[] requests =
        [
            new GeoDistanceAggregationRequest("distance", "place", new GeoPoint(51.5, -0.12), [new GeoDistanceRange(0, 1)]),
            new GeoCentroidAggregationRequest("centroid", "place"),
            new GeoBoundsAggregationRequest("bounds", "place"),
        ];

        var (_, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "legacy"), 1, requests, TestContext.Current.CancellationToken);

        GeoDistanceAggregationResult distance = Assert.IsType<GeoDistanceAggregationResult>(results[0]);
        Assert.Equal(1, distance.Buckets[0].DocumentCount);
        GeoCentroidAggregationResult centroid = Assert.IsType<GeoCentroidAggregationResult>(results[1]);
        Assert.NotNull(centroid.Centroid);
        Assert.Equal(51.5, centroid.Centroid.Value.Latitude);
        Assert.InRange(Math.Abs(centroid.Centroid.Value.Longitude - -0.12), 0, 1e-12);
        Assert.Equal(SpatialDimension.Point, centroid.Dimension);
        GeoBoundsAggregationResult bounds = Assert.IsType<GeoBoundsAggregationResult>(results[2]);
        Assert.NotNull(bounds.Bounds);
        Assert.Equal(51.5, bounds.Bounds.Value.South);
        Assert.Equal(51.5, bounds.Bounds.Value.North);
        Assert.InRange(Math.Abs(bounds.Bounds.Value.West - -0.12), 0, 1e-12);
        Assert.InRange(Math.Abs(bounds.Bounds.Value.East - -0.12), 0, 1e-12);
    }

    [Fact(DisplayName = "Geo point aggregations retain legacy and packed values across deletes and force merge")]
    public void GeoPointAggregations_MixLegacyAndPackedSegmentsAfterForceMerge()
    {
        using (var writer = new IndexWriter(
            new MMapDirectory(_directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            var legacy = new LeanDocument();
            legacy.Add(new StringField("id", "legacy"));
            legacy.Add(new StringField("kind", "place"));
            legacy.Add(new NumericField("location_lat", 0));
            legacy.Add(new NumericField("location_lon", 179));
            writer.AddDocument(legacy);
            writer.Commit();

            var packed = new LeanDocument();
            packed.Add(new StringField("id", "packed"));
            packed.Add(new StringField("kind", "place"));
            packed.Add(new GeoPointField("location", 0, -179));
            writer.AddDocument(packed);
            writer.Commit();

            var deleted = new LeanDocument();
            deleted.Add(new StringField("id", "deleted"));
            deleted.Add(new StringField("kind", "place"));
            deleted.Add(new GeoPointField("location", 0, 0));
            writer.AddDocument(deleted);
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("id", "deleted"));
            writer.Commit();
            writer.ForceMerge(1);
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        ISearchAggregationRequest[] requests =
        [
            new GeoDistanceAggregationRequest(
                "distance", "location", new GeoPoint(0, 180), [new GeoDistanceRange(0, 120_000)]),
            new GeoCentroidAggregationRequest("centre", "location"),
            new GeoBoundsAggregationRequest("bounds", "location"),
        ];
        var (hits, results) = searcher.SearchWithAggregations(
            new TermQuery("kind", "place"), 0, requests, TestContext.Current.CancellationToken);

        Assert.Equal(2, hits.TotalHits);
        GeoDistanceAggregationResult distance = Assert.IsType<GeoDistanceAggregationResult>(results[0]);
        Assert.Equal(2, distance.Buckets[0].DocumentCount);
        GeoCentroidAggregationResult centroid = Assert.IsType<GeoCentroidAggregationResult>(results[1]);
        Assert.Equal(SpatialDimension.Point, centroid.Dimension);
        Assert.Equal(2, centroid.ContributingDocumentCount);
        Assert.NotNull(centroid.Centroid);
        Assert.InRange(Math.Abs(Math.Abs(centroid.Centroid.Value.Longitude) - 180), 0, 1e-3);
        GeoBoundsAggregationResult bounds = Assert.IsType<GeoBoundsAggregationResult>(results[2]);
        Assert.Equal(2, bounds.ContributingDocumentCount);
        Assert.True(bounds.Bounds!.Value.CrossesDateline);
    }

    [Fact(DisplayName = "Shape metadata aggregations require complete Shape DocValues before query traversal")]
    public void ShapeAggregations_RequireCompleteCoverageButShapeRelationsRemainAvailable()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var enabled = new LeanDocument();
            enabled.Add(new StringField("tag", "enabled"));
            enabled.Add(new LatLonShapeField("shape", new GeoRectangle(-1, 179, 1, -179)));
            writer.AddDocument(enabled);
            writer.Commit();
        }

        using (var enabledSearcher = new IndexSearcher(new MMapDirectory(_directoryPath)))
        {
            var (hits, results) = enabledSearcher.SearchWithAggregations(
                new TermQuery("tag", "enabled"),
                10,
                new ISearchAggregationRequest[]
                {
                    new GeoCentroidAggregationRequest("centre", "shape"),
                    new GeoBoundsAggregationRequest("bounds", "shape"),
                },
                TestContext.Current.CancellationToken);
            Assert.Equal(1, hits.TotalHits);
            GeoCentroidAggregationResult centroid = Assert.IsType<GeoCentroidAggregationResult>(results[0]);
            Assert.Equal(SpatialDimension.Area, centroid.Dimension);
            Assert.Equal(1, centroid.ContributingDocumentCount);
            GeoBoundsAggregationResult bounds = Assert.IsType<GeoBoundsAggregationResult>(results[1]);
            Assert.True(bounds.Bounds!.Value.CrossesDateline);
        }

        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var disabled = new LeanDocument();
            disabled.Add(new StringField("tag", "disabled"));
            disabled.Add(new LatLonShapeField("shape", new GeoPoint(0, 0), storeDocValues: false));
            writer.AddDocument(disabled);
            writer.Commit();
        }

        using var mixedSearcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            mixedSearcher.SearchWithAggregations(
                new TermQuery("tag", "no-match"),
                10,
                new ISearchAggregationRequest[] { new GeoCentroidAggregationRequest("centre", "shape") },
                TestContext.Current.CancellationToken));
        Assert.Contains("complete Geo Shape DocValues coverage", exception.Message, StringComparison.Ordinal);

        TopDocs relationHits = mixedSearcher.Search(
            new GeoShapeQuery("shape", SpatialRelation.Intersects, new GeoPoint(0, 180)),
            10,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, relationHits.TotalHits);
    }

    [Fact(DisplayName = "Shape aggregations treat a fully deleted merged field as empty input")]
    public void ShapeAggregations_FullyDeletedFieldAfterForceMergeReturnsEmptyResults()
    {
        using (var writer = new IndexWriter(
            new MMapDirectory(_directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            var deletedShape = new LeanDocument();
            deletedShape.Add(new StringField("id", "deleted-shape"));
            deletedShape.Add(new LatLonShapeField("area", new GeoRectangle(-1, -2, 3, 4)));
            writer.AddDocument(deletedShape);
            writer.Commit();

            var survivor = new LeanDocument();
            survivor.Add(new StringField("id", "survivor"));
            writer.AddDocument(survivor);
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("id", "deleted-shape"));
            writer.Commit();
            writer.ForceMerge(1);
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        SegmentReader mergedSegment = Assert.Single(searcher.GetSegmentReaders());
        Assert.Contains(mergedSegment.Info.SpatialFields, static field => field.FieldName == "area" && field.Kind == SpatialFieldKind.GeoShape);
        Assert.False(mergedSegment.TryGetPackedBkdFieldMetadata("area", out _));
        Assert.False(mergedSegment.TryGetShapeDocValuesFieldMetadata("area", out _));

        var (hits, results) = searcher.SearchWithAggregations(
            new TermQuery("id", "survivor"),
            10,
            [
                new GeoCentroidAggregationRequest("centre", "area"),
                new GeoBoundsAggregationRequest("bounds", "area"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, hits.TotalHits);
        GeoCentroidAggregationResult centroid = Assert.IsType<GeoCentroidAggregationResult>(results[0]);
        Assert.Null(centroid.Centroid);
        Assert.Null(centroid.Dimension);
        Assert.Equal(0, centroid.ContributingDocumentCount);
        GeoBoundsAggregationResult bounds = Assert.IsType<GeoBoundsAggregationResult>(results[1]);
        Assert.Null(bounds.Bounds);
        Assert.Equal(0, bounds.ContributingDocumentCount);
    }

    [Fact(DisplayName = "Empty typed spatial aggregations return named empty results")]
    public void SearchWithAggregations_NoMatchesReturnsEmptySpatialResults()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "present"));
            document.Add(new GeoPointField("location", 1, 2));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        var (_, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "absent"),
            0,
            new ISearchAggregationRequest[]
            {
                new GeoCentroidAggregationRequest("centre", "location"),
                new GeoBoundsAggregationRequest("bounds", "location"),
            },
            TestContext.Current.CancellationToken);

        Assert.Null(Assert.IsType<GeoCentroidAggregationResult>(results[0]).Centroid);
        Assert.Null(Assert.IsType<GeoCentroidAggregationResult>(results[0]).Dimension);
        Assert.Equal(0, Assert.IsType<GeoCentroidAggregationResult>(results[0]).ContributingDocumentCount);
        Assert.Null(Assert.IsType<GeoBoundsAggregationResult>(results[1]).Bounds);
    }

    [Fact(DisplayName = "Geo distance ranges are half-open and count each multi-valued document once per bucket")]
    public void GeoDistanceAggregation_UsesHalfOpenRangesAndDocumentCounts()
    {
        double halfSine = Math.Sin(Math.PI / 360d);
        double oneDegreeMetres = 6_371_000d * 2d * Math.Atan2(halfSine, Math.Sqrt(1d - (halfSine * halfSine)));
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var multiValued = new LeanDocument();
            multiValued.Add(new StringField("tag", "distance"));
            multiValued.Add(new GeoPointField("place", 0, 0));
            multiValued.Add(new GeoPointField("place", 0, 1));
            writer.AddDocument(multiValued);

            var boundary = new LeanDocument();
            boundary.Add(new StringField("tag", "distance"));
            boundary.Add(new GeoPointField("place", 0, 1));
            writer.AddDocument(boundary);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        var request = new GeoDistanceAggregationRequest(
            "distance",
            "place",
            new GeoPoint(0, 0),
            [
                new GeoDistanceRange(null, oneDegreeMetres),
                new GeoDistanceRange(oneDegreeMetres, oneDegreeMetres + 1d),
                new GeoDistanceRange(oneDegreeMetres + 1d, null),
                new GeoDistanceRange(0d, null),
            ]);

        var (_, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "distance"),
            0,
            new ISearchAggregationRequest[] { request },
            TestContext.Current.CancellationToken);

        GeoDistanceAggregationResult result = Assert.IsType<GeoDistanceAggregationResult>(Assert.Single(results));
        Assert.Equal(new long[] { 1, 2, 0, 2 }, result.Buckets.Select(static bucket => bucket.DocumentCount));
    }

    [Fact(DisplayName = "Geo centroid selects and weights the highest shape dimension")]
    public void GeoCentroidAggregation_WeightsLineAndAreaAndResetsLowerDimensions()
    {
        GeoLineString line = new(
        [
            new GeoPoint(0, 0),
            new GeoPoint(0, 2),
            new GeoPoint(0, 8),
        ]);
        GeoPolygon area = new(
        [
            new GeoPoint(0, 0),
            new GeoPoint(0, 4),
            new GeoPoint(4, 4),
            new GeoPoint(4, 0),
        ],
        [
            [
                new GeoPoint(0.5, 0.5),
                new GeoPoint(1.5, 0.5),
                new GeoPoint(1.5, 1.5),
                new GeoPoint(0.5, 1.5),
            ],
        ]);

        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var point = new LeanDocument();
            point.Add(new StringField("kind", "point"));
            point.Add(new LatLonShapeField("shape", new GeoPoint(0, 0)));
            writer.AddDocument(point);

            var lineDocument = new LeanDocument();
            lineDocument.Add(new StringField("kind", "line"));
            lineDocument.Add(new LatLonShapeField("shape", line));
            writer.AddDocument(lineDocument);

            var areaDocument = new LeanDocument();
            areaDocument.Add(new StringField("kind", "area"));
            areaDocument.Add(new LatLonShapeField("shape", area));
            writer.AddDocument(areaDocument);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        GeoCentroidAggregationResult pointResult = Run("point");
        Assert.Equal(SpatialDimension.Point, pointResult.Dimension);
        Assert.NotNull(pointResult.Centroid);
        Assert.InRange(Math.Abs(pointResult.Centroid.Value.Latitude), 0, 1e-6);
        Assert.InRange(Math.Abs(pointResult.Centroid.Value.Longitude), 0, 1e-6);

        GeoCentroidAggregationResult lineResult = Run("line");
        Assert.Equal(SpatialDimension.Line, lineResult.Dimension);
        Assert.NotNull(lineResult.Centroid);
        Assert.InRange(Math.Abs(lineResult.Centroid.Value.Latitude), 0, 1e-4);
        Assert.InRange(Math.Abs(lineResult.Centroid.Value.Longitude - 4), 0, 1e-3);

        GeoCentroidAggregationResult areaResult = Run("area");
        Assert.Equal(SpatialDimension.Area, areaResult.Dimension);
        Assert.NotNull(areaResult.Centroid);
        double expectedNetAreaCentroid = 31d / 15d;
        Assert.InRange(Math.Abs(areaResult.Centroid.Value.Latitude - expectedNetAreaCentroid), 0, 1e-3);
        Assert.InRange(Math.Abs(areaResult.Centroid.Value.Longitude - expectedNetAreaCentroid), 0, 1e-2);

        var (_, allResults) = searcher.SearchWithAggregations(
            new MatchAllDocsQuery(),
            10,
            new ISearchAggregationRequest[] { new GeoCentroidAggregationRequest("centre", "shape") },
            TestContext.Current.CancellationToken);
        GeoCentroidAggregationResult all = Assert.IsType<GeoCentroidAggregationResult>(Assert.Single(allResults));
        Assert.Equal(SpatialDimension.Area, all.Dimension);
        Assert.Equal(1, all.ContributingDocumentCount);
        Assert.Equal(areaResult.Centroid, all.Centroid);

        GeoCentroidAggregationResult Run(string kind)
        {
            var (_, values) = searcher.SearchWithAggregations(
                new TermQuery("kind", kind),
                10,
                new ISearchAggregationRequest[] { new GeoCentroidAggregationRequest("centre", "shape") },
                TestContext.Current.CancellationToken);
            return Assert.IsType<GeoCentroidAggregationResult>(Assert.Single(values));
        }
    }

    [Fact(DisplayName = "Geo point centroid returns zero longitude for a degenerate circular mean")]
    public void GeoCentroidAggregation_DegenerateLongitudeVectorReturnsZero()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "antipodal"));
            document.Add(new GeoPointField("place", 0, 0));
            document.Add(new GeoPointField("place", 0, 180));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        var (_, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "antipodal"),
            1,
            new ISearchAggregationRequest[] { new GeoCentroidAggregationRequest("centre", "place") },
            TestContext.Current.CancellationToken);
        GeoCentroidAggregationResult result = Assert.IsType<GeoCentroidAggregationResult>(Assert.Single(results));
        Assert.Equal(SpatialDimension.Point, result.Dimension);
        Assert.Equal(1, result.ContributingDocumentCount);
        Assert.Equal(0, result.Centroid!.Value.Longitude);
    }

    [Fact(DisplayName = "Geo bounds resolves equal circular gaps deterministically and includes disconnected shapes")]
    public void GeoBoundsAggregation_UsesExactCircularIntervalUnion()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var tiedPoints = new LeanDocument();
            tiedPoints.Add(new StringField("kind", "tied"));
            tiedPoints.Add(new GeoPointField("place", 0, -120));
            tiedPoints.Add(new GeoPointField("place", 0, 0));
            tiedPoints.Add(new GeoPointField("place", 0, 120));
            writer.AddDocument(tiedPoints);

            var disconnectedShapes = new LeanDocument();
            disconnectedShapes.Add(new StringField("kind", "shape"));
            disconnectedShapes.Add(new LatLonShapeField(
                "shape",
                new GeoGeometryCollection(
                [
                    new GeoRectangle(-1, 170, 1, 175),
                    new GeoRectangle(-2, -178, 2, -175),
                ])));
            writer.AddDocument(disconnectedShapes);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        GeoBoundsAggregationResult tied = Run("tied", "place");
        Assert.NotNull(tied.Bounds);
        Assert.Equal(-120, tied.Bounds.Value.West, precision: 3);
        Assert.Equal(120, tied.Bounds.Value.East, precision: 3);
        Assert.False(tied.Bounds.Value.CrossesDateline);

        GeoBoundsAggregationResult shape = Run("shape", "shape");
        Assert.NotNull(shape.Bounds);
        Assert.True(shape.Bounds.Value.CrossesDateline);
        Assert.InRange(Math.Abs(shape.Bounds.Value.West - 170), 0, 1e-3);
        Assert.InRange(Math.Abs(shape.Bounds.Value.East - -175), 0, 1e-3);

        GeoBoundsAggregationResult Run(string kind, string field)
        {
            var (_, values) = searcher.SearchWithAggregations(
                new TermQuery("kind", kind),
                10,
                new ISearchAggregationRequest[] { new GeoBoundsAggregationRequest("bounds", field) },
                TestContext.Current.CancellationToken);
            return Assert.IsType<GeoBoundsAggregationResult>(Assert.Single(values));
        }
    }

    [Fact(DisplayName = "Geo bounds returns full longitude coverage when primitive intervals cover the globe")]
    public void GeoBoundsAggregation_ReturnsFullLongitudeExtentForCompleteCoverage()
    {
        var world = new GeoGeometryCollection(
        [
            new GeoRectangle(-10, -180, 10, -90),
            new GeoRectangle(-10, -90, 10, 0),
            new GeoRectangle(-10, 0, 10, 90),
            new GeoRectangle(-10, 90, 10, 180),
        ]);
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "world"));
            document.Add(new LatLonShapeField("shape", world));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        var (_, results) = searcher.SearchWithAggregations(
            new TermQuery("tag", "world"),
            10,
            new ISearchAggregationRequest[] { new GeoBoundsAggregationRequest("bounds", "shape") },
            TestContext.Current.CancellationToken);
        GeoBoundsAggregationResult result = Assert.IsType<GeoBoundsAggregationResult>(Assert.Single(results));
        Assert.NotNull(result.Bounds);
        Assert.Equal(-180, result.Bounds.Value.West, precision: 3);
        Assert.Equal(180, result.Bounds.Value.East, precision: 3);
        Assert.False(result.Bounds.Value.CrossesDateline);
    }

    [Fact(DisplayName = "Heterogeneous aggregations reject duplicate names and observe cancellation")]
    public void HeterogeneousAggregations_ValidateNamesAndCancellation()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_directoryPath), new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new StringField("tag", "one"));
            document.Add(new NumericField("votes", 1));
            document.Add(new GeoPointField("place", 0, 0));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_directoryPath));
        ISearchAggregationRequest[] duplicateNames =
        [
            new AggregationRequest("same", "votes"),
            new GeoCentroidAggregationRequest("same", "place"),
        ];
        Assert.Throws<ArgumentException>(() => searcher.SearchWithAggregations(
            new MatchAllDocsQuery(), 1, duplicateNames, TestContext.Current.CancellationToken));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => searcher.SearchWithAggregations(
            new MatchAllDocsQuery(),
            1,
            new ISearchAggregationRequest[] { new GeoCentroidAggregationRequest("centre", "place") },
            cancellation.Token));
    }
}
