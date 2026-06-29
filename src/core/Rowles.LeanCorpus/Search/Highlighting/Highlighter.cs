using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Extracts text snippets from stored fields with matching terms highlighted.
/// Query term occurrences are located directly in the text via case-insensitive
/// substring search with word-boundary validation, avoiding the cost of full
/// re-analysis through the standard analyser pipeline.
/// </summary>
public sealed class Highlighter : IHighlighter
{
    private readonly string _preTag;
    private readonly string _postTag;
    private readonly StopWordFilter _stopWordFilter;
    private readonly System.Text.StringBuilder _stringBuilder = new();

    /// <summary>Initialises a new <see cref="Highlighter"/> with the given tags.</summary>
    /// <param name="preTag">Opening highlight tag, e.g. "&lt;b&gt;" or "&lt;em&gt;".</param>
    /// <param name="postTag">Closing highlight tag, e.g. "&lt;/b&gt;" or "&lt;/em&gt;".</param>
    /// <param name="analyser">
    /// Retained for binary compatibility; no longer used. Highlighting now locates
    /// query terms directly in the stored text without re-analysing.
    /// </param>
    public Highlighter(string preTag = "<b>", string postTag = "</b>", IAnalyser? analyser = null)
    {
        _ = analyser; // retained for binary compatibility, no longer consumed
        _preTag = preTag;
        _postTag = postTag;
        _stopWordFilter = new StopWordFilter(null);
    }

    /// <summary>
    /// Returns the best snippet from <paramref name="text"/> containing highlighted
    /// occurrences of the query terms. Returns the original text (truncated) if no matches.
    /// </summary>
    /// <param name="text">The stored field text to highlight.</param>
    /// <param name="queryTerms">Terms to highlight. Stop words are ignored.</param>
    /// <param name="maxSnippetLength">Maximum character length of the returned snippet.</param>
    /// <returns>A snippet of <paramref name="text"/> with matching terms wrapped in highlight tags, with ellipsis when truncated.</returns>
    public string GetBestFragment(string text, IReadOnlySet<string> queryTerms, int maxSnippetLength = 200)
    {
        if (string.IsNullOrEmpty(text) || queryTerms.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Scan for each query term directly in the text at word boundaries.
        var matches = new List<(int Start, int End)>();
        foreach (var term in queryTerms)
        {
            if (string.IsNullOrEmpty(term))
                continue;
            if (_stopWordFilter.IsStopWord(term.AsSpan()))
                continue;

            FindTermOccurrences(text, term, matches);
        }

        if (matches.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Sort by start offset for window scoring.
        matches.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        int bestStart = 0;
        int bestEnd = Math.Min(text.Length, maxSnippetLength);
        int bestScore = 0;

        int right = 0;
        for (int left = 0; left < matches.Count; left++)
        {
            var m = matches[left];
            int windowStart = Math.Max(0, m.Start - maxSnippetLength / 4);
            int windowEnd = Math.Min(text.Length, windowStart + maxSnippetLength);

            if (right < left)
                right = left;
            while (right < matches.Count &&
                   matches[right].Start >= windowStart &&
                   matches[right].End <= windowEnd)
            {
                right++;
            }

            int score = right - left;
            if (score > bestScore)
            {
                bestScore = score;
                bestStart = windowStart;
                bestEnd = windowEnd;
            }

            // Short-circuit: remaining matches can't exceed the current best.
            if (matches.Count - left <= bestScore)
                break;
        }

        // Build snippet.
        var sb = _stringBuilder;
        sb.Clear();
        sb.EnsureCapacity(bestEnd - bestStart + matches.Count * (_preTag.Length + _postTag.Length) + 6);
        if (bestStart > 0)
            sb.Append("...");

        int lastEnd = bestStart;
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.End <= bestStart)
                continue;
            if (m.Start >= bestEnd)
                break;
            if (m.Start < bestStart || m.End > bestEnd || m.Start < lastEnd)
                continue;

            sb.Append(text, lastEnd, m.Start - lastEnd);
            sb.Append(_preTag);
            sb.Append(text, m.Start, m.End - m.Start);
            sb.Append(_postTag);
            lastEnd = m.End;

            if (m.End >= bestEnd)
                break;
        }
        sb.Append(text, lastEnd, bestEnd - lastEnd);
        if (bestEnd < text.Length)
            sb.Append("...");

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GetBestFragment(string text, Query query,
        IReadOnlyList<TermVectorEntry>? termVectors = null,
        int maxSnippetLength = 200)
    {
        var terms = ExtractTerms(query);
        return GetBestFragment(text, terms, maxSnippetLength);
    }

    /// <summary>Extracts query terms from a query for use with GetBestFragment.</summary>
    /// <param name="query">The query from which to collect searchable terms.</param>
    /// <returns>A case-insensitive set of term strings suitable for use with one of the GetBestFragment overloads.</returns>
    public static HashSet<string> ExtractTerms(Query query)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTerms(query, terms);
        return terms;
    }

    private static void CollectTerms(Query query, HashSet<string> terms)
    {
        switch (query)
        {
            case TermQuery tq:
                terms.Add(tq.Term);
                break;
            case BooleanQuery bq:
                foreach (var clause in bq.Clauses)
                    if (clause.Occur != Occur.MustNot)
                        CollectTerms(clause.Query, terms);
                break;
            case PhraseQuery pq:
                foreach (var t in pq.Terms)
                    terms.Add(t);
                break;
            case PrefixQuery prefixQ:
                terms.Add(prefixQ.Prefix);
                break;
        }
    }

    /// <summary>
    /// Finds all occurrences of <paramref name="term"/> in <paramref name="text"/>
    /// that sit at word boundaries (preceded and followed by a non-letter-or-digit
    /// character, or the start/end of the string).
    /// </summary>
    private static void FindTermOccurrences(string text, string term, List<(int Start, int End)> results)
    {
        int searchStart = 0;
        while (searchStart < text.Length)
        {
            int idx = text.IndexOf(term, searchStart, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                break;

            bool leftBoundary = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool rightBoundary = idx + term.Length >= text.Length
                || !char.IsLetterOrDigit(text[idx + term.Length]);

            if (leftBoundary && rightBoundary)
                results.Add((idx, idx + term.Length));

            searchStart = idx + 1;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
