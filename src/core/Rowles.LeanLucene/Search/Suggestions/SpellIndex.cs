using System.Buffers;

namespace Rowles.LeanLucene.Search.Suggestions;

/// <summary>
/// Pre-built character-trigram inverted index for fast spelling correction.
/// Build once via <see cref="Build"/>, then call <see cref="Suggest"/> per query.
/// This mirrors Lucene.NET's SpellChecker pattern: amortise the dictionary
/// scan at build time, achieve near-constant-time lookups at query time.
/// </summary>
public sealed class SpellIndex
{
    private readonly string[] _terms;
    private readonly int[] _docFreqs;
    private readonly Dictionary<string, int[]> _trigramIndex;

    private SpellIndex(string[] terms, int[] docFreqs, Dictionary<string, int[]> trigramIndex)
    {
        _terms = terms;
        _docFreqs = docFreqs;
        _trigramIndex = trigramIndex;
    }

    /// <summary>
    /// Builds a <see cref="SpellIndex"/> for the given field by extracting all unique
    /// terms from the segment readers and constructing a trigram inverted index.
    /// This is an O(T * L) operation where T is the unique term count and L is
    /// average term length. Call once and reuse for multiple <see cref="Suggest"/> calls.
    /// </summary>
    public static SpellIndex Build(Searcher.IndexSearcher searcher, string field)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        ArgumentException.ThrowIfNullOrEmpty(field);

        var fieldPrefix = string.Concat(field, "\x00");

