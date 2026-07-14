using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanSearcherManager = Rowles.LeanCorpus.Search.Searcher.SearcherManager;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures acquire and release overhead of <see cref="LeanSearcherManager"/>
/// against Lucene.NET <c>SearcherManager</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class SearcherManagerBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanSearcherManager? _leanManager;

    private RAMDirectory? _luceneDirectory;
    private Lucene.Net.Search.SearcherManager? _luceneManager;

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
        _leanManager?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneManager?.Dispose();
        _luceneDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearcherManager_AcquireSearch()
    {
        var searcher = _leanManager!.Acquire();
        try
        {
            return searcher.Search(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"), TopN).TotalHits;
        }
        finally
        {
            _leanManager.Release(searcher);
        }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearcherManager_AcquireLease()
    {
        using var lease = _leanManager!.AcquireLease();
        return lease.Searcher.Search(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearcherManager_AcquireSearch()
    {
        var searcher = _luceneManager!.Acquire();
        try
        {
            return searcher.Search(new Lucene.Net.Search.TermQuery(new Term("body", "government")), TopN).TotalHits;
        }
        finally
        {
            _luceneManager.Release(searcher);
        }
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-searchermgr-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanManager = new LeanSearcherManager(_leanDirectory);
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneDirectory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Lucene.Net.Documents.Field.Store.NO),
                new LuceneTextField("body", documents[i], Lucene.Net.Documents.Field.Store.NO)
            };
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneManager = new Lucene.Net.Search.SearcherManager(_luceneDirectory, searcherFactory: null);
    }
}
