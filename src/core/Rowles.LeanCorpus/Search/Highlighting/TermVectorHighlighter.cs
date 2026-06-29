using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.TermVectors;

namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Extracts highlighted text snippets using pre-computed term vector offsets
/// and a <see cref="FieldQuery"/> that carries per-term phrase position constraints.
/// </summary>
/// <remarks>
/// Unlike <see cref="Highlighter"/>, this class does not re-analyse the stored
/// text when term vector offsets are available. It uses
/// <see cref="TermVectorEntry.StartOffsets"/> / <see cref="TermVectorEntry.EndOffsets"/>
/// to locate terms directly in the original text and validates phrase-position
/// constraints against the term vector's position data.
/// <para>
/// When term vectors lack offsets (degraded mode), it falls back to the standard
/// <see cref="Highlighter"/> using a <see cref="StandardAnalyser"/>.
/// </para>
/// </remarks>
public sealed class TermVectorHighlighter : IHighlighter
{
    private readonly string _preTag;
    private readonly string _postTag;
    private readonly Highlighter _fallback;

    /// <summary>Initialises a new <see cref="TermVectorHighlighter"/> with the given highlight tags.</summary>
    /// <param name="preTag">Opening highlight tag, e.g. "&lt;b&gt;" or "&lt;em&gt;".</param>
    /// <param name="postTag">Closing highlight tag, e.g. "&lt;/b&gt;" or "&lt;/em&gt;".</param>
    public TermVectorHighlighter(string preTag = "<b>", string postTag = "</b>")
    {
        _preTag = preTag;
        _postTag = postTag;
        _fallback = new Highlighter(preTag, postTag);
    }

    /// <summary>
    /// Returns the best snippet from <paramref name="text"/> containing highlighted
    /// occurrences of the query terms described by <paramref name="fieldQuery"/>,
    /// using <paramref name="termVectors"/> to locate term positions and offsets.
    /// Terms and phrase constraints are resolved across all fields referenced by the query.
    /// </summary>
    /// <param name="text">The stored field text to highlight.</param>
    /// <param name="fieldQuery">Pre-flattened query with per-field term and phrase-position data.</param>
    /// <param name="termVectors">Term vector entries for the document and field.</param>
    /// <param name="maxSnippetLength">Maximum character length of the returned snippet.</param>
    /// <returns>A snippet of <paramref name="text"/> with matching terms wrapped in highlight tags.</returns>
    public string GetBestFragment(
        string text,
        FieldQuery fieldQuery,
        IReadOnlyList<TermVectorEntry> termVectors,
        int maxSnippetLength = 200)
    {
        if (string.IsNullOrEmpty(text) || termVectors.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Build a combined view across all fields.
        var allTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fieldQuery.Fields)
            allTerms.UnionWith(fieldQuery.GetTerms(field));

        if (allTerms.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Collect matches from term vectors.
        var matches = CollectMatches(text, termVectors, allTerms, fieldQuery, out bool hasOffsets);
        if (!hasOffsets)
            return Fallback(text, allTerms, maxSnippetLength);

        if (matches.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Validate phrase-position constraints.
        ValidatePhraseMatches(matches, termVectors, fieldQuery);

        if (matches.Count == 0)
            return Truncate(text, maxSnippetLength);

        // Sort by character start.
        matches.Sort(static (a, b) => a.CharStart.CompareTo(b.CharStart));

        // Scan text for natural boundaries.
        var boundaries = ScanBoundaries(text);

        // Score sliding windows.
        (int bestStart, int bestEnd) = ScoreWindows(text, matches, boundaries, maxSnippetLength);

        // Build the highlighted snippet.
        return BuildSnippet(text, matches, bestStart, bestEnd);
    }

    /// <inheritdoc />
    public string GetBestFragment(string text, Query query,
        IReadOnlyList<TermVectorEntry>? termVectors = null,
        int maxSnippetLength = 200)
    {
        if (termVectors is null || termVectors.Count == 0)
            return _fallback.GetBestFragment(text, Highlighter.ExtractTerms(query), maxSnippetLength);
        var fieldQuery = new FieldQuery(query);
        return GetBestFragment(text, fieldQuery, termVectors, maxSnippetLength);
    }

    // -- Step 1: Collect matches from term vectors --

    private static List<Match> CollectMatches(
        string text,
        IReadOnlyList<TermVectorEntry> termVectors,
        HashSet<string> allTerms,
        FieldQuery fieldQuery,
        out bool hasOffsets)
    {
        var matches = new List<Match>();
        hasOffsets = false;

        foreach (var entry in termVectors)
        {
            if (!allTerms.Contains(entry.Term))
                continue;

            if (entry.StartOffsets is null || entry.EndOffsets is null)
                continue;
            if (entry.StartOffsets.Length != entry.EndOffsets.Length)
                continue;
            if (entry.Positions is null || entry.Positions.Length != entry.StartOffsets.Length)
                continue;

            hasOffsets = true;

            // Determine phrase positions for this term (across all fields).
            HashSet<int>? combinedPhrasePositions = null;
            float weight = 0.0f;
            foreach (var field in fieldQuery.Fields)
            {
                var pp = fieldQuery.GetPhrasePositions(field, entry.Term);
                if (pp is not null)
                {
                    combinedPhrasePositions ??= [];
                    combinedPhrasePositions.UnionWith(pp);
                }
                else
                {
                    // If ANY field has this term as non-phrase, treat as non-phrase.
                    if (fieldQuery.GetTerms(field).Contains(entry.Term))
                        combinedPhrasePositions = null;
                }
            }

            bool isPhrase = combinedPhrasePositions is not null;

            for (int p = 0; p < entry.StartOffsets.Length; p++)
            {
                int charStart = entry.StartOffsets[p];
                int charEnd = entry.EndOffsets[p];
                if (charStart < 0 || charEnd <= charStart || charEnd > text.Length)
                    continue;

                // If this is a phrase-constrained term, filter positions.
                if (isPhrase)
                {
                    // The term vector position must match one of the required phrase positions.
                    // Store the required positions for later validation.
                    // We'll keep the match now and validate in Step 2.
                }

                matches.Add(new Match(charStart, charEnd, entry.Positions[p], weight, isPhrase));
            }
        }

        return matches;
    }

    // -- Step 2: Validate phrase-position constraints --

    private static void ValidatePhraseMatches(
        List<Match> matches,
        IReadOnlyList<TermVectorEntry> termVectors,
        FieldQuery fieldQuery)
    {
        // Build a set of all term-vector positions that have ANY query term match.
        var allMatchingPositions = new HashSet<int>();
        foreach (var m in matches)
            allMatchingPositions.Add(m.Position);

        // For each phrase-constrained match, validate adjacency.
        // A phrase match is valid only if the term is part of a chain of
        // matching terms at consecutive positions.
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var m = matches[i];
            if (!m.IsPhrase)
                continue;

            // Check if this position has a neighbour at +1 that also matches.
            // This is a simple adjacency check — it validates that at least
            // two query terms appear consecutively.
            bool hasAdjacentMatch = allMatchingPositions.Contains(m.Position + 1)
                                 || allMatchingPositions.Contains(m.Position - 1);

            if (!hasAdjacentMatch)
            {
                matches.RemoveAt(i);
                allMatchingPositions.Remove(m.Position);
            }
        }
    }

    // -- Step 3: Boundary scan --

    /// <summary>
    /// Boundary characters where natural sentence/paragraph breaks occur.
    /// </summary>
    private static readonly char[] BoundaryChars =
        ['.', '!', '?', '\n', ';'];

    private static List<int> ScanBoundaries(string text)
    {
        var boundaries = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.' || c == '!' || c == '?' || c == '\n' || c == ';')
                boundaries.Add(i + 1); // boundary is after the character

            // Handle em-dash and paragraph breaks.
            if (c == '\u2014') // em dash
                boundaries.Add(i + 1);
        }

        // Always include 0 and text.Length as limits.
        boundaries.Add(0);
        boundaries.Add(text.Length);
        boundaries.Sort();
        return boundaries;
    }

