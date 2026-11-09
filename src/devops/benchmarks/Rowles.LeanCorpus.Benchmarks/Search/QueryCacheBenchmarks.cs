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
/// rotating miss workload so the cache behaviour is explicit. Setup proves the hit and miss
/// rates with the cache counters and checks cached results against uncached ones, outside timing.
/// Prebuilt rows reuse query instances on both sides; construction-included rows build the
/// query on every call.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class QueryCacheBenchmarks
{
    private const int TopN = 25;
    private const int CacheEntries = 1024;
    internal const int MissKeyCount = 2048;
    private const string FieldBody = "body";
    private const string QueryTerm1 = "government";
    private const string QueryTerm2 = "market";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _cachedSearcher;
    private LeanIndexSearcher? _uncachedSearcher;
    private Rowles.LeanCorpus.Search.Queries.BooleanQuery _prebuiltBooleanQuery = null!;
    private Rowles.LeanCorpus.Search.Queries.BooleanQuery[] _booleanCacheMissQueries = [];
    private int _booleanCacheMissIndex;
    private int _booleanUncachedMissIndex;

    private LuceneIndexSearcher? _luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanDirectory = SharedStandardIndex.CreateDirectory();
        _cachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = true, QueryCacheMaxEntries = CacheEntries });
        _uncachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = false });
        _luceneSearcher = SharedStandardIndex.LuceneSearcher;

        _prebuiltBooleanQuery = BuildBooleanQuery();
        _booleanCacheMissQueries = new Rowles.LeanCorpus.Search.Queries.BooleanQuery[MissKeyCount];
        // Boosts start at 2 so no miss key shares the boost-1 hit query's fingerprint.
        for (int i = 0; i < _booleanCacheMissQueries.Length; i++)
            _booleanCacheMissQueries[i] = BuildBooleanQuery(i + 2);

        ProveCacheWorkload();
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

    [Benchmark(Description = "Cache enabled, prebuilt BooleanQuery")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache_BooleanQuery_Prebuilt()
        => _cachedSearcher!.Search(_prebuiltBooleanQuery, TopN).TotalHits;

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

    [Benchmark(Description = "Cache disabled, prebuilt BooleanQuery")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache_BooleanQuery_Prebuilt()
        => _uncachedSearcher!.Search(_prebuiltBooleanQuery, TopN).TotalHits;

    [Benchmark(Description = "Cache disabled, BooleanQuery miss variants")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache_BooleanQuery_MissVariants()
    {
        var query = _booleanCacheMissQueries[_booleanUncachedMissIndex++ & (_booleanCacheMissQueries.Length - 1)];
        return _uncachedSearcher!.Search(query, TopN).TotalHits;
    }

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

    /// <summary>
    /// Proves, outside timing, that hit rows hit, miss rows miss, cached results equal uncached
    /// results, miss keys are distinct and invalidation forces a miss.
    /// </summary>
    private void ProveCacheWorkload()
    {
        var cache = _cachedSearcher!.Cache ?? throw new InvalidOperationException("The cached searcher has no query cache.");
        cache.Clear();

        foreach (var (name, query) in new (string, Rowles.LeanCorpus.Search.Query)[]
                 {
                     ("term", new TermQuery(FieldBody, QueryTerm1)),
                     ("boolean", BuildBooleanQuery()),
                     ("boolean prebuilt", _prebuiltBooleanQuery)
                 })
        {
            var uncached = _uncachedSearcher!.Search(query, TopN);
            long hits = cache.Hits, misses = cache.Misses;
            _cachedSearcher.Search(query, TopN);
            var second = _cachedSearcher.Search(query, TopN);
            ValidateCounterDelta(string.Concat(name, " populate and hit"), cache.Hits - hits, cache.Misses - misses,
                expectedHits: name == "boolean prebuilt" ? 2 : 1, expectedMisses: name == "boolean prebuilt" ? 0 : 1);
            ValidateSameResults(name, uncached, second, docId => _uncachedSearcher.GetStoredFields(docId)["id"][0]);
        }

        int distinctKeys = _booleanCacheMissQueries
            .Select(QueryCache.CreateQueryFingerprint)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctKeys != MissKeyCount
            || _booleanCacheMissQueries.Any(query => QueryCache.CreateQueryFingerprint(query) == QueryCache.CreateQueryFingerprint(_prebuiltBooleanQuery)))
            throw new InvalidOperationException($"Query-cache miss keys are not distinct: {distinctKeys} of {MissKeyCount}.");

        for (int cycle = 1; cycle <= 2; cycle++)
        {
            long hits = cache.Hits, misses = cache.Misses;
            foreach (var query in _booleanCacheMissQueries)
                _cachedSearcher.Search(query, TopN);
            long hitDelta = cache.Hits - hits, missDelta = cache.Misses - misses;
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Benchmark preflight query-cache miss cycle {cycle}: keys={MissKeyCount}, capacity={CacheEntries}, hits={hitDelta}, misses={missDelta}"));
            ValidateCounterDelta(string.Concat("miss cycle ", cycle.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                hitDelta, missDelta, expectedHits: 0, expectedMisses: MissKeyCount);
        }

        _cachedSearcher.Search(_prebuiltBooleanQuery, TopN);
        cache.Invalidate();
        long hitsBefore = cache.Hits, missesBefore = cache.Misses;
        _cachedSearcher.Search(_prebuiltBooleanQuery, TopN);
        ValidateCounterDelta("after invalidation", cache.Hits - hitsBefore, cache.Misses - missesBefore, expectedHits: 0, expectedMisses: 1);

        cache.Clear();
        _booleanCacheMissIndex = 0;
        _booleanUncachedMissIndex = 0;
    }

    /// <summary>Fails setup when the cache counters do not show the expected hits and misses.</summary>
    internal static void ValidateCounterDelta(string step, long hits, long misses, long expectedHits, long expectedMisses)
    {
        if (hits != expectedHits || misses != expectedMisses)
            throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Query-cache {step}: expected {expectedHits} hits and {expectedMisses} misses, observed {hits} hits and {misses} misses."));
    }

    /// <summary>Fails setup unless totals, logical IDs and scores are identical, in order.</summary>
    internal static void ValidateSameResults(string name, TopDocs expected, TopDocs actual, Func<int, string> logicalId)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        BenchmarkFixtureEvidence.RequireWork("query-cache", name, BenchmarkFixtureEvidence.LeanCorpusEngine, expected.TotalHits);
        if (expected.TotalHits != actual.TotalHits || expected.ScoreDocs.Length != actual.ScoreDocs.Length)
            throw new InvalidOperationException(
                $"Query-cache {name} totals differ: uncached {expected.TotalHits}/{expected.ScoreDocs.Length}, cached {actual.TotalHits}/{actual.ScoreDocs.Length}.");
        for (int i = 0; i < expected.ScoreDocs.Length; i++)
        {
            var left = expected.ScoreDocs[i];
            var right = actual.ScoreDocs[i];
            if (!string.Equals(logicalId(left.DocId), logicalId(right.DocId), StringComparison.Ordinal)
                || BitConverter.SingleToInt32Bits(left.Score) != BitConverter.SingleToInt32Bits(right.Score))
                throw new InvalidOperationException(
                    $"Query-cache {name} result {i} differs: uncached {logicalId(left.DocId)}@{left.Score}, cached {logicalId(right.DocId)}@{right.Score}.");
        }
    }
}
