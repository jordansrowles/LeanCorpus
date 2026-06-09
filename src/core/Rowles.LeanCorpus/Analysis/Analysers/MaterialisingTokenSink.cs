namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// An <see cref="ISpanTokenSink"/> that materialises span-backed tokens into a <see cref="List{Token}"/>.
/// Use when the caller needs random access to tokens after analysis (e.g. highlighting, query parsing).
/// </summary>
public sealed class MaterialisingTokenSink : ISpanTokenSink
{
    /// <summary>Gets the materialised token list. Cleared on each <see cref="Reset"/>.</summary>
    public List<Token> Tokens { get; } = [];

    /// <summary>Clears the token list for reuse.</summary>
    public void Reset() => Tokens.Clear();

    /// <inheritdoc/>
    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }
}
