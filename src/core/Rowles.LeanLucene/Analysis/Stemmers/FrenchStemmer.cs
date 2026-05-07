namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// French Snowball-inspired stemmer. Handles common French suffixes.
/// </summary>
public sealed class FrenchStemmer : IStemmer
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

        // Step 1: Remove common suffixes
        len = RemoveSuffix(buf, len, "issements", "")
           ?? RemoveSuffix(buf, len, "issement", "")
           ?? RemoveSuffix(buf, len, "ements", "")
           ?? RemoveSuffix(buf, len, "ement", "")
           ?? RemoveSuffix(buf, len, "ations", "")
           ?? RemoveSuffix(buf, len, "ation", "")
           ?? RemoveSuffix(buf, len, "euses", "")
           ?? RemoveSuffix(buf, len, "euse", "")
           ?? RemoveSuffix(buf, len, "eurs", "")
           ?? RemoveSuffix(buf, len, "eur", "")
           ?? RemoveSuffix(buf, len, "ités", "")
           ?? RemoveSuffix(buf, len, "ité", "")
           ?? RemoveSuffix(buf, len, "ives", "")
           ?? RemoveSuffix(buf, len, "ive", "")
           ?? RemoveSuffix(buf, len, "ifs", "")
           ?? RemoveSuffix(buf, len, "if", "")
           ?? RemoveSuffix(buf, len, "aux", "al")
           ?? buf.Length;

        // Step 2: Verb endings
        len = RemoveSuffix(buf, len, "issent", "")
           ?? RemoveSuffix(buf, len, "issons", "")
           ?? RemoveSuffix(buf, len, "issez", "")
           ?? RemoveSuffix(buf, len, "irent", "")
           ?? RemoveSuffix(buf, len, "eront", "")
           ?? RemoveSuffix(buf, len, "erons", "")
           ?? RemoveSuffix(buf, len, "erez", "")
           ?? RemoveSuffix(buf, len, "ent", "")
           ?? RemoveSuffix(buf, len, "ons", "")
           ?? RemoveSuffix(buf, len, "ez", "")
           ?? RemoveSuffix(buf, len, "er", "")
           ?? RemoveSuffix(buf, len, "es", "")
           ?? len;

        // Remove trailing 'e' if stem > 2 chars
        if (len > 2 && buf[len - 1] == 'e')
            len--;

        var result = buf[..len];
        return result.SequenceEqual(word.AsSpan()) ? word : new string(result);
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
