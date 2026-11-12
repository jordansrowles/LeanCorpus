using BenchmarkDotNet.Attributes;
using LeanFuzzyQuery = Rowles.LeanCorpus.Search.Queries.FuzzyQuery;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures first-pass, primed replay and three-cycle no-hit workloads around the
/// unchanged 128-entry cache limit. Values are per batch, not per query.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class FuzzyCacheWorkingSetBenchmarks
{
    private const string FieldPrefix = "body\0";
    private const int TopN = 25;
    private const int ChurnPasses = 3;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(1, 127, 128, 129, 256)]
    public int WorkingSet { get; set; }

    private LeanMMapDirectory? _directory;
    private LeanIndexSearcher? _searcher;
    private LeanFuzzyQuery[] _queries = [];

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _directory = SharedStandardIndex.CreateDirectory();
        ResetSearcher();
        var terms = FuzzyExpansionCacheBenchmarks.ReadBodyDictionaryTerms(_searcher!);
        _queries = FuzzyExpansionCacheBenchmarks.CreateDictionaryVerifiedNoHitQueries(terms, WorkingSet);
        MeasureCachePhases();
        ResetSearcher();
        // Warm replay and churn start after one complete priming pass. A set
        // above capacity cannot remain warm; the telemetry records its misses.
        if (SearchPasses(1) != 0)
            throw new InvalidOperationException("Working-set queries returned unexpected logical hits.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _directory?.Dispose();
    }

    [IterationSetup(Target = nameof(ColdWorkingSet))]
    public void PrepareColdWorkingSet() => ResetSearcher();

    [Benchmark(Description = "Cold first pass (N searches)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ColdWorkingSet() => SearchPasses(1);

    [Benchmark(Description = "Primed replay (N searches)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WarmWorkingSet() => SearchPasses(1);

    [Benchmark(Description = "Repeated churn (3N searches)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ChurnWorkingSet() => SearchPasses(ChurnPasses);

    private void ResetSearcher()
    {
        _searcher?.Dispose();
        _searcher = new LeanIndexSearcher(_directory!, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true
        });
        // Open dictionaries outside timing without populating fuzzy caches.
        foreach (var reader in _searcher.GetSegmentReaders())
            _ = reader.GetAllTermsForField(FieldPrefix);
    }

    private int SearchPasses(int passes)
    {
        int hits = 0;
        for (int pass = 0; pass < passes; pass++)
            foreach (var query in _queries)
                hits += _searcher!.Search(query, TopN).TotalHits;
        return hits;
    }

    private void MeasureCachePhases()
    {
        int capacity = FuzzyExpansionCacheBenchmarks.ReadFuzzyCacheCapacity();
        ResetSearcher();
        MeasurePhase("cold", 1, expectHits: false);
        MeasurePhase("warm-replay", 1, expectHits: WorkingSet <= capacity);
        MeasurePhase("churn", ChurnPasses, expectHits: WorkingSet <= capacity);

        void MeasurePhase(string phase, int passes, bool expectHits)
        {
            long hits = 0;
            long misses = 0;
            int maximumRetained = 0;
            foreach (var reader in _searcher!.GetSegmentReaders())
            {
                for (int pass = 0; pass < passes; pass++)
                {
                    foreach (var query in _queries)
                    {
                        bool hit = reader.TryGetCachedFuzzyMatches(FieldPrefix, query.Term,
                            query.MaxEdits, query.MaxExpansions, out var cached);
                        var results = reader.GetFuzzyMatches(FieldPrefix, query.Term.AsSpan(),
                            query.MaxEdits, query.MaxExpansions, useCache: true);
                        if (results.Count != 0 || (hit && !ReferenceEquals(cached, results)))
                            throw new InvalidOperationException("No-hit cache results changed during working-set preflight.");
                        if (hit) hits++; else misses++;
                    }
                    int retained = _queries.Count(query => reader.TryGetCachedFuzzyMatches(
                        FieldPrefix, query.Term, query.MaxEdits, query.MaxExpansions, out _));
                    maximumRetained = Math.Max(maximumRetained, retained);
                    if (retained > capacity)
                        throw new InvalidOperationException("The fuzzy cache exceeded its existing capacity.");
                }
            }
            if ((expectHits && misses != 0) || (!expectHits && hits != 0))
                throw new InvalidOperationException($"Unexpected {phase} cache counts: hits={hits}, misses={misses}.");
            Console.WriteLine($"Fuzzy working-set preflight: working-set={WorkingSet}, phase={phase}, "
                + $"searches-per-invocation={WorkingSet * passes}, readers={_searcher.GetSegmentReaders().Count}, "
                + $"cache-hits={hits}, cache-misses={misses}, retained-at-pass-end-max={maximumRetained}, "
                + $"capacity={capacity}, expansions=0, logical-hits=0.");
        }
    }
}
