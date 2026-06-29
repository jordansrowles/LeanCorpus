namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// German Snowball-inspired stemmer. Handles common German inflectional and
/// derivational suffixes. Operates on lowercased input and folds umlauts
/// (ä→a, ö→o, ü→u) and ß→ss as a preliminary step, mirroring Snowball's
/// approach for German.
/// </summary>
public sealed class GermanStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        // Fold umlauts and sharp s. ß expands to "ss" so we need a slightly
        // larger buffer than the input length in the worst case.
        int maxLen = word.Length;
        for (int i = 0; i < word.Length; i++) if (word[i] == 'ß') maxLen++;

        Span<char> buf = maxLen <= 64
            ? stackalloc char[maxLen]
            : new char[maxLen];

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

        int maxLen = word.Length;
        for (int i = 0; i < word.Length; i++) if (word[i] == 'ß') maxLen++;

        if (output.Length < maxLen) return -1;

        int len = 0;
        foreach (var ch in word)
        {
            switch (ch)
            {
                case 'ä': output[len++] = 'a'; break;
                case 'ö': output[len++] = 'o'; break;
                case 'ü': output[len++] = 'u'; break;
                case 'ß': output[len++] = 's'; output[len++] = 's'; break;
                default: output[len++] = ch; break;
            }
        }

        // Step 1: Derivational suffixes
        len = RemoveSuffix(output, len, "erungen", "")
           ?? RemoveSuffix(output, len, "erung", "")
           ?? RemoveSuffix(output, len, "ungen", "")
           ?? RemoveSuffix(output, len, "ung", "")
           ?? RemoveSuffix(output, len, "heiten", "")
           ?? RemoveSuffix(output, len, "heits", "")
           ?? RemoveSuffix(output, len, "heit", "")
           ?? RemoveSuffix(output, len, "keiten", "")
           ?? RemoveSuffix(output, len, "keit", "")
           ?? RemoveSuffix(output, len, "schaften", "")
           ?? RemoveSuffix(output, len, "schaft", "")
           ?? RemoveSuffix(output, len, "ismus", "")
           ?? RemoveSuffix(output, len, "isten", "")
           ?? RemoveSuffix(output, len, "isten", "")
           ?? RemoveSuffix(output, len, "ist", "")
           ?? len;

        // Step 2: Adjective suffixes
        len = RemoveSuffix(output, len, "lichen", "")
           ?? RemoveSuffix(output, len, "liche", "")
           ?? RemoveSuffix(output, len, "licher", "")
           ?? RemoveSuffix(output, len, "lichem", "")
           ?? RemoveSuffix(output, len, "liches", "")
           ?? RemoveSuffix(output, len, "lich", "")
           ?? RemoveSuffix(output, len, "ischen", "")
           ?? RemoveSuffix(output, len, "ische", "")
           ?? RemoveSuffix(output, len, "ischer", "")
           ?? RemoveSuffix(output, len, "ischem", "")
           ?? RemoveSuffix(output, len, "isches", "")
           ?? RemoveSuffix(output, len, "isch", "")
           ?? RemoveSuffix(output, len, "igen", "")
           ?? RemoveSuffix(output, len, "ige", "")
           ?? RemoveSuffix(output, len, "iger", "")
           ?? RemoveSuffix(output, len, "igem", "")
           ?? RemoveSuffix(output, len, "iges", "")
           ?? RemoveSuffix(output, len, "ig", "")
           ?? len;

        // Step 3: Verb endings
        len = RemoveSuffix(output, len, "test", "")
           ?? RemoveSuffix(output, len, "etet", "")
           ?? RemoveSuffix(output, len, "etet", "")
           ?? RemoveSuffix(output, len, "est", "")
           ?? RemoveSuffix(output, len, "tet", "")
           ?? RemoveSuffix(output, len, "et", "")
           ?? RemoveSuffix(output, len, "te", "")
           ?? RemoveSuffix(output, len, "nd", "")
           ?? len;

        // Step 4: Noun/plural inflections
        len = RemoveSuffix(output, len, "innen", "")
           ?? RemoveSuffix(output, len, "erns", "")
           ?? RemoveSuffix(output, len, "ern", "")
           ?? RemoveSuffix(output, len, "ens", "")
           ?? RemoveSuffix(output, len, "ers", "")
           ?? RemoveSuffix(output, len, "en", "")
           ?? RemoveSuffix(output, len, "em", "")
           ?? RemoveSuffix(output, len, "es", "")
           ?? RemoveSuffix(output, len, "er", "")
           ?? RemoveSuffix(output, len, "e", "")
           ?? RemoveSuffix(output, len, "s", "")
           ?? len;

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
