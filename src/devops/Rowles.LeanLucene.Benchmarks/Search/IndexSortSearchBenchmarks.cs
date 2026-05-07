using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;

namespace Rowles.LeanLucene.Benchmarks;

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

    [GlobalSetup]
    public void Setup()
    {
        var documentsWithPrices = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildSearchIndices(documentsWithPrices);
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
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_SortedSearch_EarlyTermination()
    {
        var topDocs = _sortedSearcher!.Search(new TermQuery("body", "product"), TopN, SortField.Numeric("price"));
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_SortedSearch_PostSort()
    {
        var topDocs = _unsortedSearcher!.Search(new TermQuery("body", "product"), TopN, SortField.Numeric("price"));
        return topDocs.TotalHits;
    }

    private void BuildSearchIndices((string Body, double Price)[] documentsWithPrices)
    {
        _unsortedPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-sort-ns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_unsortedPath);
        _unsortedDir = new LeanMMapDirectory(_unsortedPath);

        using (var writer = new IndexWriter(_unsortedDir, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            IndexDocuments(writer, documentsWithPrices);
            writer.Commit();
        }
        _unsortedSearcher = new LeanIndexSearcher(_unsortedDir);

        _sortedPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-sort-s-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sortedPath);
        _sortedDir = new LeanMMapDirectory(_sortedPath);

        using (var writer = new IndexWriter(_sortedDir, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            IndexSort = new IndexSort(SortField.Numeric("price"))
        }))
        {
            IndexDocuments(writer, documentsWithPrices);
            writer.Commit();
        }
        _sortedSearcher = new LeanIndexSearcher(_sortedDir);
    }

    private static void IndexDocuments(IndexWriter writer, (string Body, double Price)[] documentsWithPrices)
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
