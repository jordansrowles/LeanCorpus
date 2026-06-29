namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Arabic light stemmer. Removes common Arabic prefixes and suffixes without
/// performing full morphological analysis or root extraction.
/// Based on the Khoja and Garside (1999) light-stemming approach.
/// Expects lowercased, fully vowelised or unvowelised Unicode Arabic input.
/// Hamza normalisation (أ إ آ → ا) should be applied upstream.
/// </summary>
public sealed class ArabicStemmer : ISpanStemmer
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

        // Step 1: Strip definite article ال (al-)
        // Also handles وال (wal-), بال (bal-), كال (kal-), فال (fal-)
        len = RemovePrefix(output, len, "وال")
           ?? RemovePrefix(output, len, "بال")
           ?? RemovePrefix(output, len, "كال")
           ?? RemovePrefix(output, len, "فال")
           ?? RemovePrefix(output, len, "ال")
           ?? len;

        // Step 2: Strip common single-char prefixes (و ف ب ل ك س)
        // Only when remaining stem would be ≥ 3 chars
        len = RemovePrefix(output, len, "و")
           ?? RemovePrefix(output, len, "ف")
           ?? RemovePrefix(output, len, "ب")
           ?? RemovePrefix(output, len, "ل")
           ?? RemovePrefix(output, len, "ك")
           ?? RemovePrefix(output, len, "س")
           ?? len;

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
