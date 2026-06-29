using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Scoring;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
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
using LuceneSortField = Lucene.Net.Search.SortField;
using LeanSortField = Rowles.LeanCorpus.Search.Scoring.SortField;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures sorted-search behaviour with sorted and unsorted indices.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class IndexSortSearchBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _unsortedPath = string.Empty;
    private string _sortedPath = string.Empty;
    private LeanMMapDirectory? _unsortedDir;
    private LeanMMapDirectory? _sortedDir;
    private LeanIndexSearcher? _unsortedSearcher;
    private LeanIndexSearcher? _sortedSearcher;

    // Lucene.NET state
    private string _lucenePath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documentsWithPrices = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildSearchIndices(documentsWithPrices);
        BuildLuceneIndex(documentsWithPrices);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _unsortedSearcher?.Dispose();
        _sortedSearcher?.Dispose();

        if (!string.IsNullOrWhiteSpace(_unsortedPath) && Directory.Exists(_unsortedPath))
            Directory.Delete(_unsortedPath, recursive: true);
        if (!string.IsNullOrWhiteSpace(_sortedPath) && Directory.Exists(_sortedPath))
            Directory.Delete(_sortedPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_lucenePath) && Directory.Exists(_lucenePath))
            Directory.Delete(_lucenePath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SortedSearch_EarlyTermination()
    {
        var topDocs = _sortedSearcher!.Search(new TermQuery("body", "product"), TopN, LeanSortField.Numeric("price"));
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SortedSearch_PostSort()
    {
        var topDocs = _unsortedSearcher!.Search(new TermQuery("body", "product"), TopN, LeanSortField.Numeric("price"));
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SortedSearch()
    {
        var q = new LuceneTermQuery(new LuceneTerm("body", "product"));
        var sort = new Lucene.Net.Search.Sort(
            new LuceneSortField("price", Lucene.Net.Search.SortFieldType.DOUBLE));
        var hits = _luceneSearcher!.Search(q, TopN, sort);
        return hits.TotalHits;
    }

    private void BuildLuceneIndex((string Body, double Price)[] docs)
    {
        _lucenePath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_lucenePath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_lucenePath));
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

    private void BuildSearchIndices((string Body, double Price)[] documentsWithPrices)
    {
        _unsortedPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-ns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_unsortedPath);
        _unsortedDir = new LeanMMapDirectory(_unsortedPath);

        using (var writer = new LeanIndexWriter(_unsortedDir, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            IndexDocuments(writer, documentsWithPrices);
            writer.Commit();
        }
        _unsortedSearcher = new LeanIndexSearcher(_unsortedDir);

        _sortedPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-s-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sortedPath);
        _sortedDir = new LeanMMapDirectory(_sortedPath);

        using (var writer = new LeanIndexWriter(_sortedDir, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            IndexSort = new IndexSort(LeanSortField.Numeric("price"))
        }))
        {
            IndexDocuments(writer, documentsWithPrices);
            writer.Commit();
        }
        _sortedSearcher = new LeanIndexSearcher(_sortedDir);
    }

    private static void IndexDocuments(LeanIndexWriter writer, (string Body, double Price)[] documentsWithPrices)
    {
        for (int i = 0; i < documentsWithPrices.Length; i++)
        {
            var (body, price) = documentsWithPrices[i];
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", body));
            doc.Add(new NumericField("price", price));
            writer.AddDocument(doc);
        }
    }
}
