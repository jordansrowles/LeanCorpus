namespace Rowles.LeanLucene.Analysis;

/// <summary>
/// Represents a single token produced by the analysis pipeline,
/// carrying the term text and its character offsets in the original input.
/// </summary>
public readonly struct Token(string text, int startOffset, int endOffset)
{
    /// <summary>
    /// Gets the normalised term text of the token.
    /// </summary>
    public string Text { get; } = text;

    /// <summary>
    /// Gets the start character offset of the token in the original input.
    /// </summary>
    public int StartOffset { get; } = startOffset;

    /// <summary>
    /// Gets the exclusive end character offset of the token in the original input.
    /// </summary>
    public int EndOffset { get; } = endOffset;
}
