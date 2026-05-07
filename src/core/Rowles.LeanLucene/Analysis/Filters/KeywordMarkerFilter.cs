using System.Collections.Frozen;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Identifies tokens that should be treated as keywords by compatible analysers.
/// </summary>
public sealed class KeywordMarkerFilter : ITokenFilter
{
    private readonly FrozenSet<string> _keywords;

    /// <summary>
    /// Initialises a new <see cref="KeywordMarkerFilter"/> with the specified keyword set.
    /// </summary>
    /// <param name="keywords">The token texts that should bypass compatible downstream processing.</param>
    public KeywordMarkerFilter(IEnumerable<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        _keywords = keywords.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
    }

    /// <summary>
    /// Returns true when the supplied text is marked as a keyword.
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the text is a keyword.</returns>
    internal bool IsKeyword(string text) => _keywords.Contains(text);

    /// <summary>
    /// Returns true when the supplied text is marked as a keyword.
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the text is a keyword.</returns>
    internal bool IsKeyword(ReadOnlySpan<char> text)
        => _keywords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(text);
}
