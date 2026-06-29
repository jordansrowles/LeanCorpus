namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Token filter that supports multi-token synonym expansion using a trie-based
/// <see cref="SynonymMap"/>. Uses longest-match lookahead for multi-word synonyms
/// and inserts replacement tokens at the same position offsets.
/// </summary>
/// <remarks>
/// Replaces the simpler single-token synonym approach with trie-based longest-match.
/// The <see cref="ISpanTokenFilter"/> implementation passes tokens through without
/// synonym expansion because multi-token synonym expansion requires random access to
/// previous tokens and a flush mechanism that the one-at-a-time span interface cannot
/// provide. Callers that need synonym expansion should use a dedicated analyser that
/// works on the full token list.
/// </remarks>
public sealed class SynonymGraphFilter : ISpanTokenFilter
{
    private readonly SynonymMap _map;

    /// <summary>
    /// Initialises a new <see cref="SynonymGraphFilter"/> with the specified synonym map.
    /// </summary>
    /// <param name="map">The synonym map used for multi-token expansion lookups.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="map"/> is <see langword="null"/>.</exception>
    public SynonymGraphFilter(SynonymMap map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

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
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
