using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares BKD-backed <see cref="RangeQuery"/> throughput against Lucene.NET <c>NumericRangeQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class RangeQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Range width as a fraction of the full 1–1000 price range.</summary>
    [Params(0.01, 0.1, 0.5)]
    public double RangeWidth { get; set; } = 0.1;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    private RAMDirectory? _luceneDirectory;
    private StandardAnalyzer? _luceneAnalyzer;
    private DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildLeanIndex(docs);
        BuildLuceneIndex(docs);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RangeQuery()
    {
        double span = 1000.0 * RangeWidth;
        return _leanSearcher!.Search(new RangeQuery("price", 100, 100 + span), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NumericRangeQuery()
    {
        double span = 1000.0 * RangeWidth;
        var q = NumericRangeQuery.NewDoubleRange("price", 100.0, 100.0 + span, minInclusive: true, maxInclusive: true);
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    private void BuildLeanIndex((string Body, double Price)[] docs)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-range-{Guid.NewGuid():N}");
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

    private void BuildLuceneIndex((string Body, double Price)[] docs)
    {
        _luceneDirectory = new RAMDirectory();
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer));
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Field.Store.NO),
                new Lucene.Net.Documents.TextField("body", docs[i].Body, Field.Store.NO),
                new DoubleField("price", docs[i].Price, Field.Store.NO)
            };
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }
}