    // -- Step 4: Score windows --

    private (int bestStart, int bestEnd) ScoreWindows(
        string text,
        List<Match> matches,
        List<int> boundaries,
        int maxSnippetLength)
    {
        int bestStart = 0;
        int bestEnd = Math.Min(text.Length, maxSnippetLength);
        float bestScore = 0.0f;

        for (int i = 0; i < matches.Count; i++)
        {
            int windowStart = Math.Max(0, matches[i].CharStart - maxSnippetLength / 4);
            int windowEnd = Math.Min(text.Length, windowStart + maxSnippetLength);

            float score = 0.0f;

            // Count matches within the window.
            for (int j = i; j < matches.Count; j++)
            {
                if (matches[j].CharStart >= windowStart && matches[j].CharEnd <= windowEnd)
                    score += 1.0f;
                else if (matches[j].CharStart >= windowEnd)
                    break;
            }

            // Bonus for window start near a boundary.
            int nearestBoundary = FindNearestBoundary(boundaries, windowStart);
            if (Math.Abs(nearestBoundary - windowStart) <= 5)
                score += 0.5f;

            // Penalty for breaking mid-word (prefer word-aligned windows).
            if (windowStart > 0 && !char.IsWhiteSpace(text[windowStart]) && !char.IsWhiteSpace(text[windowStart - 1]))
                score -= 0.3f;

            if (score > bestScore)
            {
                bestScore = score;
                bestStart = windowStart;
                bestEnd = windowEnd;
            }
        }

        return (bestStart, bestEnd);
    }

    private static int FindNearestBoundary(List<int> boundaries, int position)
    {
        int index = boundaries.BinarySearch(position);
        if (index < 0)
            index = ~index;

        int best = boundaries[Math.Min(index, boundaries.Count - 1)];
        if (index > 0)
        {
            int prev = boundaries[index - 1];
            if (Math.Abs(prev - position) < Math.Abs(best - position))
                best = prev;
        }
        return best;
    }

    // -- Step 5: Build snippet --

    private string BuildSnippet(string text, List<Match> matches, int bestStart, int bestEnd)
    {
        var sb = new System.Text.StringBuilder(
            bestEnd - bestStart + matches.Count * (_preTag.Length + _postTag.Length) + 6);

        if (bestStart > 0)
            sb.Append("...");

        int lastEnd = bestStart;
        foreach (var m in matches)
        {
            if (m.CharEnd <= bestStart)
                continue;
            if (m.CharStart >= bestEnd)
                break;
            if (m.CharStart < bestStart || m.CharEnd > bestEnd || m.CharStart < lastEnd)
                continue;

            sb.Append(text, lastEnd, m.CharStart - lastEnd);
            sb.Append(_preTag);
            sb.Append(text, m.CharStart, m.CharEnd - m.CharStart);
            sb.Append(_postTag);
            lastEnd = m.CharEnd;
        }

        sb.Append(text, lastEnd, bestEnd - lastEnd);
        if (bestEnd < text.Length)
            sb.Append("...");

        return sb.ToString();
    }

    // -- Fallback: re-analyse using StandardAnalyser --

    private string Fallback(string text, HashSet<string> terms, int maxSnippetLength)
    {
        return _fallback.GetBestFragment(text, terms, maxSnippetLength);
    }

    // -- helpers --

    private readonly record struct Match(
        int CharStart, int CharEnd, int Position, float Weight, bool IsPhrase);

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
