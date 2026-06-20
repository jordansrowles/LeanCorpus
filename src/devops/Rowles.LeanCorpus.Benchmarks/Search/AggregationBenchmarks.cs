using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Aggregations;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDoubleField = Lucene.Net.Documents.DoubleField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

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

    // Lucene.NET index state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    private static readonly AggregationRequest StatsRequest =
        new("price_stats", "price", AggregationType.Stats);

    private static readonly AggregationRequest HistogramRequest =
        new("price_hist", "price", AggregationType.Histogram) { HistogramInterval = 100.0 };

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        try
        {
            BuildLeanIndex(docs);
            BuildLuceneIndex(docs);
        }
        catch
        {
            _leanSearcher?.Dispose();
            _luceneReader?.Dispose();
            _luceneDirectory?.Dispose();
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var q = new LuceneTermQuery(new LuceneTerm("body", "government"));
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearchWithStats()
    {
        var q = new LuceneTermQuery(new LuceneTerm("body", "government"));
        var hits = _luceneSearcher!.Search(q, TopN);
        // Manual stats aggregation over DocValues for matching docs.
        var reader = _luceneReader!;
        var leaves = reader.Leaves;
        NumericDocValues? dv = null;
        if (leaves.Count > 0)
            dv = leaves[0].AtomicReader.GetNumericDocValues("price");
        double sum = 0, sumSq = 0, min = double.MaxValue, max = double.MinValue;
        int count = 0;
        if (dv is not null)
        {
            foreach (var sd in hits.ScoreDocs)
            {
                var price = dv.Get(sd.Doc);
                sum += price;
                sumSq += price * price;
                if (price < min) min = price;
                if (price > max) max = price;
                count++;
            }
        }
        return count;
    }

    private void BuildLuceneIndex((string Body, double Price)[] docs)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-agg-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", docs[i].Body,
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneDoubleField("price", docs[i].Price,
                Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private void BuildLeanIndex((string Body, double Price)[] docs)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-agg-{Guid.NewGuid():N}");
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
