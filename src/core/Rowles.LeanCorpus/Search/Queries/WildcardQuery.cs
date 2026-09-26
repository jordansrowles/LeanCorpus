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
    public WildcardQuery(string field, string pattern)
    {
        Field = field;
        Pattern = pattern;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is WildcardQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(WildcardQuery), Field, Pattern));

    /// <summary>Tests whether a term matches the wildcard pattern.</summary>
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

            if (p < pattern.Length && pattern[p] == '\\')
            {
                if (p + 1 < pattern.Length)
                {
                    if (pattern[p + 1] == term[t])
                    {
                        t++;
                        p += 2;
                        continue;
                    }
                }
                else if (term[t] == '\\')
                {
                    t++;
                    p++;
                    continue;
                }
            }
            else if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == term[t]))
            {
                t++;
                p++;
                continue;
            }

            if (starP >= 0)
            {
                p = starP;
                t = ++starT;
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
}
