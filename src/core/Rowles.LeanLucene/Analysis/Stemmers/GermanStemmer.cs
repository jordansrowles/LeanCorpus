namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// German Snowball-inspired stemmer. Handles common German inflectional and
/// derivational suffixes. Operates on lowercased input and folds umlauts
/// (ä→a, ö→o, ü→u) and ß→ss as a preliminary step, mirroring Snowball's
/// approach for German.
/// </summary>
public sealed class GermanStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        if (word.Length <= 3) return word;

        // Fold umlauts and sharp s. ß expands to "ss" so we need a slightly
        // larger buffer than the input length in the worst case.
        int maxLen = word.Length;
        for (int i = 0; i < word.Length; i++) if (word[i] == 'ß') maxLen++;

        Span<char> buf = maxLen <= 64
            ? stackalloc char[maxLen]
            : new char[maxLen];

        int len = 0;
        foreach (var ch in word)
        {
            switch (ch)
            {
                case 'ä': buf[len++] = 'a'; break;
                case 'ö': buf[len++] = 'o'; break;
                case 'ü': buf[len++] = 'u'; break;
                case 'ß': buf[len++] = 's'; buf[len++] = 's'; break;
                default: buf[len++] = ch; break;
            }
        }

        // Step 1: Derivational suffixes
        len = RemoveSuffix(buf, len, "erungen", "")
           ?? RemoveSuffix(buf, len, "erung", "")
           ?? RemoveSuffix(buf, len, "ungen", "")
           ?? RemoveSuffix(buf, len, "ung", "")
           ?? RemoveSuffix(buf, len, "heiten", "")
           ?? RemoveSuffix(buf, len, "heits", "")
           ?? RemoveSuffix(buf, len, "heit", "")
           ?? RemoveSuffix(buf, len, "keiten", "")
           ?? RemoveSuffix(buf, len, "keit", "")
           ?? RemoveSuffix(buf, len, "schaften", "")
           ?? RemoveSuffix(buf, len, "schaft", "")
           ?? RemoveSuffix(buf, len, "ismus", "")
           ?? RemoveSuffix(buf, len, "isten", "")
           ?? RemoveSuffix(buf, len, "isten", "")
           ?? RemoveSuffix(buf, len, "ist", "")
           ?? buf.Length;

        // Step 2: Adjective suffixes
        len = RemoveSuffix(buf, len, "lichen", "")
           ?? RemoveSuffix(buf, len, "liche", "")
           ?? RemoveSuffix(buf, len, "licher", "")
           ?? RemoveSuffix(buf, len, "lichem", "")
           ?? RemoveSuffix(buf, len, "liches", "")
           ?? RemoveSuffix(buf, len, "lich", "")
           ?? RemoveSuffix(buf, len, "ischen", "")
           ?? RemoveSuffix(buf, len, "ische", "")
           ?? RemoveSuffix(buf, len, "ischer", "")
           ?? RemoveSuffix(buf, len, "ischem", "")
           ?? RemoveSuffix(buf, len, "isches", "")
           ?? RemoveSuffix(buf, len, "isch", "")
           ?? RemoveSuffix(buf, len, "igen", "")
           ?? RemoveSuffix(buf, len, "ige", "")
           ?? RemoveSuffix(buf, len, "iger", "")
           ?? RemoveSuffix(buf, len, "igem", "")
           ?? RemoveSuffix(buf, len, "iges", "")
           ?? RemoveSuffix(buf, len, "ig", "")
           ?? len;

        // Step 3: Verb endings
        len = RemoveSuffix(buf, len, "test", "")
           ?? RemoveSuffix(buf, len, "etet", "")
           ?? RemoveSuffix(buf, len, "etet", "")
           ?? RemoveSuffix(buf, len, "est", "")
           ?? RemoveSuffix(buf, len, "tet", "")
           ?? RemoveSuffix(buf, len, "et", "")
           ?? RemoveSuffix(buf, len, "te", "")
           ?? RemoveSuffix(buf, len, "nd", "")
           ?? len;

        // Step 4: Noun/plural inflections
        len = RemoveSuffix(buf, len, "innen", "")
           ?? RemoveSuffix(buf, len, "erns", "")
           ?? RemoveSuffix(buf, len, "ern", "")
           ?? RemoveSuffix(buf, len, "ens", "")
           ?? RemoveSuffix(buf, len, "ers", "")
           ?? RemoveSuffix(buf, len, "en", "")
           ?? RemoveSuffix(buf, len, "em", "")
           ?? RemoveSuffix(buf, len, "es", "")
           ?? RemoveSuffix(buf, len, "er", "")
           ?? RemoveSuffix(buf, len, "e", "")
           ?? RemoveSuffix(buf, len, "s", "")
           ?? len;

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
