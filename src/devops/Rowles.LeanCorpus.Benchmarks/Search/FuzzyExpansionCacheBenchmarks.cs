using System.Globalization;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanFuzzyQuery = Rowles.LeanCorpus.Search.Queries.FuzzyQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures warm and mixed fuzzy-expansion cache workloads, including a repeatable
/// query set large enough to force the bounded cache's reset path.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class FuzzyExpansionCacheBenchmarks
{
    private const int TopN = 25;
    private const int MaxExpansions = 50;
    private const int MaxNoHitEdits = 2;
    private const int MaxNoHitCandidateAttempts = 512;
    private const int MixedRepeatsPerQuery = 32;
    private const uint MixedWorkloadSeed = 0x4C433343;
    private const string BodyFieldPrefix = "body\0";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private LeanMMapDirectory? _directory;
    private LeanIndexSearcher? _cachedSearcher;
    private LeanIndexSearcher? _uncachedSearcher;
    private LeanFuzzyQuery _positiveQuery = null!;
    private LeanFuzzyQuery _negativeQuery = null!;
    private LeanFuzzyQuery[] _negativeQueries = [];
    private LeanFuzzyQuery[] _mixedQueries = [];
    private LeanFuzzyQuery[] _mixedWorkload = [];
    private LeanFuzzyQuery[] _evictionQueries = [];
    private int _mixedIndex;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _directory = SharedStandardIndex.CreateDirectory();
        _cachedSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        _uncachedSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        });

        _positiveQuery = CreateQuery("goverment", maxEdits: 1);
        LeanFuzzyQuery[] positiveMixedQueries =
        [
            CreateQuery("marke", maxEdits: 1),
            CreateQuery("goverment", maxEdits: 1),
            CreateQuery("presdnt", maxEdits: 2),
            CreateQuery("econmic", maxEdits: 1)
        ];
        var cacheCapacity = ReadFuzzyCacheCapacity();

        using (var preflightSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false
        }))
        {
            var dictionaryTerms = ReadBodyDictionaryTerms(preflightSearcher);
            _negativeQueries = CreateDictionaryVerifiedNoHitQueries(dictionaryTerms, checked(cacheCapacity + 1));
            _negativeQuery = _negativeQueries[0];
            _mixedQueries = positiveMixedQueries
                .Concat(_negativeQueries.Skip(1).Take(positiveMixedQueries.Length))
                .ToArray();
            _mixedWorkload = CreateMixedWorkload(
                positiveMixedQueries,
                _negativeQueries.Skip(1).Take(positiveMixedQueries.Length).ToArray());
            _evictionQueries = _negativeQueries;

            Validate(preflightSearcher, _positiveQuery, expectedPositive: true);
            foreach (var query in _negativeQueries)
                Validate(preflightSearcher, query, expectedPositive: false);
            foreach (var query in positiveMixedQueries)
                Validate(preflightSearcher, query, expectedPositive: true);
            foreach (var query in _evictionQueries)
                Validate(preflightSearcher, query, expectedPositive: false);
        }

        using (var cacheTelemetrySearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        }))
            MeasureMixedExpansionCacheHitRate(cacheTelemetrySearcher, _mixedWorkload, positiveMixedQueries);

        // Keep only the cache-effect searcher alive during measurement. Query-result
        // caching is disabled so repeated searches still exercise fuzzy expansion.
        _ = _cachedSearcher.Search(_positiveQuery, TopN);
        _ = _cachedSearcher.Search(_negativeQuery, TopN);
        foreach (var query in _mixedQueries)
            _ = _cachedSearcher.Search(query, TopN);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _uncachedSearcher?.Dispose();
        _cachedSearcher?.Dispose();
        _directory?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Warm positive expansion cache")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WarmPositiveExpansionCache()
        => _cachedSearcher!.Search(_positiveQuery, TopN).TotalHits;

    [Benchmark(Description = "Warm negative expansion cache")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WarmNegativeExpansionCache()
        => _cachedSearcher!.Search(_negativeQuery, TopN).TotalHits;

    [Benchmark(Description = "Uncached negative expansion lookup")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int UncachedNegativeExpansionCache()
        => _uncachedSearcher!.Search(_negativeQuery, TopN).TotalHits;

    [Benchmark(Description = "Mixed warm positive and negative expansion cache")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int MixedWarmExpansionCache()
    {
        var query = _mixedWorkload[(Interlocked.Increment(ref _mixedIndex) - 1) & (_mixedWorkload.Length - 1)];
        return _cachedSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark(Description = "Cache eviction cycle followed by positive search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ExpansionCacheEvictionCycle()
    {
        foreach (var query in _evictionQueries)
            _ = _cachedSearcher!.Search(query, TopN);

        return _cachedSearcher!.Search(_positiveQuery, TopN).TotalHits;
    }

    private static byte[][] ReadBodyDictionaryTerms(LeanIndexSearcher searcher)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reader in searcher.GetSegmentReaders())
        {
            foreach (var (qualifiedTerm, _) in reader.GetAllTermsForField(BodyFieldPrefix))
            {
                if (!qualifiedTerm.StartsWith(BodyFieldPrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Term dictionary returned '{qualifiedTerm}' outside the requested body field.");

                terms.Add(qualifiedTerm[BodyFieldPrefix.Length..]);
            }
        }

        if (terms.Count == 0)
            throw new InvalidOperationException("The fuzzy cache benchmark index has no body terms to verify no-hit queries against.");

        return terms.Select(static term => Encoding.UTF8.GetBytes(term)).ToArray();
    }

    private static LeanFuzzyQuery[] CreateDictionaryVerifiedNoHitQueries(byte[][] dictionaryTerms, int count)
    {
        var queries = new List<LeanFuzzyQuery>(count);
        for (var candidate = 0; candidate < MaxNoHitCandidateAttempts && queries.Count < count; candidate++)
        {
            var term = string.Concat("zzzzfuzzycache-nohit-", candidate.ToString("x8", CultureInfo.InvariantCulture));
            var candidateUtf8 = Encoding.UTF8.GetBytes(term);
            if (!dictionaryTerms.All(dictionaryTerm =>
                    !IsWithinUtf8EditDistance(candidateUtf8, dictionaryTerm, MaxNoHitEdits)))
                continue;

            queries.Add(CreateQuery(term, MaxNoHitEdits));
        }

        if (queries.Count != count)
            throw new InvalidOperationException(
                $"Only {queries.Count} of {count} deterministic no-hit fuzzy queries were verified within {MaxNoHitCandidateAttempts} candidates.");

        return queries.ToArray();
    }

    private static bool IsWithinUtf8EditDistance(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, int maxEdits)
    {
        if (Math.Abs(first.Length - second.Length) > maxEdits)
            return false;

        if (first.Length > second.Length)
        {
            var temporary = first;
            first = second;
            second = temporary;
        }

        Span<int> row = first.Length + 1 <= 256
            ? stackalloc int[first.Length + 1]
            : new int[first.Length + 1];
        for (var index = 0; index < row.Length; index++)
            row[index] = index;

        for (var secondIndex = 1; secondIndex <= second.Length; secondIndex++)
        {
            var previous = row[0];
            row[0] = secondIndex;
            var rowMinimum = row[0];
            for (var firstIndex = 1; firstIndex <= first.Length; firstIndex++)
            {
                var current = Math.Min(
                    Math.Min(row[firstIndex] + 1, row[firstIndex - 1] + 1),
                    previous + (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1));
                previous = row[firstIndex];
                row[firstIndex] = current;
                rowMinimum = Math.Min(rowMinimum, current);
            }

            if (rowMinimum > maxEdits)
                return false;
        }

        return row[first.Length] <= maxEdits;
    }

    private static LeanFuzzyQuery[] CreateMixedWorkload(
        IReadOnlyList<LeanFuzzyQuery> positiveQueries,
        IReadOnlyList<LeanFuzzyQuery> negativeQueries)
    {
        var workload = new LeanFuzzyQuery[checked((positiveQueries.Count + negativeQueries.Count) * MixedRepeatsPerQuery)];
        var next = 0;
        foreach (var query in positiveQueries)
            for (var repeat = 0; repeat < MixedRepeatsPerQuery; repeat++)
                workload[next++] = query;
        foreach (var query in negativeQueries)
            for (var repeat = 0; repeat < MixedRepeatsPerQuery; repeat++)
                workload[next++] = query;

        Shuffle(workload, MixedWorkloadSeed);
        return workload;
    }

    private static void Shuffle<T>(T[] values, uint seed)
    {
        var state = seed;
        for (var index = values.Length - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var swapIndex = (int)(state % (uint)(index + 1));
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    private static void MeasureMixedExpansionCacheHitRate(
        LeanIndexSearcher searcher,
        LeanFuzzyQuery[] workload,
        IReadOnlyList<LeanFuzzyQuery> positiveQueries)
    {
        var readers = searcher.GetSegmentReaders();
        if (readers.Count == 0)
            throw new InvalidOperationException("The fuzzy cache benchmark index has no segment readers.");

        long firstPassRequests = 0;
        long firstPassHits = 0;
        long steadyStateRequests = 0;
        long steadyStateHits = 0;
        foreach (var reader in readers)
        {
            var firstResultByQuery = new Dictionary<(string Term, int MaxEdits, int MaxExpansions), List<(string Term, long Offset, int Distance)>>();
            foreach (var query in workload)
            {
                var key = (query.Term, query.MaxEdits, query.MaxExpansions);
                var results = reader.GetFuzzyMatches(
                    BodyFieldPrefix,
                    query.Term.AsSpan(),
                    query.MaxEdits,
                    query.MaxExpansions,
                    useCache: true);
                if (firstResultByQuery.TryGetValue(key, out var firstResult))
                {
                    if (!ReferenceEquals(firstResult, results))
                        throw new InvalidOperationException($"The warmed fuzzy expansion cache did not retain query '{query.Term}' during the mixed preflight.");
                    firstPassHits++;
                }
                else
                {
                    firstResultByQuery.Add(key, results);
                }

                firstPassRequests++;
            }

            foreach (var query in workload)
            {
                var key = (query.Term, query.MaxEdits, query.MaxExpansions);
                var results = reader.GetFuzzyMatches(
                    BodyFieldPrefix,
                    query.Term.AsSpan(),
                    query.MaxEdits,
                    query.MaxExpansions,
                    useCache: true);
                if (!firstResultByQuery.TryGetValue(key, out var firstResult) || !ReferenceEquals(firstResult, results))
                    throw new InvalidOperationException($"The fuzzy expansion cache missed warmed query '{query.Term}' during the steady-state mixed preflight.");

                steadyStateHits++;
                steadyStateRequests++;
            }
        }

        var distinctQueries = workload
            .Select(static query => (query.Term, query.MaxEdits, query.MaxExpansions))
            .Distinct()
            .Count();
        var expectedFirstPassHits = (long)(workload.Length - distinctQueries) * readers.Count;
        if (firstPassHits != expectedFirstPassHits || steadyStateHits != steadyStateRequests)
            throw new InvalidOperationException(
                $"Mixed fuzzy cache preflight observed {firstPassHits}/{firstPassRequests} first-pass hits and {steadyStateHits}/{steadyStateRequests} steady-state hits; expected {expectedFirstPassHits} first-pass hits and all steady-state calls to hit.");

        var positiveTerms = new HashSet<string>(positiveQueries.Select(static query => query.Term), StringComparer.Ordinal);
        var positiveRequests = workload.Count(query => positiveTerms.Contains(query.Term));
        var negativeRequests = workload.Length - positiveRequests;
        var firstPassHitRate = firstPassRequests == 0 ? 0d : (double)firstPassHits / firstPassRequests;
        var steadyStateHitRate = steadyStateRequests == 0 ? 0d : (double)steadyStateHits / steadyStateRequests;
        Console.WriteLine(
            $"Fuzzy cache mixed preflight: seed=0x{MixedWorkloadSeed:X8}, segment-readers={readers.Count}, queries={workload.Length}, positive={positiveRequests}, no-hit={negativeRequests}, first-pass-cache-hits={firstPassHits}/{firstPassRequests} ({firstPassHitRate:P2}), steady-state-cache-hits={steadyStateHits}/{steadyStateRequests} ({steadyStateHitRate:P2}).");
    }

    private static LeanFuzzyQuery CreateQuery(string term, int maxEdits)
        => new("body", term, maxEdits, MaxExpansions);

    private static void Validate(LeanIndexSearcher searcher, LeanFuzzyQuery query, bool expectedPositive)
    {
        var expansionCount = searcher.GetSegmentReaders().Sum(reader =>
            reader.GetFuzzyMatches("body\0", query.Term.AsSpan(), query.MaxEdits,
                query.MaxExpansions, useCache: false).Count);
        var totalHits = searcher.Search(query, TopN).TotalHits;
        if (expectedPositive && (expansionCount == 0 || totalHits == 0))
            throw new InvalidOperationException(
                $"Fuzzy cache preflight '{query.Term}' returned {expansionCount} expansions and {totalHits} hits; expected both to be positive.");
        if (!expectedPositive && (expansionCount != 0 || totalHits != 0))
            throw new InvalidOperationException(
                $"Fuzzy cache preflight '{query.Term}' returned {expansionCount} expansions and {totalHits} hits; expected zero.");
    }

    private static int ReadFuzzyCacheCapacity()
    {
        var field = typeof(TermDictionaryReader).GetField("MaxFuzzyCacheEntries", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetRawConstantValue() is not int capacity || capacity < 1)
            throw new InvalidOperationException("Could not resolve the Core fuzzy expansion cache capacity for the eviction workload.");
        return capacity;
    }
}
