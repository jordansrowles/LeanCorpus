using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.TermVectors;

namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Extracts highlighted text snippets using term vector offsets from the index.
/// Unlike <see cref="Highlighter"/>, this does not re-analyse the stored text;
/// it uses pre-computed start/end character offsets from term vectors to locate
/// query terms in the original field text.
/// </summary>
public sealed class PostingsHighlighter
{
    private readonly string _preTag;
    private readonly string _postTag;

    /// <summary>Initialises a new <see cref="PostingsHighlighter"/> with the given highlight tags.</summary>
    /// <param name="preTag">Opening highlight tag, e.g. "&lt;b&gt;" or "&lt;em&gt;".</param>
    /// <param name="postTag">Closing highlight tag, e.g. "&lt;/b&gt;" or "&lt;/em&gt;".</param>
    public PostingsHighlighter(string preTag = "<b>", string postTag = "</b>")
    {
        _preTag = preTag;
        _postTag = postTag;
    }

    /// <summary>
    /// Returns the best snippet from <paramref name="text"/> containing highlighted
    /// occurrences of the query terms, using term vector offsets to locate terms.
    /// </summary>
    /// <param name="text">The stored field text to highlight.</param>
    /// <param name="termVectors">Term vector entries for the document and field, containing start/end offsets.</param>
    /// <param name="queryTerms">Lowercased terms to highlight.</param>
    /// <param name="maxSnippetLength">Maximum character length of the returned snippet.</param>
    /// <returns>A snippet of <paramref name="text"/> with matching terms wrapped in highlight tags.</returns>
    public string GetBestFragment(string text, IReadOnlyList<TermVectorEntry> termVectors,
        IReadOnlySet<string> queryTerms, int maxSnippetLength = 200)
    {
        if (string.IsNullOrEmpty(text) || queryTerms.Count == 0 || termVectors.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Collect all matching (startOffset, endOffset) pairs from term vectors.
        var matchingOffsets = new List<(int Start, int End)>();
        foreach (var entry in termVectors)
        {
            if (!queryTerms.Contains(entry.Term)) continue;
            if (entry.StartOffsets is null || entry.EndOffsets is null) continue;
            if (entry.StartOffsets.Length != entry.EndOffsets.Length) continue;

            for (int p = 0; p < entry.StartOffsets.Length; p++)
            {
                int start = entry.StartOffsets[p];
                int end = entry.EndOffsets[p];
                if (start >= 0 && end > start && end <= text.Length)
                    matchingOffsets.Add((start, end));
            }
        }

        if (matchingOffsets.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Sort by start offset.
        matchingOffsets.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        // Find the best window containing the most matches within maxSnippetLength.
        int bestStart = 0;
        int bestEnd = Math.Min(text.Length, maxSnippetLength);
        int bestScore = 0;

        int right = 0;
        for (int left = 0; left < matchingOffsets.Count; left++)
        {
            int windowStart = Math.Max(0, matchingOffsets[left].Start - maxSnippetLength / 4);
            int windowEnd = Math.Min(text.Length, windowStart + maxSnippetLength);

            if (right < left)
                right = left;
            while (right < matchingOffsets.Count &&
                   matchingOffsets[right].Start >= windowStart &&
                   matchingOffsets[right].End <= windowEnd)
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

        // Build the highlighted snippet.
        var sb = new System.Text.StringBuilder(
            bestEnd - bestStart + matchingOffsets.Count * (_preTag.Length + _postTag.Length) + 6);
        if (bestStart > 0)
            sb.Append("...");

        int lastEnd = bestStart;
        foreach (var (start, end) in matchingOffsets)
        {
            if (end <= bestStart) continue;
            if (start >= bestEnd) break;
            if (start < bestStart || end > bestEnd || start < lastEnd)
                continue;

            sb.Append(text, lastEnd, start - lastEnd);
            sb.Append(_preTag);
            sb.Append(text, start, end - start);
            sb.Append(_postTag);
            lastEnd = end;
        }
        sb.Append(text, lastEnd, bestEnd - lastEnd);
        if (bestEnd < text.Length)
            sb.Append("...");

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
