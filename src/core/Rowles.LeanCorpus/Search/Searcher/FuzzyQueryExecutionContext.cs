using System.Collections.Concurrent;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Coordinates whole-index plans for one query execution: fuzzy term expansion, shared multi-term
/// expansions and globally selected vector candidates (ADR049).
/// </summary>
internal sealed class FuzzyQueryExecutionContext(
    IReadOnlyList<SegmentReader> readers,
    bool useExpansionCache,
    IReadOnlyDictionary<VectorQuery, PreparedVectorQuery>? seededVectorPlans = null)
{
    private static readonly IReadOnlyList<(string Term, long Offset, int Distance)> EmptyMatches =
        Array.Empty<(string Term, long Offset, int Distance)>();

    // Every search pushes a context, so these are created on first use: searches without fuzzy
    // clauses, and warm searches that never compile an automaton, do not pay for them.
    // Usually one entry per search, so a locked dictionary (which is also its own lock) is far
    // cheaper than a concurrent one, and it is created only by the first fuzzy clause.
    private Dictionary<FuzzyPlanKey, Lazy<FuzzyExpansionPlan>>? _plans;
    private ConcurrentDictionary<AutomatonKey, Lazy<LevenshteinAutomaton>>? _automata;
    private int _automatonCompilationCount;

    internal int AutomatonCompilationCount => Volatile.Read(ref _automatonCompilationCount);

    private MultiTermExpansionContext? _expansions;

    // Keyed by value: VectorQuery equality covers every setting that changes the selected set, so a
    // rewrite that produces an equal copy shares this search's selection, and a MultiReader seed.
    private Dictionary<VectorQuery, Lazy<PreparedVectorQuery>>? _vectorPlans;

    /// <summary>
    /// The resource check of the route currently preparing this search's vector queries, applied to
    /// nested preparation as well, such as a vector query inside another vector query's filter.
    /// </summary>
    internal Action? VectorResourceCheck { get; set; }

    /// <summary>
    /// Returns this search's selected set for <paramref name="query"/>, building it once. A failed or
    /// interrupted build is discarded, so a later search never observes a partial selection.
    /// </summary>
    internal PreparedVectorQuery GetVectorPlan(VectorQuery query, Func<PreparedVectorQuery> build)
    {
        if (seededVectorPlans is not null && seededVectorPlans.TryGetValue(query, out var seeded))
            return seeded;

        var plans = Volatile.Read(ref _vectorPlans)
            ?? Interlocked.CompareExchange(
                ref _vectorPlans,
                new Dictionary<VectorQuery, Lazy<PreparedVectorQuery>>(),
                null)
            ?? _vectorPlans!;
        Lazy<PreparedVectorQuery>? lazy;
        lock (plans)
        {
            if (!plans.TryGetValue(query, out lazy))
            {
                lazy = new Lazy<PreparedVectorQuery>(build, LazyThreadSafetyMode.ExecutionAndPublication);
                plans.Add(query, lazy);
            }
        }

        try
        {
            return lazy.Value;
        }
        catch
        {
            lock (plans)
            {
                if (plans.TryGetValue(query, out var current) && ReferenceEquals(current, lazy))
                    plans.Remove(query);
            }
            throw;
        }
    }

    /// <summary>
    /// This search's shared prefix, wildcard and regexp expansions, carried in the same per-search scope.
    /// Created on first use, so searches without those clauses allocate nothing for it.
    /// </summary>
    internal MultiTermExpansionContext Expansions
        => EnsureCreated(ref _expansions);

    private static T EnsureCreated<T>(ref T? field) where T : class, new()
        => Volatile.Read(ref field) ?? Interlocked.CompareExchange(ref field, new T(), null) ?? field!;

    internal IReadOnlyList<(string Term, long Offset, int Distance)> GetMatches(
        FuzzyQuery query,
        SegmentReader reader)
    {
        var plan = GetPlan(query);
        return plan.MatchesByReader is { } matchesByReader
            && matchesByReader.TryGetValue(reader, out var matches)
                ? matches
                : EmptyMatches;
    }

    /// <summary>
    /// Returns the globally selected terms, building the shared plan if this is its first use.
    /// <paramref name="checkResources"/> runs between readers while the plan is built; if it throws, the
    /// failed plan is discarded so the context never retains a partial or faulted entry.
    /// </summary>
    internal IReadOnlyList<string> GetSelectedTerms(FuzzyQuery query, Action? checkResources = null)
        => GetPlan(query, checkResources).SelectedTerms;

    private FuzzyExpansionPlan GetPlan(FuzzyQuery query, Action? checkResources = null)
    {
        var key = new FuzzyPlanKey(query.Field, query.Term, query.MaxEdits, query.MaxExpansions);
        var plans = EnsureCreated(ref _plans);
        Lazy<FuzzyExpansionPlan>? lazy;
        lock (plans)
        {
            if (!plans.TryGetValue(key, out lazy))
            {
                lazy = new Lazy<FuzzyExpansionPlan>(
                    () => BuildPlan(query, checkResources),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                plans.Add(key, lazy);
            }
        }

        try
        {
            return lazy.Value;
        }
        catch
        {
            lock (plans)
            {
                if (plans.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                    plans.Remove(key);
            }
            throw;
        }
    }

    private FuzzyExpansionPlan BuildPlan(FuzzyQuery query, Action? checkResources)
    {
        string fieldPrefix = string.Concat(query.Field, "\0");
        Dictionary<SegmentReader, List<(string Term, long Offset, int Distance)>>? matchesByReader = null;
        Dictionary<string, FuzzyCandidate>? candidatesByTerm = null;

        foreach (var reader in readers)
        {
            checkResources?.Invoke();
            var matches = reader.GetFuzzyMatches(
                fieldPrefix,
                query.Term.AsSpan(),
                query.MaxEdits,
                query.MaxExpansions,
                useExpansionCache,
                () => GetAutomaton(query.Term, query.MaxEdits));
            if (matches.Count == 0)
                continue;

            matchesByReader ??= new Dictionary<SegmentReader, List<(string Term, long Offset, int Distance)>>(
                ReferenceEqualityComparer.Instance);
            matchesByReader.Add(reader, matches);
            candidatesByTerm ??= new Dictionary<string, FuzzyCandidate>(StringComparer.Ordinal);

            foreach (var match in matches)
            {
                if (!candidatesByTerm.ContainsKey(match.Term))
                {
                    candidatesByTerm.Add(match.Term, new FuzzyCandidate(match.Term, match.Distance));
                }
            }
        }

        if (candidatesByTerm is null || matchesByReader is null)
            return FuzzyExpansionPlan.Empty;

        var candidates = new FuzzyCandidate[candidatesByTerm.Count];
        candidatesByTerm.Values.CopyTo(candidates, 0);
        Array.Sort(candidates, FuzzyCandidateComparer.Instance);
        int selectedCount = Math.Min(candidates.Length, query.MaxExpansions);
        var selectedTerms = new string[selectedCount];
        for (int i = 0; i < selectedCount; i++)
            selectedTerms[i] = candidates[i].Term;

        // Nothing was trimmed, so every reader's matches are already the selected ones.
        if (selectedCount == candidates.Length)
            return new FuzzyExpansionPlan(matchesByReader, selectedTerms);

        var selected = new HashSet<string>(selectedTerms, StringComparer.Ordinal);
        foreach (var reader in readers)
        {
            if (!matchesByReader.TryGetValue(reader, out var segmentMatches))
                continue;

            var selectedMatches = segmentMatches
                .Where(match => selected.Contains(match.Term))
                .ToList();
            if (selectedMatches.Count == 0)
                matchesByReader.Remove(reader);
            else
                matchesByReader[reader] = selectedMatches;
        }

        return new FuzzyExpansionPlan(matchesByReader, selectedTerms);
    }

    private LevenshteinAutomaton GetAutomaton(string term, int maxEdits)
    {
        var key = new AutomatonKey(term, maxEdits);
        return EnsureCreated(ref _automata).GetOrAdd(
            key,
            _ => new Lazy<LevenshteinAutomaton>(
                () =>
                {
                    Interlocked.Increment(ref _automatonCompilationCount);
                    return new LevenshteinAutomaton(term, maxEdits);
                },
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private readonly record struct FuzzyPlanKey(string Field, string Term, int MaxEdits, int MaxExpansions);
    private readonly record struct AutomatonKey(string Term, int MaxEdits);
    private readonly record struct FuzzyCandidate(string Term, int Distance);

    private sealed class FuzzyExpansionPlan(
        Dictionary<SegmentReader, List<(string Term, long Offset, int Distance)>>? matchesByReader,
        string[] selectedTerms)
    {
        internal static readonly FuzzyExpansionPlan Empty = new(null, Array.Empty<string>());

        internal Dictionary<SegmentReader, List<(string Term, long Offset, int Distance)>>? MatchesByReader { get; }
            = matchesByReader;

        internal string[] SelectedTerms { get; } = selectedTerms;
    }

    private sealed class FuzzyCandidateComparer : IComparer<FuzzyCandidate>
    {
        internal static readonly FuzzyCandidateComparer Instance = new();

        public int Compare(FuzzyCandidate left, FuzzyCandidate right)
        {
            int distanceComparison = left.Distance.CompareTo(right.Distance);
            return distanceComparison != 0
                ? distanceComparison
                : CompareUtf8Order(left.Term, right.Term);
        }
    }

    /// <summary>
    /// Orders two strings exactly as their UTF-8 encodings compare bytewise (Unicode scalar order),
    /// without encoding them. UTF-16 order differs only where a surrogate meets U+E000 to U+FFFF,
    /// so those code units are remapped before comparison.
    /// </summary>
    internal static int CompareUtf8Order(string left, string right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            char a = left[i], b = right[i];
            if (a != b)
                return ScalarOrderKey(a) - ScalarOrderKey(b);
        }

        return left.Length - right.Length;
    }

    private static int ScalarOrderKey(char c)
        => c < 0xD800 ? c : c >= 0xE000 ? c - 0x800 : c + 0x2000;
}
