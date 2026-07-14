namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Truncates the token stream after a fixed number of emitted tokens.
/// </summary>
public sealed class LimitTokenCountFilter : ISpanTokenFilter
{
    private readonly int _maxTokenCount;

    private int _count;
    /// <summary>
    /// Initialises a new <see cref="LimitTokenCountFilter"/>.
    /// </summary>
    /// <param name="maxTokenCount">Maximum number of tokens to retain.</param>
    public LimitTokenCountFilter(int maxTokenCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokenCount, 1);
        _maxTokenCount = maxTokenCount;
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
        if (_count < _maxTokenCount)
        {
            _count++;
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
        }
    }

    /// <inheritdoc/>
    public void Finish(ISpanTokenSink sink)
    {
        _count = 0;
    }

    /// <inheritdoc/>
    public ISpanTokenFilter Clone() => new LimitTokenCountFilter(_maxTokenCount);
}
