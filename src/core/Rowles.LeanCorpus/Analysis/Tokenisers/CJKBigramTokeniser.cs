namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;

/// <summary>
/// Tokeniser for CJK (Chinese, Japanese, Korean) text using overlapping bigrams.
/// Non-CJK text is tokenised by whitespace as standard. CJK characters produce
/// overlapping 2-character tokens, which is the standard approach for
/// unsegmented CJK text.
/// </summary>
public sealed class CJKBigramTokeniser : ISpanTokeniser
{
    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
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
                    sink.Add(input.Slice(runStart, 1), runStart, runEnd);
                }
                else
                {
                    for (int j = runStart; j < runEnd - 1; j++)
                        sink.Add(input.Slice(j, 2), j, j + 2);
                }
            }
            else if (char.IsLetterOrDigit(input[i]))
            {
                // Non-CJK word — standard whitespace tokenisation
                int start = i;
                while (i < input.Length && char.IsLetterOrDigit(input[i]))
                    i++;
                sink.Add(
                    input[start..i],
                    start,
                    i,
                    UnicodeTokenisation.ClassifyTokenType(input[start..i]));
            }
            else
            {
                i++; // skip whitespace/punctuation
            }
        }
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
