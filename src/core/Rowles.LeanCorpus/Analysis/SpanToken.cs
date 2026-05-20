namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Represents token data whose text is a span over the analysed input.
/// </summary>
/// <remarks>
/// The token text is only valid while the source input remains in scope. Use this
/// type for allocation-aware tokenisation pipelines that consume tokens immediately.
/// </remarks>
public readonly ref struct SpanToken
{
    /// <summary>
    /// Initialises a new <see cref="SpanToken"/>.
    /// </summary>
    /// <param name="text">The token text span.</param>
    /// <param name="startOffset">The start character offset in the original input.</param>
    /// <param name="endOffset">The exclusive end character offset in the original input.</param>
    /// <param name="type">The token type.</param>
    /// <param name="positionIncrement">The position increment relative to the previous emitted token.</param>
    /// <param name="payload">The optional payload bytes for this token.</param>
    public SpanToken(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        Text = text;
        StartOffset = startOffset;
        EndOffset = endOffset;
        Type = Token.ValidateType(type);
        PositionIncrement = positionIncrement;
        Payload = payload;
    }

    /// <summary>Gets the token text span.</summary>
    public ReadOnlySpan<char> Text { get; }

    /// <summary>Gets the start character offset in the original input.</summary>
    public int StartOffset { get; }

    /// <summary>Gets the exclusive end character offset in the original input.</summary>
    public int EndOffset { get; }

    /// <summary>Gets the token type.</summary>
    public string Type { get; }

    /// <summary>Gets the position increment relative to the previous emitted token.</summary>
    public int PositionIncrement { get; }

    /// <summary>Gets the optional payload bytes for this token.</summary>
    public byte[]? Payload { get; }
}
