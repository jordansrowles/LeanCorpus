using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares packed-only Geo nearest search with mixed legacy and packed
/// segments against the same exhaustive exact-distance sort.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class SpatialNearestCompatibilityBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("PackedOnly", "MixedLegacyPacked")]
    public string SegmentLayout { get; set; } = "PackedOnly";

    [Params("Uniform", "Clustered")]
    public string Distribution { get; set; } = "Uniform";

    [Params(10, 100, 1000)]
    public int TopN { get; set; }

    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _searcher;
    private MatchAllDocsQuery _query = new();

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = BenchmarkHelpers.CreateTempDirectory("lc-spatial-nearest-compat");
        BuildIndex();
        _directory = new MMapDirectory(_indexPath);
        _searcher = new IndexSearcher(_directory);

        SortField sort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs nearest = _searcher.Search(_query, TopN, sort);
        TopDocs exhaustive = _searcher.Search(_query, TopN, [sort, SortField.DocId]);
        if (nearest.TotalHits != exhaustive.TotalHits
            || !nearest.ScoreDocs.Select(static hit => hit.DocId)
                .SequenceEqual(exhaustive.ScoreDocs.Select(static hit => hit.DocId)))
            throw new InvalidOperationException("Geo nearest results differ from the exhaustive distance sort.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _searcher = null;
        _directory?.Dispose();
        _directory = null;
        if (!string.IsNullOrWhiteSpace(_indexPath))
            BenchmarkHelpers.DeleteDirectory(_indexPath);
        _indexPath = string.Empty;
    }

    [Benchmark(Baseline = true, Description = "Geo nearest exhaustive exact sort")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoNearest_Exhaustive()
    {
        SortField sort = SortField.GeoDistance("location", new GeoPoint(0, 0));
        TopDocs result = _searcher!.Search(_query, TopN, [sort, SortField.DocId]);
        return result.ScoreDocs.Length == 0 ? -1 : result.ScoreDocs[^1].DocId;
    }

    [Benchmark(Description = "Geo nearest packed and legacy segment merge")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_GeoNearest_BestFirst()
    {
        TopDocs result = _searcher!.Search(
            _query,
            TopN,
            SortField.GeoDistance("location", new GeoPoint(0, 0)));
        return result.ScoreDocs.Length == 0 ? -1 : result.ScoreDocs[^1].DocId;
    }

    private void BuildIndex()
    {
        var random = BenchmarkDeterministicRandom.Create($"benchmark/spatial-nearest-compat/geo/{Distribution}");
        using var directory = new MMapDirectory(_indexPath);
        var config = new IndexWriterConfig
        {
            BKDMaxLeafSize = 64,
            MergePolicy = NoMergePolicy.Instance
        };
        using var writer = new IndexWriter(directory, config);

        int legacyCount = SegmentLayout == "MixedLegacyPacked" ? DocumentCount / 2 : 0;
        for (int documentId = 0; documentId < legacyCount; documentId++)
            writer.AddDocument(CreateLegacyDocument(random));
        if (legacyCount > 0)
            writer.Commit();

        for (int documentId = legacyCount; documentId < DocumentCount; documentId++)
            writer.AddDocument(CreatePackedDocument(random));
        writer.Commit();

        if (legacyCount == 0)
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
    }

    private LeanDocument CreateLegacyDocument(DataForgePrng random)
    {
        (double latitude, double longitude) = CreateGeoPoint(random);
        var document = new LeanDocument();
        document.Add(new NumericField("location_lat", latitude, stored: false));
        document.Add(new NumericField("location_lon", longitude, stored: false));
        return document;
    }

    private LeanDocument CreatePackedDocument(DataForgePrng random)
    {
        (double latitude, double longitude) = CreateGeoPoint(random);
        var document = new LeanDocument();
        document.Add(new GeoPointField("location", latitude, longitude));
        return document;
    }

    private (double Latitude, double Longitude) CreateGeoPoint(DataForgePrng random)
        => Distribution == "Clustered"
            ? (random.NextDouble01() * 2 - 1, random.NextDouble01() * 2 - 1)
            : (random.NextDouble01() * 180 - 90, random.NextDouble01() * 360 - 180);
}
