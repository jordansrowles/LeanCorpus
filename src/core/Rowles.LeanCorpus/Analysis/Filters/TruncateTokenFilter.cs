namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Truncates token text to a maximum character length.
/// </summary>
public sealed class TruncateTokenFilter : ISpanTokenFilter
{
    private readonly int _maxLength;

    /// <summary>
    /// Initialises a new <see cref="TruncateTokenFilter"/> with the specified maximum length.
    /// </summary>
    /// <param name="maxLength">The maximum retained token text length.</param>
    public TruncateTokenFilter(int maxLength)
    {
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        _maxLength = maxLength;
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
        if (text.Length > _maxLength)
            sink.Add(text[.._maxLength], startOffset, startOffset + _maxLength, type, positionIncrement, payload);
        else
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
