namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Dutch Snowball-inspired stemmer. Handles common Dutch inflectional and
/// derivational suffixes. Expects lowercased input. Dutch vowel sequences (ij, oe,
/// eu, ui) are not decomposed here; apply normalisation upstream if needed.
/// </summary>
public sealed class DutchStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        int len = Stem(word.AsSpan(), buf);
        return len < 0 ? word : new string(buf[..len]);
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= 3)
        {
            if (output.Length < word.Length) return -1;
            word.CopyTo(output);
            return word.Length;
        }
        if (output.Length < word.Length) return -1;
        word.CopyTo(output);
        int len = word.Length;

        // Step 1: Derivational suffixes
        len = RemoveSuffix(output, len, "heden", "heid")
           ?? RemoveSuffix(output, len, "heden", "heid")
           ?? RemoveSuffix(output, len, "heden", "")
           ?? RemoveSuffix(output, len, "heid", "")
           ?? RemoveSuffix(output, len, "ingen", "")
           ?? RemoveSuffix(output, len, "ing", "")
           ?? RemoveSuffix(output, len, "lijk", "")
           ?? RemoveSuffix(output, len, "baar", "")
           ?? RemoveSuffix(output, len, "zaam", "")
           ?? RemoveSuffix(output, len, "ster", "")
           ?? RemoveSuffix(output, len, "achtig", "")
           ?? RemoveSuffix(output, len, "erij", "")
           ?? RemoveSuffix(output, len, "isme", "")
           ?? RemoveSuffix(output, len, "ist", "")
           ?? len;

        // Step 2: Verb endings
        len = RemoveSuffix(output, len, "enden", "")
           ?? RemoveSuffix(output, len, "ende", "")
           ?? RemoveSuffix(output, len, "enden", "")
           ?? RemoveSuffix(output, len, "tten", "t")
           ?? RemoveSuffix(output, len, "dden", "d")
           ?? RemoveSuffix(output, len, "ten", "")
           ?? RemoveSuffix(output, len, "den", "")
           ?? RemoveSuffix(output, len, "tte", "t")
           ?? RemoveSuffix(output, len, "dde", "d")
           ?? RemoveSuffix(output, len, "te", "")
           ?? RemoveSuffix(output, len, "de", "")
           ?? len;

        // Step 3: Plural / noun inflections
        len = RemoveSuffix(output, len, "eren", "")
           ?? RemoveSuffix(output, len, "eren", "")
           ?? RemoveSuffix(output, len, "ens", "")
           ?? RemoveSuffix(output, len, "ers", "")
           ?? RemoveSuffix(output, len, "en", "")
           ?? RemoveSuffix(output, len, "es", "")
           ?? RemoveSuffix(output, len, "s", "")
           ?? len;

        // Remove trailing 'e' if stem > 2 chars
        if (len > 3 && output[len - 1] == 'e')
            len--;

        return len;
    }

    private static int? RemoveSuffix(Span<char> buf, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (len < suffix.Length + 3) return null;
        if (!buf.Slice(len - suffix.Length, suffix.Length).SequenceEqual(suffix)) return null;
        int stemLen = len - suffix.Length;
        if (replacement.Length > 0)
        {
            replacement.CopyTo(buf[stemLen..]);
            return stemLen + replacement.Length;
        }
        return stemLen;
    }
}
