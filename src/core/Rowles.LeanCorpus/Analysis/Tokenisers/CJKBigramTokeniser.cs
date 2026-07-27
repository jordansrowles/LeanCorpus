namespace Rowles.LeanCorpus.Analysis.Tokenisers;

using Rowles.LeanCorpus.Analysis;

/// <summary>
/// Tokeniser for CJK (Chinese, Japanese, Korean) text using overlapping bigrams
/// on CJK unified ideographs. Non-CJK text is tokenised by standard word boundaries.
/// CJK ideograph runs produce overlapping 2-character tokens; single isolated
/// ideographs are emitted as unigrams.
/// </summary>
/// <remarks>
/// Hiragana, Katakana, and Hangul are not bigrammed. They are syllabaries
/// or composed syllables and are word-tokenised instead. Supplementary-plane
/// CJK ideographs (Extension B+) are supported via surrogate pair decoding.
/// </remarks>
public sealed class CJKBigramTokeniser : ISpanTokeniser
{
    /// <summary>Token type emitted for CJK ideograph tokens.</summary>
    public const string CjkType = "cjk";

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;

        while (i < input.Length)
        {
            int codePoint = CjkUnicode.DecodeCodePoint(input, i, out int charsConsumed);

            if (CjkUnicode.IsIdeograph(codePoint))
            {
                // Emit overlapping bigrams for CJK ideograph runs
                int runStart = i;
                i += charsConsumed;
                while (i < input.Length)
                {
                    int nextCp = CjkUnicode.DecodeCodePoint(input, i, out int nextChars);
                    if (!CjkUnicode.IsIdeograph(nextCp))
                        break;
                    i += nextChars;
                }
                int runEnd = i;
                // Walk the run by code point and emit overlapping bigrams.
                // Single-code-point runs emit a unigram.
                // Multi-code-point runs emit bigrams only. The previous
                // overlapping bigram already covers the last character.
                int pos = runStart;
                CjkUnicode.DecodeCodePoint(input, pos, out int firstCpLen);
                if (pos + firstCpLen >= runEnd)
                {
                    // Emit isolated ideographs as unigrams.
                    sink.Add(input[pos..(pos + firstCpLen)], pos, pos + firstCpLen, CjkType);
                }
                else
                {
                    while (pos < runEnd)
                    {
                        CjkUnicode.DecodeCodePoint(input, pos, out int cpLen);
                        int nextPos = pos + cpLen;
                        if (nextPos < runEnd)
                        {
                            CjkUnicode.DecodeCodePoint(input, nextPos, out int nextLen);
                            int endPos = nextPos + nextLen;
                            sink.Add(input[pos..endPos], pos, endPos, CjkType);
                            pos += cpLen;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            else if (char.IsLetterOrDigit(input[i]))
            {
                // Use standard word tokenisation outside ideograph runs.
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

}
