namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// French Snowball-inspired stemmer. Handles common French suffixes.
/// </summary>
public sealed class FrenchStemmer : ISpanStemmer
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

        // Step 1: Remove common suffixes
        len = RemoveSuffix(output, len, "issements", "")
           ?? RemoveSuffix(output, len, "issement", "")
           ?? RemoveSuffix(output, len, "ements", "")
           ?? RemoveSuffix(output, len, "ement", "")
           ?? RemoveSuffix(output, len, "ations", "")
           ?? RemoveSuffix(output, len, "ation", "")
           ?? RemoveSuffix(output, len, "euses", "")
           ?? RemoveSuffix(output, len, "euse", "")
           ?? RemoveSuffix(output, len, "eurs", "")
           ?? RemoveSuffix(output, len, "eur", "")
           ?? RemoveSuffix(output, len, "ités", "")
           ?? RemoveSuffix(output, len, "ité", "")
           ?? RemoveSuffix(output, len, "ives", "")
           ?? RemoveSuffix(output, len, "ive", "")
           ?? RemoveSuffix(output, len, "ifs", "")
           ?? RemoveSuffix(output, len, "if", "")
           ?? RemoveSuffix(output, len, "aux", "al")
           ?? len;

        // Step 2: Verb endings
        len = RemoveSuffix(output, len, "issent", "")
           ?? RemoveSuffix(output, len, "issons", "")
           ?? RemoveSuffix(output, len, "issez", "")
           ?? RemoveSuffix(output, len, "irent", "")
           ?? RemoveSuffix(output, len, "eront", "")
           ?? RemoveSuffix(output, len, "erons", "")
           ?? RemoveSuffix(output, len, "erez", "")
           ?? RemoveSuffix(output, len, "ent", "")
           ?? RemoveSuffix(output, len, "ons", "")
           ?? RemoveSuffix(output, len, "ez", "")
           ?? RemoveSuffix(output, len, "er", "")
           ?? RemoveSuffix(output, len, "es", "")
           ?? len;

        // Remove trailing 'e' if stem > 2 chars
        if (len > 2 && output[len - 1] == 'e')
            len--;

        return len;
    }

    private static int? RemoveSuffix(Span<char> buf, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (len < suffix.Length + 2) return null;
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
