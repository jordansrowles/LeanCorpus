namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Portuguese Snowball-inspired stemmer. Handles common Portuguese inflectional
/// and derivational suffixes. Covers both European (pt-PT) and Brazilian (pt-BR)
/// variants. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class PortugueseStemmer : IStemmer
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
        len = RemoveSuffix(buf, len, "amentos", "")
           ?? RemoveSuffix(buf, len, "amentos", "")
           ?? RemoveSuffix(buf, len, "imento", "")
           ?? RemoveSuffix(buf, len, "amentos", "")
           ?? RemoveSuffix(buf, len, "amento", "")
           ?? RemoveSuffix(buf, len, "imentos", "")
           ?? RemoveSuffix(buf, len, "imento", "")
           ?? RemoveSuffix(buf, len, "ações", "")
           ?? RemoveSuffix(buf, len, "ação", "")
           ?? RemoveSuffix(buf, len, "idades", "")
           ?? RemoveSuffix(buf, len, "idade", "")
           ?? RemoveSuffix(buf, len, "mente", "")
           ?? RemoveSuffix(buf, len, "ismos", "")
           ?? RemoveSuffix(buf, len, "ismo", "")
           ?? RemoveSuffix(buf, len, "istas", "")
           ?? RemoveSuffix(buf, len, "ista", "")
           ?? RemoveSuffix(buf, len, "áveis", "")
           ?? RemoveSuffix(buf, len, "ável", "")
           ?? RemoveSuffix(buf, len, "íveis", "")
           ?? RemoveSuffix(buf, len, "ível", "")
           ?? buf.Length;

        // Step 2: Verb endings
        len = RemoveSuffix(buf, len, "ando", "")
           ?? RemoveSuffix(buf, len, "endo", "")
           ?? RemoveSuffix(buf, len, "indo", "")
           ?? RemoveSuffix(buf, len, "aram", "")
           ?? RemoveSuffix(buf, len, "eram", "")
           ?? RemoveSuffix(buf, len, "iram", "")
           ?? RemoveSuffix(buf, len, "adas", "")
           ?? RemoveSuffix(buf, len, "idas", "")
           ?? RemoveSuffix(buf, len, "ados", "")
           ?? RemoveSuffix(buf, len, "idos", "")
           ?? RemoveSuffix(buf, len, "ada", "")
           ?? RemoveSuffix(buf, len, "ida", "")
           ?? RemoveSuffix(buf, len, "ado", "")
           ?? RemoveSuffix(buf, len, "ido", "")
           ?? RemoveSuffix(buf, len, "avam", "")
           ?? RemoveSuffix(buf, len, "avam", "")
           ?? RemoveSuffix(buf, len, "amos", "")
           ?? RemoveSuffix(buf, len, "emos", "")
           ?? RemoveSuffix(buf, len, "imos", "")
           ?? RemoveSuffix(buf, len, "ava", "")
           ?? RemoveSuffix(buf, len, "ias", "")
           ?? RemoveSuffix(buf, len, "ia", "")
           ?? RemoveSuffix(buf, len, "ar", "")
           ?? RemoveSuffix(buf, len, "er", "")
           ?? RemoveSuffix(buf, len, "ir", "")
           ?? len;

        // Step 3: Residual noun/adjective endings
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
