namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Token filter that supports multi-token synonym expansion using a trie-based
/// <see cref="SynonymMap"/>. Uses longest-match lookahead for multi-word synonyms
/// and inserts replacement tokens at the same position offsets.
/// </summary>
/// <remarks>
/// Buffers tokens during <see cref="Apply"/> and performs trie-based longest-match
/// synonym expansion in <see cref="ISpanTokenFilter.Finish"/>.
/// </remarks>
public sealed class SynonymGraphFilter : ISpanTokenFilter
{
    private readonly SynonymMap _map;
    private readonly List<Token> _buffer = new();

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
        ArgumentNullException.ThrowIfNull(sink);

        // Materialise the text — the span is transient.
        _buffer.Add(new Token(
            text.ToString(),
            startOffset,
            endOffset,
            type,
            positionIncrement,
            payload));

        // Pass through unchanged; synonym expansion happens in Finish.
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_buffer.Count == 0)
            return;

        // The buffered tokens were already emitted as pass-through by Apply.
        // Now emit synonym replacements: for each position where a synonym
        // matches, inject replacement tokens at position 0 (same position).
        int i = 0;
        while (i < _buffer.Count)
        {
            int matchLen = _map.TryMatch(_buffer, i, out var replacements);
            if (matchLen > 0)
            {
                var first = _buffer[i];
                var last = _buffer[i + matchLen - 1];

                // Emit replacement tokens at the same position as the first
                // matched source token.
                for (int r = 0; r < replacements!.Length; r++)
                {
                    sink.Add(
                        replacements[r].AsSpan(),
                        first.StartOffset,
                        last.EndOffset,
                        first.Type,
                        positionIncrement: r == 0 ? 1 : 0,
                        payload: null);
                }
                i += matchLen;
            }
            else
            {
                i++;
            }
        }

        _buffer.Clear();
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new SynonymGraphFilter(_map);
}
