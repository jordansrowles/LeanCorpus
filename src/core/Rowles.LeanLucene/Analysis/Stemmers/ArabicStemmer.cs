namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Arabic light stemmer. Removes common Arabic prefixes and suffixes without
/// performing full morphological analysis or root extraction.
/// Based on the Khoja and Garside (1999) light-stemming approach.
/// Expects lowercased, fully vowelised or unvowelised Unicode Arabic input.
/// Hamza normalisation (أ إ آ → ا) should be applied upstream.
/// </summary>
public sealed class ArabicStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word)
    {
        if (word.Length <= 2) return word;

        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        word.AsSpan().CopyTo(buf);
        int len = buf.Length;

        // Step 1: Strip definite article ال (al-)
        // Also handles وال (wal-), بال (bal-), كال (kal-), فال (fal-)
        len = RemovePrefix(buf, len, "وال")
           ?? RemovePrefix(buf, len, "بال")
           ?? RemovePrefix(buf, len, "كال")
           ?? RemovePrefix(buf, len, "فال")
           ?? RemovePrefix(buf, len, "ال")
           ?? len;

        // Step 2: Strip common single-char prefixes (و ف ب ل ك س)
        // Only when remaining stem would be ≥ 3 chars
        len = RemovePrefix(buf, len, "و")
           ?? RemovePrefix(buf, len, "ف")
           ?? RemovePrefix(buf, len, "ب")
           ?? RemovePrefix(buf, len, "ل")
           ?? RemovePrefix(buf, len, "ك")
           ?? RemovePrefix(buf, len, "س")
           ?? len;

        // Step 3: Strip common suffixes (longest first)
        len = RemoveSuffix(buf, len, "تين", "")    // dual feminine
           ?? RemoveSuffix(buf, len, "ين", "")     // masculine plural genitive / dual
           ?? RemoveSuffix(buf, len, "ون", "")     // masculine sound plural nominative
           ?? RemoveSuffix(buf, len, "ات", "")     // feminine plural
           ?? RemoveSuffix(buf, len, "ان", "")     // dual nominative
           ?? RemoveSuffix(buf, len, "تا", "")     // dual feminine nominative
           ?? RemoveSuffix(buf, len, "ية", "")     // nisba feminine
           ?? RemoveSuffix(buf, len, "ية", "")
           ?? RemoveSuffix(buf, len, "يا", "")
           ?? RemoveSuffix(buf, len, "ها", "")     // pronoun suffix (her/it)
           ?? RemoveSuffix(buf, len, "هم", "")     // pronoun suffix (them)
           ?? RemoveSuffix(buf, len, "هن", "")     // pronoun suffix (them fem)
           ?? RemoveSuffix(buf, len, "كم", "")     // pronoun suffix (you pl)
           ?? RemoveSuffix(buf, len, "نا", "")     // pronoun suffix (us)
           ?? RemoveSuffix(buf, len, "ني", "")     // pronoun suffix (me)
           ?? RemoveSuffix(buf, len, "تم", "")     // past verb 2nd pl masc
           ?? RemoveSuffix(buf, len, "تن", "")     // past verb 2nd pl fem
           ?? RemoveSuffix(buf, len, "ة", "")      // ta marbuta (feminine marker)
           ?? RemoveSuffix(buf, len, "ت", "")      // ta (past verb / feminine)
           ?? RemoveSuffix(buf, len, "ي", "")      // ya (genitive / 1st sg)
           ?? RemoveSuffix(buf, len, "ا", "")      // alif (accusative tanwin)
           ?? len;

        var result = buf[..len];
        return result.SequenceEqual(word.AsSpan()) ? word : new string(result);
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
