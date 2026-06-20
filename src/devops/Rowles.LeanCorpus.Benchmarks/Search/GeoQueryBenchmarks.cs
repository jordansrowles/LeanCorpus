using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Geo;
using Spatial4n.Context;
using Spatial4n.Distance;
using Spatial4n.Shapes;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="GeoDistanceQuery"/> and <see cref="GeoBoundingBoxQuery"/> throughput
/// on a corpus of documents with random geo coordinates.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class GeoQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Distance", "BoundingBox")]
    public string GeoQueryType { get; set; } = "Distance";

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

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoDistanceQuery()
        => _leanSearcher!.Search(
            new GeoDistanceQuery("location", centreLat: 51.5, centreLon: -0.1, radiusMetres: 50_000), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoBoundingBoxQuery()
        => _leanSearcher!.Search(
            new GeoBoundingBoxQuery("location", minLat: 50.0, maxLat: 53.0, minLon: -2.0, maxLon: 2.0), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_GeoDistanceQuery()
    {
        var centre = _spatialContext!.MakePoint(-0.1, 51.5);
        var args = new SpatialArgs(SpatialOperation.IsWithin,
            _spatialContext.MakeCircle(centre, DistanceUtils.Dist2Degrees(50_000d, DistanceUtils.EarthMeanRadiusKilometers)));
        var filter = _spatialStrategy!.MakeFilter(args);
        var hits = _luceneSearcher!.Search(
            new Lucene.Net.Search.MatchAllDocsQuery(), filter, TopN);
        return hits.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_GeoBoundingBoxQuery()
    {
        var rect = _spatialContext!.MakeRectangle(-2.0, 2.0, 50.0, 53.0);
        var args = new SpatialArgs(SpatialOperation.Intersects, rect);
        var filter = _spatialStrategy!.MakeFilter(args);
        var hits = _luceneSearcher!.Search(
            new Lucene.Net.Search.MatchAllDocsQuery(), filter, TopN);
        return hits.TotalHits;
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-geo-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new MMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        _spatialContext = SpatialContext.Geo;
        _spatialStrategy = new RecursivePrefixTreeStrategy(
            new GeohashPrefixTree(_spatialContext, maxLevels: 11), "location");

        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));

        var rng = new Random(42);
        for (int i = 0; i < documents.Length; i++)
        {
            double lat = rng.NextDouble() * 180.0 - 90.0;
            double lon = rng.NextDouble() * 360.0 - 180.0;
            var pt = _spatialContext.MakePoint(lon, lat);
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", documents[i],
                Lucene.Net.Documents.Field.Store.NO));
            foreach (var field in _spatialStrategy.CreateIndexableFields(pt))
                doc.Add(field);
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-geo-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });

        var rng = new Random(42);
        for (int i = 0; i < documents.Length; i++)
        {
            double lat = rng.NextDouble() * 180.0 - 90.0;
            double lon = rng.NextDouble() * 360.0 - 180.0;
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            doc.Add(new Rowles.LeanCorpus.Document.Fields.GeoPointField("location", lat, lon));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}
