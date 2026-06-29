namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Italian Snowball-inspired stemmer. Handles common Italian inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class ItalianStemmer : ISpanStemmer
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
        len = RemoveSuffix(output, len, "azioni", "")
           ?? RemoveSuffix(output, len, "azione", "")
           ?? RemoveSuffix(output, len, "amenti", "")
           ?? RemoveSuffix(output, len, "amento", "")
           ?? RemoveSuffix(output, len, "imenti", "")
           ?? RemoveSuffix(output, len, "imento", "")
           ?? RemoveSuffix(output, len, "ità", "")
           ?? RemoveSuffix(output, len, "mente", "")
           ?? RemoveSuffix(output, len, "ismi", "")
           ?? RemoveSuffix(output, len, "ismo", "")
           ?? RemoveSuffix(output, len, "isti", "")
           ?? RemoveSuffix(output, len, "ista", "")
           ?? RemoveSuffix(output, len, "ibili", "")
           ?? RemoveSuffix(output, len, "ibile", "")
           ?? RemoveSuffix(output, len, "abili", "")
           ?? RemoveSuffix(output, len, "abile", "")
           ?? len;

        // Step 2: Verb endings — infinitive, gerund, past participle
        len = RemoveSuffix(output, len, "andosi", "")
           ?? RemoveSuffix(output, len, "endosi", "")
           ?? RemoveSuffix(output, len, "ando", "")
           ?? RemoveSuffix(output, len, "endo", "")
           ?? RemoveSuffix(output, len, "arono", "")
           ?? RemoveSuffix(output, len, "erono", "")
           ?? RemoveSuffix(output, len, "irono", "")
           ?? RemoveSuffix(output, len, "ati", "")
           ?? RemoveSuffix(output, len, "ute", "")
           ?? RemoveSuffix(output, len, "uti", "")
           ?? RemoveSuffix(output, len, "ite", "")
           ?? RemoveSuffix(output, len, "iti", "")
           ?? RemoveSuffix(output, len, "ate", "")
           ?? RemoveSuffix(output, len, "ato", "")
           ?? RemoveSuffix(output, len, "uta", "")
           ?? RemoveSuffix(output, len, "uto", "")
           ?? RemoveSuffix(output, len, "ita", "")
           ?? RemoveSuffix(output, len, "ito", "")
           ?? RemoveSuffix(output, len, "avano", "")
           ?? RemoveSuffix(output, len, "evano", "")
           ?? RemoveSuffix(output, len, "ivano", "")
           ?? RemoveSuffix(output, len, "anno", "")
           ?? RemoveSuffix(output, len, "erei", "")
           ?? RemoveSuffix(output, len, "irei", "")
           ?? RemoveSuffix(output, len, "arsi", "")
           ?? RemoveSuffix(output, len, "ersi", "")
           ?? RemoveSuffix(output, len, "irsi", "")
           ?? RemoveSuffix(output, len, "are", "")
           ?? RemoveSuffix(output, len, "ere", "")
           ?? RemoveSuffix(output, len, "ire", "")
           ?? len;

        // Step 3: Noun/adjective gender & number
        len = RemoveSuffix(output, len, "osi", "")
           ?? RemoveSuffix(output, len, "ose", "")
           ?? RemoveSuffix(output, len, "osi", "")
           ?? RemoveSuffix(output, len, "i", "")
           ?? RemoveSuffix(output, len, "e", "")
           ?? RemoveSuffix(output, len, "a", "")
           ?? RemoveSuffix(output, len, "o", "")
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
