namespace Rowles.LeanLucene.Analysis.Tokenisers;

using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Slices input text into tokens at word boundaries, splitting on
/// whitespace and punctuation whilst tracking character offsets.
/// </summary>
public sealed class Tokeniser : ITokeniser
{
    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            // Skip non-letter/digit characters (whitespace, punctuation, etc.)
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            // Start of a token
            int start = i;
            while (i < input.Length && char.IsLetterOrDigit(input[i]))
            {
                i++;
            }

            tokens.Add(new Token(input[start..i].ToString(), start, i));
        }

        return tokens;
    }

    /// <summary>
    /// Emits token offsets without allocating any strings.
    /// Used by <see cref="StandardAnalyser"/> to defer string materialisation until after filtering.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="offsets">The list to populate with <c>(Start, End)</c> character offset pairs. Cleared before use.</param>
    public void TokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;

        while (i < input.Length)
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && char.IsLetterOrDigit(input[i]))
                i++;

            offsets.Add((start, i));
        }
    }
}
