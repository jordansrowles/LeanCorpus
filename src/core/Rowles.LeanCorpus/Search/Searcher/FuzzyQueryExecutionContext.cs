using System.Collections.Concurrent;
using System.Text;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Coordinates fuzzy term expansion across all segments for one query execution.</summary>
internal sealed class FuzzyQueryExecutionContext(
    IReadOnlyList<SegmentReader> readers,
    bool useExpansionCache)
{
    private static readonly IReadOnlyList<(string Term, long Offset, int Distance)> EmptyMatches =
        Array.Empty<(string Term, long Offset, int Distance)>();

    private readonly ConcurrentDictionary<FuzzyPlanKey, Lazy<FuzzyExpansionPlan>> _plans = new();
    private readonly ConcurrentDictionary<AutomatonKey, Lazy<LevenshteinAutomaton>> _automata = new();
    private int _automatonCompilationCount;

    internal int AutomatonCompilationCount => Volatile.Read(ref _automatonCompilationCount);

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

    internal IReadOnlyList<string> GetSelectedTerms(FuzzyQuery query)
        => GetPlan(query).SelectedTerms;

    private FuzzyExpansionPlan GetPlan(FuzzyQuery query)
    {
        var key = new FuzzyPlanKey(query.Field, query.Term, query.MaxEdits, query.MaxExpansions);
        return _plans.GetOrAdd(
            key,
            _ => new Lazy<FuzzyExpansionPlan>(
                () => BuildPlan(query),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private FuzzyExpansionPlan BuildPlan(FuzzyQuery query)
    {
        string fieldPrefix = string.Concat(query.Field, "\0");
        Dictionary<SegmentReader, List<(string Term, long Offset, int Distance)>>? matchesByReader = null;
        Dictionary<string, FuzzyCandidate>? candidatesByTerm = null;

        foreach (var reader in readers)
        {
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
                    candidatesByTerm.Add(
                        match.Term,
                        new FuzzyCandidate(match.Term, match.Distance, Encoding.UTF8.GetBytes(match.Term)));
                }
            }
        }

        if (candidatesByTerm is null || matchesByReader is null)
            return FuzzyExpansionPlan.Empty;

        var candidates = candidatesByTerm.Values.ToList();
        candidates.Sort(FuzzyCandidateComparer.Instance);
        if (candidates.Count > query.MaxExpansions)
            candidates.RemoveRange(query.MaxExpansions, candidates.Count - query.MaxExpansions);

        var selectedTerms = candidates.Select(static candidate => candidate.Term).ToArray();
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
        return _automata.GetOrAdd(
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
    private readonly record struct FuzzyCandidate(string Term, int Distance, byte[] Utf8Term);

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
                : left.Utf8Term.AsSpan().SequenceCompareTo(right.Utf8Term);
        }
    }
}
