namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Matches terms using wildcard patterns (* = any chars, ? = single char).
/// A backslash escapes the next pattern character so wildcard metacharacters can be matched literally.
/// </summary>
public sealed class WildcardQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the wildcard pattern where <c>*</c> matches any characters, <c>?</c> matches one character, and backslash escapes the next character.</summary>
    public string Pattern { get; }

    /// <summary>Initialises a new <see cref="WildcardQuery"/> for the given field and pattern.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="pattern">The wildcard pattern (<c>*</c> = any chars, <c>?</c> = single char, backslash escapes the next character).</param>
    public WildcardQuery(string field, string pattern) : this(field, pattern, null)
    {
    }

    /// <summary>Initialises a query with an optional distinct matched-term expansion limit.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="pattern">The matching pattern.</param>
    /// <param name="maximumExpansions">A positive per-clause limit, or null for trusted unbounded execution.</param>
    public WildcardQuery(string field, string pattern, int? maximumExpansions)
    {
        if (maximumExpansions is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumExpansions));
        MaximumExpansions = maximumExpansions;
        Field = field;
        Pattern = pattern;
    }

    /// <summary>Gets the distinct matched-term limit, or null for trusted unbounded execution.</summary>
    public int? MaximumExpansions { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is WildcardQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
        MaximumExpansions == other.MaximumExpansions &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(WildcardQuery), Field, Pattern, MaximumExpansions));

    /// <summary>Tests whether a term matches the wildcard pattern.</summary>
    /// <remarks>
    /// <c>*</c> matches any sequence of characters and <c>?</c> matches exactly one Unicode scalar value,
    /// so a surrogate pair counts as one character. A backslash makes the next character literal, and a
    /// trailing backslash matches a literal backslash. Search uses the same interpretation on every route.
    /// </remarks>
    public static bool Matches(ReadOnlySpan<char> term, ReadOnlySpan<char> pattern)
    {
        int t = 0, p = 0;
        int starT = -1, starP = -1;

        while (t < term.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starP = ++p;
                starT = t;
                continue;
            }

            if (p < pattern.Length && TryMatchElement(term, t, pattern, p, out int termWidth, out int patternWidth))
            {
                t += termWidth;
                p += patternWidth;
                continue;
            }

            if (starP >= 0)
            {
                // Retry after the star consumes one more whole scalar value.
                p = starP;
                starT += Codecs.Fst.WildcardPattern.ScalarWidth(term, starT);
                t = starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }

    private static bool TryMatchElement(
        ReadOnlySpan<char> term, int t, ReadOnlySpan<char> pattern, int p, out int termWidth, out int patternWidth)
    {
        termWidth = Codecs.Fst.WildcardPattern.ScalarWidth(term, t);
        if (pattern[p] == '?')
        {
            patternWidth = 1;
            return true;
        }

        int literalStart = p;
        if (pattern[p] == '\\' && p + 1 < pattern.Length)
            literalStart = p + 1;

        int literalWidth = Codecs.Fst.WildcardPattern.ScalarWidth(pattern, literalStart);
        patternWidth = literalStart - p + literalWidth;
        return literalWidth == termWidth && pattern.Slice(literalStart, literalWidth).SequenceEqual(term.Slice(t, termWidth));
    }
}
