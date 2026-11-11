namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Arabic light stemmer. Removes the definite article (with an attached conjunction or
/// preposition), or otherwise a leading conjunction و, and common suffixes, without performing
/// full morphological analysis or root extraction. Other single-letter prefixes are not removed,
/// because they are usually root letters. Expects unvowelised or fully vowelised Unicode Arabic
/// input; hamza normalisation should be applied upstream.
/// </summary>
public sealed class ArabicStemmer : IShareableSpanStemmer
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
        if (word.Length <= 2)
        {
            if (output.Length < word.Length) return -1;
            word.CopyTo(output);
            return word.Length;
        }
        if (output.Length < word.Length) return -1;
        word.CopyTo(output);
        int len = word.Length;

        // Step 1: strip the definite article, alone or with an attached conjunction or
        // preposition, leaving at least 3 characters.
        int? withoutArticle = RemovePrefix(output, len, "وال")
           ?? RemovePrefix(output, len, "بال")
           ?? RemovePrefix(output, len, "كال")
           ?? RemovePrefix(output, len, "فال")
           ?? RemovePrefix(output, len, "لل")
           ?? RemovePrefix(output, len, "ال");

        // Step 2: only when no article was stripped, strip a leading conjunction و,
        // leaving at least 3 characters. No other single-letter prefix is removed.
        len = withoutArticle ?? RemovePrefix(output, len, "و") ?? len;

        // Step 3: Strip common suffixes (longest first)
        len = RemoveSuffix(output, len, "تين", "")    // dual feminine
           ?? RemoveSuffix(output, len, "ين", "")     // masculine plural genitive / dual
           ?? RemoveSuffix(output, len, "ون", "")     // masculine sound plural nominative
           ?? RemoveSuffix(output, len, "ات", "")     // feminine plural
           ?? RemoveSuffix(output, len, "ان", "")     // dual nominative
           ?? RemoveSuffix(output, len, "تا", "")     // dual feminine nominative
           ?? RemoveSuffix(output, len, "ية", "")     // nisba feminine
           ?? RemoveSuffix(output, len, "ية", "")
           ?? RemoveSuffix(output, len, "يا", "")
           ?? RemoveSuffix(output, len, "ها", "")     // pronoun suffix (her/it)
           ?? RemoveSuffix(output, len, "هم", "")     // pronoun suffix (them)
           ?? RemoveSuffix(output, len, "هن", "")     // pronoun suffix (them fem)
           ?? RemoveSuffix(output, len, "كم", "")     // pronoun suffix (you pl)
           ?? RemoveSuffix(output, len, "نا", "")     // pronoun suffix (us)
           ?? RemoveSuffix(output, len, "ني", "")     // pronoun suffix (me)
           ?? RemoveSuffix(output, len, "تم", "")     // past verb 2nd pl masc
           ?? RemoveSuffix(output, len, "تن", "")     // past verb 2nd pl fem
           ?? RemoveSuffix(output, len, "ة", "")      // ta marbuta (feminine marker)
           ?? RemoveSuffix(output, len, "ت", "")      // ta (past verb / feminine)
           ?? RemoveSuffix(output, len, "ي", "")      // ya (genitive / 1st sg)
           ?? RemoveSuffix(output, len, "ا", "")      // alif (accusative tanwin)
           ?? len;

        return len;
    }

    private static int? RemovePrefix(Span<char> buf, int len, ReadOnlySpan<char> prefix)
    {
        // Require at least 3 characters remaining after stripping
        if (len < prefix.Length + 3) return null;
        if (!buf[..prefix.Length].SequenceEqual(prefix)) return null;
        int newLen = len - prefix.Length;
        buf[prefix.Length..len].CopyTo(buf);
        return newLen;
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
