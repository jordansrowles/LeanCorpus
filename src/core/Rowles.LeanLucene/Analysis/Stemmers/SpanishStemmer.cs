namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Spanish Snowball-inspired stemmer. Handles common Spanish inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input;
/// accented vowels (á, é, í, ó, ú) are treated as distinct characters.
/// </summary>
public sealed class SpanishStemmer : IStemmer
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

        // Step 1: Derivational suffixes (longest match first)
        len = RemoveSuffix(buf, len, "amientos", "")
           ?? RemoveSuffix(buf, len, "imientos", "")
           ?? RemoveSuffix(buf, len, "amiento", "")
           ?? RemoveSuffix(buf, len, "imiento", "")
           ?? RemoveSuffix(buf, len, "aciones", "")
           ?? RemoveSuffix(buf, len, "ución", "")
           ?? RemoveSuffix(buf, len, "uciones", "")
           ?? RemoveSuffix(buf, len, "ación", "")
           ?? RemoveSuffix(buf, len, "idades", "")
           ?? RemoveSuffix(buf, len, "idad", "")
           ?? RemoveSuffix(buf, len, "mente", "")
           ?? RemoveSuffix(buf, len, "ismos", "")
           ?? RemoveSuffix(buf, len, "ismo", "")
           ?? RemoveSuffix(buf, len, "istas", "")
           ?? RemoveSuffix(buf, len, "ista", "")
           ?? RemoveSuffix(buf, len, "ibles", "")
           ?? RemoveSuffix(buf, len, "ible", "")
           ?? RemoveSuffix(buf, len, "ables", "")
           ?? RemoveSuffix(buf, len, "able", "")
           ?? buf.Length;

        // Step 2: Verb endings — present/past participle, infinitive, gerund
        len = RemoveSuffix(buf, len, "ándose", "")
           ?? RemoveSuffix(buf, len, "iéndose", "")
           ?? RemoveSuffix(buf, len, "ándome", "")
           ?? RemoveSuffix(buf, len, "ando", "")
           ?? RemoveSuffix(buf, len, "iendo", "")
           ?? RemoveSuffix(buf, len, "aron", "")
           ?? RemoveSuffix(buf, len, "ieron", "")
           ?? RemoveSuffix(buf, len, "adas", "")
           ?? RemoveSuffix(buf, len, "idas", "")
           ?? RemoveSuffix(buf, len, "ados", "")
           ?? RemoveSuffix(buf, len, "idos", "")
           ?? RemoveSuffix(buf, len, "ada", "")
           ?? RemoveSuffix(buf, len, "ida", "")
           ?? RemoveSuffix(buf, len, "ado", "")
           ?? RemoveSuffix(buf, len, "ido", "")
           ?? RemoveSuffix(buf, len, "aban", "")
           ?? RemoveSuffix(buf, len, "ían", "")
           ?? RemoveSuffix(buf, len, "arán", "")
           ?? RemoveSuffix(buf, len, "erán", "")
           ?? RemoveSuffix(buf, len, "irán", "")
           ?? RemoveSuffix(buf, len, "aron", "")
           ?? RemoveSuffix(buf, len, "aré", "")
           ?? RemoveSuffix(buf, len, "eré", "")
           ?? RemoveSuffix(buf, len, "iré", "")
           ?? RemoveSuffix(buf, len, "amos", "")
           ?? RemoveSuffix(buf, len, "emos", "")
           ?? RemoveSuffix(buf, len, "imos", "")
           ?? RemoveSuffix(buf, len, "aban", "")
           ?? RemoveSuffix(buf, len, "abas", "")
           ?? RemoveSuffix(buf, len, "aba", "")
           ?? RemoveSuffix(buf, len, "ías", "")
           ?? RemoveSuffix(buf, len, "ía", "")
           ?? RemoveSuffix(buf, len, "ar", "")
           ?? RemoveSuffix(buf, len, "er", "")
           ?? RemoveSuffix(buf, len, "ir", "")
           ?? len;

        // Step 3: Remove gender/number suffixes
        len = RemoveSuffix(buf, len, "os", "")
           ?? RemoveSuffix(buf, len, "as", "")
           ?? RemoveSuffix(buf, len, "es", "")
           ?? RemoveSuffix(buf, len, "o", "")
           ?? RemoveSuffix(buf, len, "a", "")
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
