namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Consumes span-backed tokens synchronously without requiring token text strings.
/// </summary>
public interface ISpanTokenSink
{
    /// <summary>
    /// Adds a token to the sink.
    /// </summary>
    /// <param name="text">The token text span. The sink must not retain this span after the call returns.</param>
    /// <param name="startOffset">The start character offset in the original input.</param>
    /// <param name="endOffset">The exclusive end character offset in the original input.</param>
    /// <param name="type">The token type.</param>
    /// <param name="positionIncrement">The position increment relative to the previous emitted token.</param>
    /// <param name="payload">The optional payload bytes for this token.</param>
    void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null);

    /// <summary>
    /// Adds a token edge with an explicit position length.
    /// </summary>
    /// <remarks>
    /// Implementations of the legacy overload receive a length of one. New sinks that
    /// consume token graphs must implement this overload so graph information is not lost.
    /// </remarks>
    /// <param name="text">The token text span.</param>
    /// <param name="startOffset">The start character offset.</param>
    /// <param name="endOffset">The exclusive end character offset.</param>
    /// <param name="type">The token type.</param>
    /// <param name="positionIncrement">The relative token start position.</param>
    /// <param name="positionLength">The positive number of positions spanned by the token.</param>
    /// <param name="payload">The optional payload bytes.</param>
    void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        int positionLength,
        byte[]? payload)
    {
        Token.ValidatePositionLength(positionLength);
        Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
