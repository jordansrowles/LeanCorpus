namespace Rowles.LeanLucene.Analysis.Tokenisers;

using Rowles.LeanLucene.Analysis;

/// <summary>
/// Tokeniser for CJK (Chinese, Japanese, Korean) text using overlapping bigrams.
/// Non-CJK text is tokenised by whitespace as standard. CJK characters produce
/// overlapping 2-character tokens, which is the standard approach for
/// unsegmented CJK text.
/// </summary>
public sealed class CJKBigramTokeniser : ITokeniser
{
    /// <inheritdoc/>
    public List<Token> Tokenise(ReadOnlySpan<char> input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            if (IsCJK(input[i]))
            {
                // Emit overlapping bigrams for CJK runs
                int runStart = i;
                while (i < input.Length && IsCJK(input[i]))
                    i++;
                int runEnd = i;
                int runLen = runEnd - runStart;

                if (runLen == 1)
                {
                    // Single CJK character — emit as unigram
                    tokens.Add(new Token(input.Slice(runStart, 1).ToString(), runStart, runEnd));
                }
                else
                {
                    for (int j = runStart; j < runEnd - 1; j++)
                        tokens.Add(new Token(input.Slice(j, 2).ToString(), j, j + 2));
                }
            }
            else if (char.IsLetterOrDigit(input[i]))
            {
                // Non-CJK word — standard whitespace tokenisation
                int start = i;
                while (i < input.Length && char.IsLetterOrDigit(input[i]))
                    i++;
                tokens.Add(new Token(input.Slice(start, i - start).ToString(), start, i));
            }
            else
            {
                i++; // skip whitespace/punctuation
            }
        }

        return tokens;
    }

    /// <summary>
    /// Returns true if the character is in a CJK unified ideograph range
    /// or common CJK punctuation/katakana/hiragana range.
    /// </summary>
    private static bool IsCJK(char c)
    {
        // CJK Unified Ideographs
        if (c >= '\u4E00' && c <= '\u9FFF') return true;
        // CJK Extension A
        if (c >= '\u3400' && c <= '\u4DBF') return true;
        // Hiragana
        if (c >= '\u3040' && c <= '\u309F') return true;
        // Katakana
        if (c >= '\u30A0' && c <= '\u30FF') return true;
        // Hangul Syllables
        if (c >= '\uAC00' && c <= '\uD7AF') return true;
        return false;
    }
}
