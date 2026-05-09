namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Italian Snowball-inspired stemmer. Handles common Italian inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class ItalianStemmer : IStemmer
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
        len = RemoveSuffix(buf, len, "azioni", "")
           ?? RemoveSuffix(buf, len, "azione", "")
           ?? RemoveSuffix(buf, len, "amenti", "")
           ?? RemoveSuffix(buf, len, "amento", "")
           ?? RemoveSuffix(buf, len, "imenti", "")
           ?? RemoveSuffix(buf, len, "imento", "")
           ?? RemoveSuffix(buf, len, "ità", "")
           ?? RemoveSuffix(buf, len, "mente", "")
           ?? RemoveSuffix(buf, len, "ismi", "")
           ?? RemoveSuffix(buf, len, "ismo", "")
           ?? RemoveSuffix(buf, len, "isti", "")
           ?? RemoveSuffix(buf, len, "ista", "")
           ?? RemoveSuffix(buf, len, "ibili", "")
           ?? RemoveSuffix(buf, len, "ibile", "")
           ?? RemoveSuffix(buf, len, "abili", "")
           ?? RemoveSuffix(buf, len, "abile", "")
           ?? buf.Length;

        // Step 2: Verb endings — infinitive, gerund, past participle
        len = RemoveSuffix(buf, len, "andosi", "")
           ?? RemoveSuffix(buf, len, "endosi", "")
           ?? RemoveSuffix(buf, len, "ando", "")
           ?? RemoveSuffix(buf, len, "endo", "")
           ?? RemoveSuffix(buf, len, "arono", "")
           ?? RemoveSuffix(buf, len, "erono", "")
           ?? RemoveSuffix(buf, len, "irono", "")
           ?? RemoveSuffix(buf, len, "ati", "")
           ?? RemoveSuffix(buf, len, "ute", "")
           ?? RemoveSuffix(buf, len, "uti", "")
           ?? RemoveSuffix(buf, len, "ite", "")
           ?? RemoveSuffix(buf, len, "iti", "")
           ?? RemoveSuffix(buf, len, "ate", "")
           ?? RemoveSuffix(buf, len, "ato", "")
           ?? RemoveSuffix(buf, len, "uta", "")
           ?? RemoveSuffix(buf, len, "uto", "")
           ?? RemoveSuffix(buf, len, "ita", "")
           ?? RemoveSuffix(buf, len, "ito", "")
           ?? RemoveSuffix(buf, len, "avano", "")
           ?? RemoveSuffix(buf, len, "evano", "")
           ?? RemoveSuffix(buf, len, "ivano", "")
           ?? RemoveSuffix(buf, len, "anno", "")
           ?? RemoveSuffix(buf, len, "erei", "")
           ?? RemoveSuffix(buf, len, "irei", "")
           ?? RemoveSuffix(buf, len, "arsi", "")
           ?? RemoveSuffix(buf, len, "ersi", "")
           ?? RemoveSuffix(buf, len, "irsi", "")
           ?? RemoveSuffix(buf, len, "are", "")
           ?? RemoveSuffix(buf, len, "ere", "")
           ?? RemoveSuffix(buf, len, "ire", "")
           ?? len;

        // Step 3: Noun/adjective gender & number
        len = RemoveSuffix(buf, len, "osi", "")
           ?? RemoveSuffix(buf, len, "ose", "")
           ?? RemoveSuffix(buf, len, "osi", "")
           ?? RemoveSuffix(buf, len, "i", "")
           ?? RemoveSuffix(buf, len, "e", "")
           ?? RemoveSuffix(buf, len, "a", "")
           ?? RemoveSuffix(buf, len, "o", "")
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
