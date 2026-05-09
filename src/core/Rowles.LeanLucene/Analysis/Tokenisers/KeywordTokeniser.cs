namespace Rowles.LeanLucene.Analysis.Tokenisers;

/// <summary>
/// Treats the complete input as a single token.
/// </summary>
public sealed class KeywordTokeniser : ITokeniser
{
    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>(input.IsEmpty ? 0 : 1);
        if (!input.IsEmpty)
            tokens.Add(new Token(input.ToString(), 0, input.Length));
        return tokens;
    }
}
