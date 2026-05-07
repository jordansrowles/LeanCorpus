using System.Text.RegularExpressions;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Replaces text matching a regex pattern with a replacement string.
/// </summary>
public sealed class PatternReplaceCharFilter : ICharFilter
{
    private readonly Regex _pattern;
    private readonly string _replacement;

    /// <summary>
    /// Initialises a new <see cref="PatternReplaceCharFilter"/> with the specified regex pattern and replacement.
    /// </summary>
    /// <param name="pattern">A regular expression pattern to match against the input.</param>
    /// <param name="replacement">The replacement string for matched substrings.</param>
    public PatternReplaceCharFilter(string pattern, string replacement)
    {
        _pattern = new Regex(pattern);
        _replacement = replacement;
    }

    /// <inheritdoc/>
    public string Filter(ReadOnlySpan<char> input)
        => _pattern.Replace(input.ToString(), _replacement);
}
