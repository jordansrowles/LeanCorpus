namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Represents a single token produced by the analysis pipeline, carrying the term text,
/// offsets, token-type metadata, and positional metadata.
/// </summary>
public readonly struct Token
{
    /// <summary>
    /// The default token type used for ordinary term tokens.
    /// </summary>
    public const string DefaultType = "term";

    /// <summary>
    /// Initialises a new <see cref="Token"/> with an extensible token type.
    /// </summary>
    public Token(
        string text,
        int startOffset,
        int endOffset,
        string type = DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        Text = text;
        StartOffset = startOffset;
        EndOffset = endOffset;
        Type = ValidateType(type);
        PositionIncrement = positionIncrement;
        Payload = payload;
    }

    internal static string ValidateType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Token type must be a non-empty value.", nameof(type));
        return type;
    }

    /// <summary>
    /// Gets the normalised term text of the token.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the start character offset of the token in the original input.
    /// </summary>
    public int StartOffset { get; }

    /// <summary>
    /// Gets the exclusive end character offset of the token in the original input.
    /// </summary>
    public int EndOffset { get; }

    /// <summary>
    /// Gets the token type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the position increment for this token relative to the previous emitted token.
    /// </summary>
    public int PositionIncrement { get; }

    /// <summary>
    /// Gets the optional payload bytes associated with this token position.
    /// </summary>
    public byte[]? Payload { get; }

}
