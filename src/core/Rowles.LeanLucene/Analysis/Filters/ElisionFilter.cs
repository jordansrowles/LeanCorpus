using System.Collections.Frozen;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Removes configured elided articles before straight or curly apostrophes.
/// </summary>
public sealed class ElisionFilter : ITokenFilter
{
    private static readonly string[] DefaultArticles =
    [
        "l", "m", "t", "qu", "n", "s", "j", "d", "c",
        "jusqu", "quoiqu", "lorsqu", "puisqu"
    ];

    private readonly FrozenSet<string> _articles;

    /// <summary>
    /// Initialises a new <see cref="ElisionFilter"/>.
    /// </summary>
    /// <param name="articles">Articles to remove, or <see langword="null"/> for the default French set.</param>
    /// <param name="ignoreCase">Whether article matching should ignore case.</param>
    public ElisionFilter(IEnumerable<string>? articles = null, bool ignoreCase = true)
    {
        _articles = (articles ?? DefaultArticles).ToFrozenSet(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            int apostrophe = IndexOfApostrophe(token.Text);
            if (apostrophe <= 0 || apostrophe == token.Text.Length - 1)
                continue;

            if (!_articles.GetAlternateLookup<ReadOnlySpan<char>>().Contains(token.Text.AsSpan(0, apostrophe)))
                continue;

            int newStart = token.StartOffset + apostrophe + 1;
            tokens[i] = new Token(token.Text[(apostrophe + 1)..], newStart, token.EndOffset);
        }
    }

    private static int IndexOfApostrophe(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\'' or '\u2019')
                return i;
        }

        return -1;
    }
}
