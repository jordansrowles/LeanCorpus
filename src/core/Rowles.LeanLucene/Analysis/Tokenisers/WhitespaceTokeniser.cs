namespace Rowles.LeanLucene.Analysis.Tokenisers;

/// <summary>
/// Splits input text into tokens separated only by whitespace.
/// </summary>
public sealed class WhitespaceTokeniser : ITokeniser
{
    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        TokeniseOffsets(input, tokens);
        return tokens;
    }

    /// <summary>
    /// Emits whitespace-delimited tokens into the supplied list.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="tokens">The list to populate. Cleared before use.</param>
    public void TokeniseOffsets(ReadOnlySpan<char> input, List<Token> tokens)
    {
        tokens.Clear();
        int i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            int start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;

            tokens.Add(new Token(input[start..i].ToString(), start, i));
        }
    }

    /// <summary>
    /// Emits whitespace-delimited token offsets into the supplied list without materialising token text.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="offsets">The list to populate. Cleared before use.</param>
    internal void TokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i]))
                i++;

            if (i >= input.Length)
                break;

            int start = i;
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
                i++;

            offsets.Add((start, i));
        }
    }
}
