using System.Collections.Frozen;
using Rowles.LeanCorpus.Analysis;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes configured elided articles before straight or curly apostrophes.
/// </summary>
public sealed class ElisionFilter : ISpanTokenFilter
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

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        int apostrophe = IndexOfApostrophe(text);
        if (apostrophe <= 0 || apostrophe == text.Length - 1)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        if (!_articles.GetAlternateLookup<ReadOnlySpan<char>>().Contains(text[..apostrophe]))
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        int newStart = startOffset + apostrophe + 1;
        sink.Add(text[(apostrophe + 1)..], newStart, endOffset, type, positionIncrement, payload);
    }

    private static int IndexOfApostrophe(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '\'' or '\u2019')
                return i;
        }

        return -1;
    }
}
