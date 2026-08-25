namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Token filter that supports multi-token synonym expansion using a trie-based
/// <see cref="SynonymMap"/>. Uses longest-match lookahead for multi-word synonyms
/// and inserts replacement tokens at the same position offsets.
/// </summary>
/// <remarks>
/// Buffers tokens during application and performs trie-based longest-match
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

    }

    /// <inheritdoc/>
    public void Apply(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
        int positionIncrement, int positionLength, byte[]? payload, ISpanTokenSink sink)
    {
        _buffer.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload, positionLength));
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_buffer.Count == 0)
            return;

        var input = new TokenGraph();
        foreach (var token in _buffer)
            input.Add(token);
        input.ValidateOrdered();

        var output = new TokenGraph();
        foreach (var edge in input.Edges)
            output.Add(edge.Token, edge.StartPosition);

        // Emit each configured replacement as an alternative single edge spanning
        // the matched source phrase. SynonymMap values are individual replacement
        // terms; multi-token targets are not part of its current public contract.
        int i = 0;
        while (i < _buffer.Count)
        {
            int matchLen = _map.TryMatch(_buffer, i, out var replacements);
            if (matchLen > 0)
            {
                var first = input.Edges[i];
                var last = input.Edges[i + matchLen - 1];

                for (int r = 0; r < replacements!.Length; r++)
                {
                    output.Add(replacements[r].AsSpan(), first.Token.StartOffset, last.Token.EndOffset,
                        first.Token.Type, first.StartPosition, last.EndPosition - first.StartPosition);
                }
                i += matchLen;
            }
            else
            {
                i++;
            }
        }

        output.Emit(sink);
        _buffer.Clear();
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new SynonymGraphFilter(_map);
}
