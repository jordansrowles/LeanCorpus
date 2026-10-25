using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Search.Geo;
using Spatial4n.Context;
using Spatial4n.Distance;
using Spatial4n.Shapes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStoredField = Rowles.LeanCorpus.Document.Fields.StoredField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStoredField = Lucene.Net.Documents.StoredField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="GeoDistanceQuery"/> and <see cref="GeoBoundingBoxQuery"/> throughput
/// on a corpus of documents with deterministic DataForge geo coordinates.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GeoQueryBenchmarks
{
    private const int TopN = 25;
    private const double DistanceRadiusMetres = 2_000_000d;
    private const double LeanEarthRadiusKilometres = 6_371.0d;

    private static readonly GeoDistanceQuery LeanDistanceQuery = new(
        "location", centreLat: 51.5, centreLon: -0.1, radiusMetres: DistanceRadiusMetres);
    private static readonly GeoBoundingBoxQuery LeanBoundingBoxQuery = new(
        "location", minLat: -30.0, maxLat: 30.0, minLon: -60.0, maxLon: 60.0);
    private static readonly Lucene.Net.Search.MatchAllDocsQuery LuceneMatchAllQuery = new();

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    // Lucene.NET spatial index state
    private string _luceneIndexPath = string.Empty;
    private MMapDirectory? _luceneDirectory;
    private DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private SpatialContext? _spatialContext;
    private RecursivePrefixTreeStrategy? _spatialStrategy;
    private Lucene.Net.Search.Filter? _luceneDistanceFilter;
    private Lucene.Net.Search.Filter? _luceneBoundingBoxFilter;

    [GlobalSetup]
    public void Setup()
    {
        try
        {
            var records = BenchmarkData.GetRecords(DocumentCount);
            BuildLeanIndex(records);
            BuildLuceneIndex(records);
            ValidateResultEquivalence();
            ValidateSpatialEdgeCases();
            ValidateBval01GeoRadiusFixture();
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _leanSearcher = null;
        _leanDirectory?.Dispose();
        _leanDirectory = null;
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneReader = null;
        _luceneDirectory?.Dispose();
        _luceneDirectory = null;
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GeoDistance")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoDistanceQuery()
        => _leanSearcher!.Search(LeanDistanceQuery, TopN).TotalHits;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GeoBoundingBox")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoBoundingBoxQuery()
        => _leanSearcher!.Search(LeanBoundingBoxQuery, TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("GeoDistance")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_GeoDistanceQuery()
        => _luceneSearcher!.Search(LuceneMatchAllQuery, _luceneDistanceFilter!, TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("GeoBoundingBox")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_GeoBoundingBoxQuery()
        => _luceneSearcher!.Search(LuceneMatchAllQuery, _luceneBoundingBoxFilter!, TopN).TotalHits;

    private void BuildLuceneIndex(SearchRecord[] records)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-geo-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new MMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        _spatialContext = SpatialContext.Geo;
        _spatialStrategy = new RecursivePrefixTreeStrategy(
            new GeohashPrefixTree(_spatialContext, maxLevels: 11), "location");
        _spatialStrategy.DistErrPct = 0;

        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));

        for (int i = 0; i < records.Length; i++)
        {
            double lat = records[i].LatitudeE6 / 1_000_000d;
            double lon = records[i].LongitudeE6 / 1_000_000d;
            var pt = _spatialContext.MakePoint(lon, lat);
            var doc = new LuceneDocument();
            doc.Add(new LuceneStoredField("id", records[i].Id));
            foreach (var field in _spatialStrategy.CreateIndexableFields(pt))
                doc.Add(field);
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
        _luceneDistanceFilter = _spatialStrategy.MakeFilter(
            CreateLuceneDistanceArgs(_spatialContext, LeanDistanceQuery));
        _luceneBoundingBoxFilter = _spatialStrategy.MakeFilter(
            CreateLuceneBoundingBoxArgs(_spatialContext, LeanBoundingBoxQuery));
    }

    private void BuildLeanIndex(SearchRecord[] records)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-geo-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });

        for (int i = 0; i < records.Length; i++)
        {
            double lat = records[i].LatitudeE6 / 1_000_000d;
            double lon = records[i].LongitudeE6 / 1_000_000d;
            var doc = new LeanDocument();
            doc.Add(new LeanStoredField("id", records[i].Id));
            doc.Add(new Rowles.LeanCorpus.Document.Fields.GeoPointField("location", lat, lon));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void ValidateResultEquivalence()
    {
        var leanDistance = _leanSearcher!.Search(LeanDistanceQuery, DocumentCount);
        var luceneDistance = _luceneSearcher!.Search(
            LuceneMatchAllQuery, _luceneDistanceFilter!, DocumentCount);
        AssertEquivalent("distance", leanDistance.TotalHits,
            leanDistance.ScoreDocs.Select(hit => GetLeanStoredId(_leanSearcher, hit.DocId)), luceneDistance.TotalHits,
            luceneDistance.ScoreDocs.Select(hit => GetLuceneStoredId(_luceneSearcher, hit.Doc)));

        var leanBoundingBox = _leanSearcher.Search(LeanBoundingBoxQuery, DocumentCount);
        var luceneBoundingBox = _luceneSearcher.Search(
            LuceneMatchAllQuery, _luceneBoundingBoxFilter!, DocumentCount);
        AssertEquivalent("bounding-box", leanBoundingBox.TotalHits,
            leanBoundingBox.ScoreDocs.Select(hit => GetLeanStoredId(_leanSearcher, hit.DocId)), luceneBoundingBox.TotalHits,
            luceneBoundingBox.ScoreDocs.Select(hit => GetLuceneStoredId(_luceneSearcher, hit.Doc)));
    }

    private static void ValidateSpatialEdgeCases()
    {
        GeoParityPoint[] points =
        [
            new("origin", 0.0, 0.0),
            new("dateline-west-near", 0.0, 179.8),
            new("dateline-east-near", 0.0, -179.8),
            new("dateline-west", 0.0, 179.9),
            new("dateline-east", 0.0, -179.9),
            new("radius-inside", 0.18, 0.0),
            new("radius-outside", 0.27, 0.0),
            new("polar-centre", 89.9, 45.0),
            new("polar-opposite", 89.9, -135.0),
            new("north-pole", 90.0, 0.0),
            new("polar-outside", 89.5, 45.0),
            new("box-min-edge", 10.0, 30.0),
            new("box-max-edge", 20.0, 40.0),
            new("box-interior", 15.0, 35.0),
            new("box-south-outside", 9.9, 35.0),
            new("box-east-outside", 15.0, 40.1),
        ];

        string leanPath = BenchmarkHelpers.CreateTempDirectory("lc-benchmark-geo-parity-lean");
        string lucenePath = BenchmarkHelpers.CreateTempDirectory("lc-benchmark-geo-parity-lucene");
        try
        {
            using var leanDirectory = new LeanMMapDirectory(leanPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                leanDirectory, new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig()))
            {
                foreach (GeoParityPoint point in points)
                {
                    var document = new LeanDocument();
                    document.Add(new LeanStoredField("id", point.Id));
                    document.Add(new Rowles.LeanCorpus.Document.Fields.GeoPointField(
                        "location", point.Latitude, point.Longitude));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
            using var leanSearcher = new LeanIndexSearcher(leanDirectory);

            using var luceneDirectory = new MMapDirectory(new System.IO.DirectoryInfo(lucenePath));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using (var writer = new Lucene.Net.Index.IndexWriter(luceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)))
            {
                var context = SpatialContext.Geo;
                var strategy = new RecursivePrefixTreeStrategy(
                    new GeohashPrefixTree(context, maxLevels: 11), "location");
                strategy.DistErrPct = 0;
                foreach (GeoParityPoint point in points)
                {
                    var document = new LuceneDocument();
                    document.Add(new LuceneStoredField("id", point.Id));
                    foreach (var field in strategy.CreateIndexableFields(
                        context.MakePoint(point.Longitude, point.Latitude)))
                        document.Add(field);
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
            using var luceneReader = LuceneDirectoryReader.Open(luceneDirectory);
            var luceneSearcher = new LuceneIndexSearcher(luceneReader);
            var spatialContext = SpatialContext.Geo;
            var spatialStrategy = new RecursivePrefixTreeStrategy(
                new GeohashPrefixTree(spatialContext, maxLevels: 11), "location");

            var distanceAtOrigin = new GeoDistanceQuery("location", 0.0, 0.0, 25_000d);
            var distanceAcrossDateLine = new GeoDistanceQuery("location", 0.0, 180.0, 25_000d);
            var distanceNearPole = new GeoDistanceQuery("location", 89.9, 45.0, 25_000d);
            var ordinaryBounds = new GeoBoundingBoxQuery("location", 10.0, 20.0, 30.0, 40.0);
            var dateLineBounds = new GeoBoundingBoxQuery("location", -1.0, 1.0, 170.0, -170.0);

            Check("distance radius interior/exterior", distanceAtOrigin,
                CreateLuceneDistanceArgs(spatialContext, distanceAtOrigin), ["origin", "radius-inside"]);
            Check("distance across the date line", distanceAcrossDateLine,
                CreateLuceneDistanceArgs(spatialContext, distanceAcrossDateLine),
                ["dateline-west-near", "dateline-east-near", "dateline-west", "dateline-east"]);
            Check("distance near the pole", distanceNearPole,
                CreateLuceneDistanceArgs(spatialContext, distanceNearPole),
                ["polar-centre", "polar-opposite", "north-pole"]);
            Check("inclusive bounding-box edges", ordinaryBounds,
                CreateLuceneBoundingBoxArgs(spatialContext, ordinaryBounds),
                ["box-min-edge", "box-max-edge", "box-interior"]);
            Check("bounding box across the date line", dateLineBounds,
                CreateLuceneBoundingBoxArgs(spatialContext, dateLineBounds),
                ["dateline-west-near", "dateline-east-near", "dateline-west", "dateline-east"]);

            void Check(string name, Rowles.LeanCorpus.Search.Query leanQuery,
                SpatialArgs luceneArgs, string[] expectedIds)
            {
                var leanResults = leanSearcher.Search(leanQuery, points.Length);
                var luceneResults = luceneSearcher.Search(
                    LuceneMatchAllQuery, spatialStrategy.MakeFilter(luceneArgs), points.Length);
                string[] leanIds = leanResults.ScoreDocs
                    .Select(hit => GetLeanStoredId(leanSearcher, hit.DocId)).ToArray();
                string[] luceneIds = luceneResults.ScoreDocs
                    .Select(hit => GetLuceneStoredId(luceneSearcher, hit.Doc)).ToArray();
                AssertExpectedEquivalent(name, leanResults.TotalHits, leanIds,
                    luceneResults.TotalHits, luceneIds, expectedIds);
            }
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(lucenePath);
            BenchmarkHelpers.DeleteDirectory(leanPath);
        }
    }

    private static void ValidateBval01GeoRadiusFixture()
    {
        GeoRadiusFixture fixture = ReadBval01GeoRadiusFixture();
        string leanPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-01-geo-lean");
        string lucenePath = BenchmarkHelpers.CreateTempDirectory("lc-bval-01-geo-lucene");
        try
        {
            using var leanDirectory = new LeanMMapDirectory(leanPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                leanDirectory, new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig()))
            {
                foreach (GeoParityPoint point in fixture.Points)
                {
                    var document = new LeanDocument();
                    document.Add(new LeanStoredField("id", point.Id));
                    document.Add(new Rowles.LeanCorpus.Document.Fields.GeoPointField(
                        "location", point.Latitude, point.Longitude));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
            using var leanSearcher = new LeanIndexSearcher(leanDirectory);

            using var luceneDirectory = new MMapDirectory(new System.IO.DirectoryInfo(lucenePath));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var context = SpatialContext.Geo;
            var strategy = new RecursivePrefixTreeStrategy(
                new GeohashPrefixTree(context, maxLevels: 11), "location")
            {
                DistErrPct = 0,
            };
            using (var writer = new Lucene.Net.Index.IndexWriter(luceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)))
            {
                foreach (GeoParityPoint point in fixture.Points)
                {
                    var document = new LuceneDocument();
                    document.Add(new LuceneStoredField("id", point.Id));
                    foreach (var field in strategy.CreateIndexableFields(
                        context.MakePoint(point.Longitude, point.Latitude)))
                        document.Add(field);
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
            using var luceneReader = LuceneDirectoryReader.Open(luceneDirectory);
            var luceneSearcher = new LuceneIndexSearcher(luceneReader);

            var query = new GeoDistanceQuery("location", fixture.CentreLatitude,
                fixture.CentreLongitude, fixture.RadiusMetres);
            var luceneFilter = strategy.MakeFilter(CreateLuceneDistanceArgs(context, query,
                fixture.EarthMeanRadiusKilometres));
            var leanResults = leanSearcher.Search(query, fixture.Points.Length);
            var luceneResults = luceneSearcher.Search(LuceneMatchAllQuery, luceneFilter, fixture.Points.Length);
            AssertExpectedEquivalent("BVAL-01 50 km radius boundaries", leanResults.TotalHits,
                leanResults.ScoreDocs.Select(hit => GetLeanStoredId(leanSearcher, hit.DocId)),
                luceneResults.TotalHits,
                luceneResults.ScoreDocs.Select(hit => GetLuceneStoredId(luceneSearcher, hit.Doc)),
                fixture.IncludedIds);
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(lucenePath);
            BenchmarkHelpers.DeleteDirectory(leanPath);
        }
    }

    private static void AssertExpectedEquivalent(string name, int leanHitCount,
        IEnumerable<string> leanIds, int luceneHitCount, IEnumerable<string> luceneIds,
        string[] expectedIds)
    {
        string[] leanResults = leanIds.ToArray();
        string[] luceneResults = luceneIds.ToArray();
        if (leanResults.Length != leanHitCount || luceneResults.Length != luceneHitCount
            || leanHitCount != expectedIds.Length || luceneHitCount != expectedIds.Length
            || !leanResults.SequenceEqual(expectedIds, StringComparer.Ordinal)
            || !luceneResults.SequenceEqual(expectedIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Geo {name} equivalence failed: expected [{string.Join(", ", expectedIds)}], " +
                $"LeanCorpus returned [{string.Join(", ", leanResults)}] ({leanHitCount} hits), " +
                $"Lucene.NET returned [{string.Join(", ", luceneResults)}] ({luceneHitCount} hits).");
        }
    }

    private static GeoRadiusFixture ReadBval01GeoRadiusFixture()
    {
        const string fixtureRelativePath = ".config/devops/benchmark-correctness-fixtures/bval-01-geo-radius-boundaries.json";
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            fixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-01-geo-radius-boundaries", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-01 fixture metadata is invalid: {fixtureRelativePath}.");

        JsonElement input = root.GetProperty("input");
        JsonElement centre = input.GetProperty("centre");
        double centreLatitude = centre.GetProperty("latitudeDegrees").GetDouble();
        double centreLongitude = centre.GetProperty("longitudeDegrees").GetDouble();
        double radiusMetres = input.GetProperty("radiusMetres").GetDouble();
        double earthMeanRadiusKilometres = input.GetProperty("earthMeanRadiusKilometres").GetDouble();
        GeoRadiusBoundaryPoint[] boundaryPoints = input.GetProperty("points").EnumerateArray()
            .Select(static point => new GeoRadiusBoundaryPoint(
                point.GetProperty("id").GetString()!,
                point.GetProperty("latitudeDegrees").GetDouble(),
                point.GetProperty("longitudeDegrees").GetDouble(),
                point.GetProperty("distanceKilometres").GetDouble()))
            .ToArray();

        JsonElement expected = root.GetProperty("expected");
        string[] includedIds = ReadFixtureIds(expected.GetProperty("includedIds"));
        string[] excludedIds = ReadFixtureIds(expected.GetProperty("excludedIds"));
        double luceneRadiusKilometres = expected.GetProperty("luceneRadiusKilometres").GetDouble();
        string[] pointIds = boundaryPoints.Select(static point => point.Id).ToArray();
        string[] classifiedIds = includedIds.Concat(excludedIds).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] sortedPointIds = pointIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        GeoRadiusBoundaryPoint[] insidePoints = boundaryPoints.Where(static point =>
            Math.Abs(point.DistanceKilometres - 49.9d) < 1e-9).ToArray();
        GeoRadiusBoundaryPoint[] outsidePoints = boundaryPoints.Where(static point =>
            Math.Abs(point.DistanceKilometres - 50.1d) < 1e-9).ToArray();

        if (radiusMetres != 50_000d || luceneRadiusKilometres != 50d
            || Math.Abs(radiusMetres / 1_000d - luceneRadiusKilometres) > 1e-9
            || earthMeanRadiusKilometres <= 0d
            || !expected.GetProperty("bothEnginesMustReturnSameLogicalIds").GetBoolean()
            || pointIds.Distinct(StringComparer.Ordinal).Count() != pointIds.Length
            || includedIds.Intersect(excludedIds, StringComparer.Ordinal).Any()
            || !classifiedIds.SequenceEqual(sortedPointIds, StringComparer.Ordinal)
            || insidePoints.Length != 1 || outsidePoints.Length != 1
            || insidePoints.Length == 1 && !includedIds.Contains(insidePoints[0].Id, StringComparer.Ordinal)
            || outsidePoints.Length == 1 && !excludedIds.Contains(outsidePoints[0].Id, StringComparer.Ordinal))
            throw new InvalidOperationException($"BVAL-01 fixture does not define the required 49.9 km/50.1 km sentinels: {fixtureRelativePath}.");

        return new GeoRadiusFixture(centreLatitude, centreLongitude, radiusMetres,
            earthMeanRadiusKilometres,
            boundaryPoints.Select(static point => new GeoParityPoint(point.Id, point.Latitude, point.Longitude)).ToArray(),
            includedIds);
    }

    private static string[] ReadFixtureIds(JsonElement value)
        => value.EnumerateArray().Select(static id => id.GetString()!).ToArray();

    private static SpatialArgs CreateLuceneDistanceArgs(SpatialContext context, GeoDistanceQuery query,
        double earthRadiusKilometres = LeanEarthRadiusKilometres)
    {
        double radiusKilometres = query.RadiusMetres / 1_000d;
        var centre = context.MakePoint(query.CentreLon, query.CentreLat);
        var circle = context.MakeCircle(centre,
            DistanceUtils.Dist2Degrees(radiusKilometres, earthRadiusKilometres));
        return new SpatialArgs(SpatialOperation.IsWithin, circle);
    }

    private static SpatialArgs CreateLuceneBoundingBoxArgs(SpatialContext context, GeoBoundingBoxQuery query)
    {
        var rectangle = context.MakeRectangle(query.MinLon, query.MaxLon, query.MinLat, query.MaxLat);
        return new SpatialArgs(SpatialOperation.Intersects, rectangle);
    }

    private static void AssertEquivalent(
        string operation,
        int leanHitCount,
        IEnumerable<string> leanIds,
        int luceneHitCount,
        IEnumerable<string> luceneIds)
    {
        string[] leanResults = leanIds.ToArray();
        string[] luceneResults = luceneIds.ToArray();
        if (leanResults.Length != leanHitCount || luceneResults.Length != luceneHitCount
            || leanHitCount != luceneHitCount
            || !leanResults.SequenceEqual(luceneResults, StringComparer.Ordinal))
        {
            string[] leanOnly = leanResults.Except(luceneResults, StringComparer.Ordinal).ToArray();
            string[] luceneOnly = luceneResults.Except(leanResults, StringComparer.Ordinal).ToArray();
            throw new InvalidOperationException(
                $"Geo {operation} results differ before timing: LeanCorpus returned {leanHitCount} hits " +
                $"and Lucene.NET returned {luceneHitCount} hits; Lean-only IDs [{string.Join(", ", leanOnly)}], " +
                $"Lucene-only IDs [{string.Join(", ", luceneOnly)}]; ordered result IDs must match.");
        }
    }

    private static string GetLeanStoredId(LeanIndexSearcher searcher, int documentId)
    {
        var fields = searcher.GetStoredFields(documentId);
        return fields.TryGetValue("id", out var values) && values.Count == 1
            ? values[0]
            : throw new InvalidOperationException($"LeanCorpus geo result {documentId} has no unique stored fixture ID.");
    }

    private static string GetLuceneStoredId(LuceneIndexSearcher searcher, int documentId)
        => searcher.Doc(documentId).Get("id")
            ?? throw new InvalidOperationException($"Lucene.NET geo result {documentId} has no stored fixture ID.");

    private sealed record GeoRadiusFixture(double CentreLatitude, double CentreLongitude, double RadiusMetres,
        double EarthMeanRadiusKilometres, GeoParityPoint[] Points, string[] IncludedIds);

    private readonly record struct GeoRadiusBoundaryPoint(string Id, double Latitude, double Longitude,
        double DistanceKilometres);

    private readonly record struct GeoParityPoint(string Id, double Latitude, double Longitude);
}
