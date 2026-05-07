namespace Rowles.LeanLucene.Analysis.Tokenisers;

using Rowles.LeanLucene.Analysis;

/// <summary>
/// Splits input text into raw tokens.
/// </summary>
public interface ITokeniser
{
    /// <summary>
    /// Splits the input text into a list of tokens at word boundaries.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <returns>A list of tokens with their character offsets in the original input.</returns>
    List<Token> Tokenise(ReadOnlySpan<char> input);
}