        // Aggregate unique terms and doc frequencies across all segments.
        var termDocFreqs = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var reader in searcher.GetSegmentReaders())
        {
            var allTerms = reader.GetAllTermsForField(fieldPrefix);
            foreach (var (qualifiedTerm, offset) in allTerms)
            {
                var bareTerm = qualifiedTerm[(field.Length + 1)..];
                int df = reader.ReadDocFreqAtOffset(offset);

                if (termDocFreqs.TryGetValue(bareTerm, out int existing))
                    termDocFreqs[bareTerm] = existing + df;
                else
                    termDocFreqs[bareTerm] = df;
            }
        }

        if (termDocFreqs.Count == 0)
            return new SpellIndex([], [], new Dictionary<string, int[]>());

        // Materialise into arrays.
        var terms = new string[termDocFreqs.Count];
        var docFreqs = new int[termDocFreqs.Count];
        int ordinal = 0;
        foreach (var (term, df) in termDocFreqs)
        {
            terms[ordinal] = term;
            docFreqs[ordinal] = df;
            ordinal++;
        }

        return new SpellIndex(terms, docFreqs, BuildTrigramIndex(terms));
    }

    /// <summary>
    /// Builds a <see cref="SpellIndex"/> for the given field from pre-collected
    /// term/docFreq pairs. Useful for testing without an <see cref="Searcher.IndexSearcher"/>.
    /// </summary>
    internal static SpellIndex BuildFromTerms(IEnumerable<(string Term, int DocFreq)> termDocFreqs)
    {
        var list = termDocFreqs.ToList();
        if (list.Count == 0)
            return new SpellIndex([], [], new Dictionary<string, int[]>());

        var terms = new string[list.Count];
        var docFreqs = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            terms[i] = list[i].Term;
            docFreqs[i] = list[i].DocFreq;
        }

        return new SpellIndex(terms, docFreqs, BuildTrigramIndex(terms));
    }

    /// <summary>
    /// Suggests corrections for the given query term. The overlap counts array
    /// is rented from <see cref="ArrayPool{T}"/> so repeated calls allocate
    /// only the small result list.
    /// </summary>
    public List<Suggestion> Suggest(string queryTerm, int maxEdits = 2, int topN = 5)
    {
        ArgumentException.ThrowIfNullOrEmpty(queryTerm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topN);

        if (_terms.Length == 0)
            return [];

        // Rent from the pool instead of allocating per query.
        int[] overlapCounts = ArrayPool<int>.Shared.Rent(_terms.Length);
        int[] touchedOrdinals = ArrayPool<int>.Shared.Rent(_terms.Length);
        int touchedCount = 0;
        try
        {
            overlapCounts.AsSpan(0, _terms.Length).Clear();

            // Use AlternateLookup for span-based trigram matching (avoids Substring allocation).
            var lookup = _trigramIndex.GetAlternateLookup<ReadOnlySpan<char>>();
            ReadOnlySpan<char> querySpan = queryTerm.AsSpan();
            int maxTrigrams = Math.Max(0, queryTerm.Length - 2);

            // Deduplicate query trigrams via packed char keys on the stack.
            // Each edit can destroy at most 3 trigrams, so the overlap
            // threshold is distinctCount - 3 * maxEdits.
            int distinctCount = 0;
            Span<long> seen = maxTrigrams <= 64
                ? stackalloc long[Math.Max(maxTrigrams, 1)]
                : new long[maxTrigrams];

            for (int i = 0; i <= queryTerm.Length - 3; i++)
            {
                var tri = querySpan.Slice(i, 3);
                long packed = ((long)tri[0] << 32) | ((long)tri[1] << 16) | tri[2];

                bool dup = false;
                for (int j = 0; j < distinctCount; j++)
                {
                    if (seen[j] == packed) { dup = true; break; }
                }
                if (dup) continue;
                seen[distinctCount++] = packed;

                if (lookup.TryGetValue(tri, out int[]? ordinals))
                {
                    foreach (int ord in ordinals)
                    {
                        if (overlapCounts[ord] == 0)
                            touchedOrdinals[touchedCount++] = ord;
                        overlapCounts[ord]++;
                    }
                }
            }

            int minOverlap = Math.Max(1, distinctCount - (3 * maxEdits));

            var results = new List<Suggestion>(Math.Min(topN * 2, 16));
            int queryLen = queryTerm.Length;

            if (distinctCount > 0)
            {
                // Iterate only terms touched by at least one query trigram
                for (int t = 0; t < touchedCount; t++)
                {
                    int i = touchedOrdinals[t];
                    if (overlapCounts[i] < minOverlap)
                        continue;

                    var candidate = _terms[i];

                    if (string.Equals(candidate, queryTerm, StringComparison.Ordinal))
                        continue;

                    if (Math.Abs(candidate.Length - queryLen) > maxEdits)
                        continue;

                    int distance = LevenshteinDistance.ComputeBounded(querySpan, candidate.AsSpan(), maxEdits);
                    if (distance > maxEdits)
                        continue;

                    float score = _docFreqs[i] / (1f + distance);
                    results.Add(new Suggestion(candidate, distance, _docFreqs[i], score));
                }
            }
            else
            {
                // No trigrams (short query): fall back to full scan with length filter
                for (int i = 0; i < _terms.Length; i++)
                {
                    var candidate = _terms[i];

                    if (string.Equals(candidate, queryTerm, StringComparison.Ordinal))
                        continue;

                    if (Math.Abs(candidate.Length - queryLen) > maxEdits)
                        continue;

                    int distance = LevenshteinDistance.ComputeBounded(querySpan, candidate.AsSpan(), maxEdits);
                    if (distance > maxEdits)
                        continue;

                    float score = _docFreqs[i] / (1f + distance);
                    results.Add(new Suggestion(candidate, distance, _docFreqs[i], score));
                }
            }

            results.Sort(static (a, b) => b.Score.CompareTo(a.Score));
            if (results.Count > topN)
                results.RemoveRange(topN, results.Count - topN);

            return results;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(overlapCounts);
            ArrayPool<int>.Shared.Return(touchedOrdinals);
        }
    }

    /// <summary>The number of unique terms in this index.</summary>
    public int TermCount => _terms.Length;

    /// <summary>
    /// Builds a trigram inverted index from the given term array.
    /// Each trigram maps to a compacted <c>int[]</c> of term ordinals.
    /// </summary>
    private static Dictionary<string, int[]> BuildTrigramIndex(string[] terms)
    {
        var builder = new Dictionary<string, List<int>>(terms.Length * 3, StringComparer.Ordinal);

        for (int i = 0; i < terms.Length; i++)
        {
            var term = terms[i];
            for (int j = 0; j <= term.Length - 3; j++)
            {
                var trigram = term.Substring(j, 3);
                if (!builder.TryGetValue(trigram, out var ordinals))
                {
                    ordinals = new List<int>();
                    builder[trigram] = ordinals;
                }
                ordinals.Add(i);
            }
        }

        // Compact List<int> to int[] to shed per-list overhead.
        var index = new Dictionary<string, int[]>(builder.Count, StringComparer.Ordinal);
        foreach (var (trigram, ordinals) in builder)
            index[trigram] = [.. ordinals];

        return index;
    }
}
