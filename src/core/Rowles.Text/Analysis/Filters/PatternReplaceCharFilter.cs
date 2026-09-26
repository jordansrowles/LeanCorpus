using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.Analysis.Filters;

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
        _pattern = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        _replacement = replacement;
    }

    /// <inheritdoc/>
    public CharFilterResult Filter(ReadOnlySpan<char> input)
    {
        string source = input.ToString();
        MatchCollection matches = _pattern.Matches(source);
        if (matches.Count == 0)
            return new CharFilterResult(source);

        var output = new CharFilterResultBuilder(source.Length);
        int sourceOffset = 0;
        foreach (Match match in matches)
        {
            output.AppendUnchanged(source.AsSpan(sourceOffset, match.Index - sourceOffset), sourceOffset);
            string replacement = match.Result(_replacement);
            output.AppendReplacement(replacement.AsSpan(), match.Index, match.Length);
            sourceOffset = match.Index + match.Length;
        }
        output.AppendUnchanged(source.AsSpan(sourceOffset), sourceOffset);
        return output.Build();
    }
}
