namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Dutch Snowball-inspired stemmer. Handles common Dutch inflectional and
/// derivational suffixes. Expects lowercased input. Dutch vowel sequences (ij, oe,
/// eu, ui) are not decomposed here; apply normalisation upstream if needed.
/// </summary>
public sealed class DutchStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        if (word.Length <= 3) return word;

        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        word.AsSpan().CopyTo(buf);
        int len = buf.Length;

        // Step 1: Derivational suffixes
        len = RemoveSuffix(buf, len, "heden", "heid")
           ?? RemoveSuffix(buf, len, "heden", "heid")
           ?? RemoveSuffix(buf, len, "heden", "")
           ?? RemoveSuffix(buf, len, "heid", "")
           ?? RemoveSuffix(buf, len, "ingen", "")
           ?? RemoveSuffix(buf, len, "ing", "")
           ?? RemoveSuffix(buf, len, "lijk", "")
           ?? RemoveSuffix(buf, len, "baar", "")
           ?? RemoveSuffix(buf, len, "zaam", "")
           ?? RemoveSuffix(buf, len, "ster", "")
           ?? RemoveSuffix(buf, len, "achtig", "")
           ?? RemoveSuffix(buf, len, "erij", "")
           ?? RemoveSuffix(buf, len, "isme", "")
           ?? RemoveSuffix(buf, len, "ist", "")
           ?? buf.Length;

        // Step 2: Verb endings
        len = RemoveSuffix(buf, len, "enden", "")
           ?? RemoveSuffix(buf, len, "ende", "")
           ?? RemoveSuffix(buf, len, "enden", "")
           ?? RemoveSuffix(buf, len, "tten", "t")
           ?? RemoveSuffix(buf, len, "dden", "d")
           ?? RemoveSuffix(buf, len, "ten", "")
           ?? RemoveSuffix(buf, len, "den", "")
           ?? RemoveSuffix(buf, len, "tte", "t")
           ?? RemoveSuffix(buf, len, "dde", "d")
           ?? RemoveSuffix(buf, len, "te", "")
           ?? RemoveSuffix(buf, len, "de", "")
           ?? len;

        // Step 3: Plural / noun inflections
        len = RemoveSuffix(buf, len, "eren", "")
           ?? RemoveSuffix(buf, len, "eren", "")
           ?? RemoveSuffix(buf, len, "ens", "")
           ?? RemoveSuffix(buf, len, "ers", "")
           ?? RemoveSuffix(buf, len, "en", "")
           ?? RemoveSuffix(buf, len, "es", "")
           ?? RemoveSuffix(buf, len, "s", "")
           ?? len;

        // Remove trailing 'e' if stem > 2 chars
        if (len > 3 && buf[len - 1] == 'e')
            len--;

        var result = buf[..len];
        return result.SequenceEqual(word.AsSpan()) ? word : new string(result);
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
