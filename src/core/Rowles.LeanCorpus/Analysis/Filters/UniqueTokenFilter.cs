namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes duplicate tokens at the same position, keeping the first occurrence.
/// </summary>
/// <remarks>
/// <para>When multiple tokens land at the same position (signalled by
/// <c>positionIncrement == 0</c>), only the first instance of each distinct token
/// text is emitted. Tokens at different positions are never deduplicated against
/// each other.</para>
/// <para>Uses a per-position <see cref="HashSet{T}"/> of token text. The set is
/// cleared on <see cref="ISpanTokenFilter.Finish"/> for reuse across documents.</para>
/// </remarks>
public sealed class UniqueTokenFilter : ISpanTokenFilter
{
    private int _currentPosition;
    private HashSet<string>? _seenAtPosition;
    private bool _firstToken = true;

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

        if (_firstToken)
        {
            _currentPosition = 0;
            _firstToken = false;
        }
        else
        {
            _currentPosition += positionIncrement;
        }

        // If the position advanced, clear the per-position set.
        if (positionIncrement > 0)
        {
            _seenAtPosition?.Clear();
        }

        _seenAtPosition ??= new HashSet<string>(StringComparer.Ordinal);

        // Use a string for the hash-set lookup. The span is transient so we
        // must copy it.
        string tokenText = text.ToString();
        if (!_seenAtPosition.Add(tokenText))
            return;

        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        _seenAtPosition?.Clear();
        _firstToken = true;
        _currentPosition = 0;
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new UniqueTokenFilter();
}
