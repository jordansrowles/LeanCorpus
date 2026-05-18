using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Aggregations;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="NumericAggregator"/> throughput for Stats and Histogram aggregation types
/// via the <see cref="LeanIndexSearcher.SearchWithAggregations"/> convenience method.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class AggregationBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    private static readonly AggregationRequest StatsRequest =
        new("price_stats", "price", AggregationType.Stats);

    private static readonly AggregationRequest HistogramRequest =
        new("price_hist", "price", AggregationType.Histogram) { HistogramInterval = 100.0 };

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildLeanIndex(docs);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchOnly()
        => _leanSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithStats()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, StatsRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithHistogram()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, HistogramRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithStatsAndHistogram()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, StatsRequest, HistogramRequest);
        return results.TotalHits;
    }

    private void BuildLeanIndex((string Body, double Price)[] docs)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-agg-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", docs[i].Body));
            doc.Add(new LeanNumericField("price", docs[i].Price));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}
