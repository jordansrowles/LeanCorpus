namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes tokens whose text length falls outside an inclusive range.
/// </summary>
public sealed class LengthFilter : ISpanTokenFilter
{
    private readonly int _minLength;
    private readonly int _maxLength;

    /// <summary>
    /// Initialises a new <see cref="LengthFilter"/> with inclusive minimum and maximum lengths.
    /// </summary>
    /// <param name="minLength">The minimum accepted token length.</param>
    /// <param name="maxLength">The maximum accepted token length.</param>
    public LengthFilter(int minLength, int maxLength)
    {
        if (minLength < 0)
            throw new ArgumentOutOfRangeException(nameof(minLength));
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (maxLength < minLength)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        _minLength = minLength;
        _maxLength = maxLength;
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
        int len = text.Length;
        if (len >= _minLength && len <= _maxLength)
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
