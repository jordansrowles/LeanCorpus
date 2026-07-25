using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanSearcherManager = Rowles.LeanCorpus.Search.Searcher.SearcherManager;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;

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
public class SearcherManagerBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanMMapDirectory? _leanDirectory;
    private LeanSearcherManager? _leanManager;

    private LuceneMMapDirectory? _luceneDirectory;
    private Lucene.Net.Search.SearcherManager? _luceneManager;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanDirectory = SharedStandardIndex.CreateDirectory();
        _leanManager = new LeanSearcherManager(_leanDirectory);
        _luceneDirectory = SharedStandardIndex.CreateLuceneDirectory();
        _luceneManager = new Lucene.Net.Search.SearcherManager(
            _luceneDirectory,
            searcherFactory: null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanManager?.Dispose();
        _leanDirectory?.Dispose();
        _luceneManager?.Dispose();
        _luceneDirectory?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "LeanCorpus acquire, search, release")]
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

    [Benchmark(Description = "LeanCorpus lease, search, release")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearcherManager_AcquireLease()
    {
        using var lease = _leanManager!.AcquireLease();
        return lease.Searcher.Search(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"), TopN).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET acquire, search, release")]
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

    [Benchmark(Description = "LeanCorpus acquire and release")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearcherManager_AcquireRelease()
    {
        var searcher = _leanManager!.Acquire();
        try
        {
            return 1;
        }
        finally
        {
            _leanManager.Release(searcher);
        }
    }

    [Benchmark(Description = "LeanCorpus lease acquire and release")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearcherManager_LeaseAcquireRelease()
    {
        using var lease = _leanManager!.AcquireLease();
        return 1;
    }

    [Benchmark(Description = "Lucene.NET acquire and release")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SearcherManager_AcquireRelease()
    {
        var searcher = _luceneManager!.Acquire();
        try
        {
            return 1;
        }
        finally
        {
            _luceneManager.Release(searcher);
        }
    }

}
