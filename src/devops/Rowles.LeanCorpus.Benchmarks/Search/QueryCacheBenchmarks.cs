using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Searcher;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares warm query-cache throughput against a cold (disabled) cache.
/// Boolean queries are measured both as repeated structural hits and as a
/// rotating miss workload so the cache behaviour is explicit.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryCacheBenchmarks
{
    private const int TopN = 25;
    private const string FieldBody = "body";
    private const string QueryTerm1 = "government";
    private const string QueryTerm2 = "market";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _cachedSearcher;
    private LeanIndexSearcher? _uncachedSearcher;
    private Rowles.LeanCorpus.Search.Queries.BooleanQuery[] _booleanCacheMissQueries = [];
    private int _booleanCacheMissIndex;

    private LuceneIndexSearcher? _luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanDirectory = SharedStandardIndex.CreateDirectory();
        _cachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = true, QueryCacheMaxEntries = 1024 });
        _uncachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = false });
        _luceneSearcher = SharedStandardIndex.LuceneSearcher;

        _booleanCacheMissQueries = new Rowles.LeanCorpus.Search.Queries.BooleanQuery[2048];
        for (int i = 0; i < _booleanCacheMissQueries.Length; i++)
            _booleanCacheMissQueries[i] = BuildBooleanQuery(i + 1);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cachedSearcher?.Dispose();
        _uncachedSearcher?.Dispose();
        _leanDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache()
        => _uncachedSearcher!.Search(new TermQuery(FieldBody, QueryTerm1), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache()
        => _cachedSearcher!.Search(new TermQuery(FieldBody, QueryTerm1), TopN).TotalHits;

    [Benchmark(Description = "Cache enabled, cacheable BooleanQuery")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache_BooleanQuery()
        => _cachedSearcher!.Search(BuildBooleanQuery(), TopN).TotalHits;

    [Benchmark(Description = "Cache enabled, BooleanQuery misses")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache_BooleanQuery_Miss()
    {
        var query = _booleanCacheMissQueries[_booleanCacheMissIndex++ & (_booleanCacheMissQueries.Length - 1)];
        return _cachedSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark(Description = "Cache disabled, BooleanQuery")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache_BooleanQuery()
        => _uncachedSearcher!.Search(BuildBooleanQuery(), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var q = new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm1));
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    private static Rowles.LeanCorpus.Search.Queries.BooleanQuery BuildBooleanQuery(float boost = 1.0f)
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery(FieldBody, QueryTerm1), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery(FieldBody, QueryTerm2), Rowles.LeanCorpus.Search.Occur.Should);
        return builder.WithBoost(boost).Build();
    }

}
