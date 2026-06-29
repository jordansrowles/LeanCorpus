namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Portuguese Snowball-inspired stemmer. Handles common Portuguese inflectional
/// and derivational suffixes. Covers both European (pt-PT) and Brazilian (pt-BR)
/// variants. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class PortugueseStemmer : ISpanStemmer
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
        len = RemoveSuffix(output, len, "amentos", "")
           ?? RemoveSuffix(output, len, "amentos", "")
           ?? RemoveSuffix(output, len, "imento", "")
           ?? RemoveSuffix(output, len, "amentos", "")
           ?? RemoveSuffix(output, len, "amento", "")
           ?? RemoveSuffix(output, len, "imentos", "")
           ?? RemoveSuffix(output, len, "imento", "")
           ?? RemoveSuffix(output, len, "ações", "")
           ?? RemoveSuffix(output, len, "ação", "")
           ?? RemoveSuffix(output, len, "idades", "")
           ?? RemoveSuffix(output, len, "idade", "")
           ?? RemoveSuffix(output, len, "mente", "")
           ?? RemoveSuffix(output, len, "ismos", "")
           ?? RemoveSuffix(output, len, "ismo", "")
           ?? RemoveSuffix(output, len, "istas", "")
           ?? RemoveSuffix(output, len, "ista", "")
           ?? RemoveSuffix(output, len, "áveis", "")
           ?? RemoveSuffix(output, len, "ável", "")
           ?? RemoveSuffix(output, len, "íveis", "")
           ?? RemoveSuffix(output, len, "ível", "")
           ?? len;

        // Step 2: Verb endings
        len = RemoveSuffix(output, len, "ando", "")
           ?? RemoveSuffix(output, len, "endo", "")
           ?? RemoveSuffix(output, len, "indo", "")
           ?? RemoveSuffix(output, len, "aram", "")
           ?? RemoveSuffix(output, len, "eram", "")
           ?? RemoveSuffix(output, len, "iram", "")
           ?? RemoveSuffix(output, len, "adas", "")
           ?? RemoveSuffix(output, len, "idas", "")
           ?? RemoveSuffix(output, len, "ados", "")
           ?? RemoveSuffix(output, len, "idos", "")
           ?? RemoveSuffix(output, len, "ada", "")
           ?? RemoveSuffix(output, len, "ida", "")
           ?? RemoveSuffix(output, len, "ado", "")
           ?? RemoveSuffix(output, len, "ido", "")
           ?? RemoveSuffix(output, len, "avam", "")
           ?? RemoveSuffix(output, len, "avam", "")
           ?? RemoveSuffix(output, len, "amos", "")
           ?? RemoveSuffix(output, len, "emos", "")
           ?? RemoveSuffix(output, len, "imos", "")
           ?? RemoveSuffix(output, len, "ava", "")
           ?? RemoveSuffix(output, len, "ias", "")
           ?? RemoveSuffix(output, len, "ia", "")
           ?? RemoveSuffix(output, len, "ar", "")
           ?? RemoveSuffix(output, len, "er", "")
           ?? RemoveSuffix(output, len, "ir", "")
           ?? len;

        // Step 3: Residual noun/adjective endings
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
