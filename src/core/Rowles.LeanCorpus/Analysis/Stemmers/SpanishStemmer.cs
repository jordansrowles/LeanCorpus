namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Spanish Snowball-inspired stemmer. Handles common Spanish inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input;
/// accented vowels (á, é, í, ó, ú) are treated as distinct characters.
/// </summary>
public sealed class SpanishStemmer : ISpanStemmer
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

        // Step 1: Derivational suffixes (longest match first)
        len = RemoveSuffix(output, len, "amientos", "")
           ?? RemoveSuffix(output, len, "imientos", "")
           ?? RemoveSuffix(output, len, "amiento", "")
           ?? RemoveSuffix(output, len, "imiento", "")
           ?? RemoveSuffix(output, len, "aciones", "")
           ?? RemoveSuffix(output, len, "ución", "")
           ?? RemoveSuffix(output, len, "uciones", "")
           ?? RemoveSuffix(output, len, "ación", "")
           ?? RemoveSuffix(output, len, "idades", "")
           ?? RemoveSuffix(output, len, "idad", "")
           ?? RemoveSuffix(output, len, "mente", "")
           ?? RemoveSuffix(output, len, "ismos", "")
           ?? RemoveSuffix(output, len, "ismo", "")
           ?? RemoveSuffix(output, len, "istas", "")
           ?? RemoveSuffix(output, len, "ista", "")
           ?? RemoveSuffix(output, len, "ibles", "")
           ?? RemoveSuffix(output, len, "ible", "")
           ?? RemoveSuffix(output, len, "ables", "")
           ?? RemoveSuffix(output, len, "able", "")
           ?? len;

        // Step 2: Verb endings — present/past participle, infinitive, gerund
        len = RemoveSuffix(output, len, "ándose", "")
           ?? RemoveSuffix(output, len, "iéndose", "")
           ?? RemoveSuffix(output, len, "ándome", "")
           ?? RemoveSuffix(output, len, "ando", "")
           ?? RemoveSuffix(output, len, "iendo", "")
           ?? RemoveSuffix(output, len, "aron", "")
           ?? RemoveSuffix(output, len, "ieron", "")
           ?? RemoveSuffix(output, len, "adas", "")
           ?? RemoveSuffix(output, len, "idas", "")
           ?? RemoveSuffix(output, len, "ados", "")
           ?? RemoveSuffix(output, len, "idos", "")
           ?? RemoveSuffix(output, len, "ada", "")
           ?? RemoveSuffix(output, len, "ida", "")
           ?? RemoveSuffix(output, len, "ado", "")
           ?? RemoveSuffix(output, len, "ido", "")
           ?? RemoveSuffix(output, len, "aban", "")
           ?? RemoveSuffix(output, len, "ían", "")
           ?? RemoveSuffix(output, len, "arán", "")
           ?? RemoveSuffix(output, len, "erán", "")
           ?? RemoveSuffix(output, len, "irán", "")
           ?? RemoveSuffix(output, len, "aron", "")
           ?? RemoveSuffix(output, len, "aré", "")
           ?? RemoveSuffix(output, len, "eré", "")
           ?? RemoveSuffix(output, len, "iré", "")
           ?? RemoveSuffix(output, len, "amos", "")
           ?? RemoveSuffix(output, len, "emos", "")
           ?? RemoveSuffix(output, len, "imos", "")
           ?? RemoveSuffix(output, len, "aban", "")
           ?? RemoveSuffix(output, len, "abas", "")
           ?? RemoveSuffix(output, len, "aba", "")
           ?? RemoveSuffix(output, len, "ías", "")
           ?? RemoveSuffix(output, len, "ía", "")
           ?? RemoveSuffix(output, len, "ar", "")
           ?? RemoveSuffix(output, len, "er", "")
           ?? RemoveSuffix(output, len, "ir", "")
           ?? len;

        // Step 3: Remove gender/number suffixes
        len = RemoveSuffix(output, len, "os", "")
           ?? RemoveSuffix(output, len, "as", "")
           ?? RemoveSuffix(output, len, "es", "")
           ?? RemoveSuffix(output, len, "o", "")
           ?? RemoveSuffix(output, len, "a", "")
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
