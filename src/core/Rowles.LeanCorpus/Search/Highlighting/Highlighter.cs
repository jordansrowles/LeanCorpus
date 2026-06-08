using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Analysis.Analysers;
namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Extracts text snippets from stored fields with matching terms highlighted.
/// </summary>
public sealed class Highlighter : IHighlighter
{
    private readonly string _preTag;
    private readonly string _postTag;
    private readonly IAnalyser _analyser;

    /// <summary>Initialises a new <see cref="Highlighter"/> with the given tags and analyser.</summary>
    /// <param name="preTag">Opening highlight tag, e.g. "&lt;b&gt;" or "&lt;em&gt;".</param>
    /// <param name="postTag">Closing highlight tag, e.g. "&lt;/b&gt;" or "&lt;/em&gt;".</param>
    /// <param name="analyser">Analyser to tokenise the input text (should match the index-time analyser).</param>
    public Highlighter(string preTag = "<b>", string postTag = "</b>", IAnalyser? analyser = null)
    {
        _preTag = preTag;
        _postTag = postTag;
        _analyser = analyser ?? new StandardAnalyser();
    }

    /// <summary>
    /// Returns the best snippet from <paramref name="text"/> containing highlighted
    /// occurrences of the query terms. Returns the original text (truncated) if no matches.
    /// </summary>
    /// <param name="text">The stored field text to highlight.</param>
    /// <param name="queryTerms">Lowercased terms to highlight.</param>
    /// <param name="maxSnippetLength">Maximum character length of the returned snippet.</param>
    /// <returns>A snippet of <paramref name="text"/> with matching terms wrapped in highlight tags, with ellipsis when truncated.</returns>
    public string GetBestFragment(string text, IReadOnlySet<string> queryTerms, int maxSnippetLength = 200)
    {
        if (string.IsNullOrEmpty(text) || queryTerms.Count == 0)
            return Truncate(text, maxSnippetLength);

        var tokens = _analyser.Analyse(text.AsSpan());
        if (tokens.Count == 0)
            return Truncate(text, maxSnippetLength);

        int bestStart = 0;
        int bestEnd = Math.Min(text.Length, maxSnippetLength);
        int bestScore = 0;
        var matchingTokenIndexes = new List<int>();

        for (int i = 0; i < tokens.Count; i++)
        {
            if (queryTerms.Contains(tokens[i].Text))
                matchingTokenIndexes.Add(i);
        }

        int right = 0;
        for (int left = 0; left < matchingTokenIndexes.Count; left++)
        {
            var token = tokens[matchingTokenIndexes[left]];
            int windowStart = Math.Max(0, token.StartOffset - maxSnippetLength / 4);
            int windowEnd = Math.Min(text.Length, windowStart + maxSnippetLength);

            if (right < left)
                right = left;
            while (right < matchingTokenIndexes.Count &&
                   tokens[matchingTokenIndexes[right]].StartOffset >= windowStart &&
                   tokens[matchingTokenIndexes[right]].EndOffset <= windowEnd)
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
        }

        var sb = new System.Text.StringBuilder(
            bestEnd - bestStart + queryTerms.Count * (_preTag.Length + _postTag.Length) + 6);
        if (bestStart > 0)
            sb.Append("...");

        int lastEnd = bestStart;
        foreach (var t in tokens)
        {
            if (t.EndOffset <= bestStart)
                continue;
            if (t.StartOffset >= bestEnd)
                break;
            if (!queryTerms.Contains(t.Text))
                continue;

            if (t.StartOffset < bestStart || t.EndOffset > bestEnd || t.StartOffset < lastEnd)
                continue;

            sb.Append(text, lastEnd, t.StartOffset - lastEnd);
            sb.Append(_preTag);
            sb.Append(text, t.StartOffset, t.EndOffset - t.StartOffset);
            sb.Append(_postTag);
            lastEnd = t.EndOffset;
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

    /// <summary>Extracts query terms from a TermQuery for use with GetBestFragment.</summary>
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

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
