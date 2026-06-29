using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Lightweight English suffix stemmer for common inflections.
/// </summary>
public sealed class LightEnglishStemmer : ISpanStemmer
{
    private static readonly FrozenDictionary<string, string> Exceptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["skies"] = "sky",
            ["dying"] = "die",
            ["lying"] = "lie",
            ["tying"] = "tie"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ProtectedWords =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "news", "innings", "proceed", "exceed", "succeed"
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Convenience overload returning a stemmed string.</summary>
    public string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        Span<char> buf = stackalloc char[word.Length];
        int len = Stem(word.AsSpan(), buf);
        return new string(buf[..len]);
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= 2)
        {
            word.CopyTo(output);
            return word.Length;
        }

        // Lowercase into output via SIMD.
        int len = word.Length;
        AsciiCharInspector.AsciiToLower(word, output);

        var lowered = output[..len];

        // Check exceptions via AlternateLookup (zero-alloc).
        var exLookup = Exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exLookup.TryGetValue(lowered, out var replacement))
        {
            replacement.AsSpan().CopyTo(output);
            return replacement.Length;
        }

        // Check protected words.
        var pwLookup = ProtectedWords.GetAlternateLookup<ReadOnlySpan<char>>();
        if (pwLookup.Contains(lowered))
            return len;

        len = StemPlural(output, len);
        len = StemPastTense(output, len);
        len = StemProgressive(output, len);
        len = StemSuffix(output, len, "ness");
        len = StemSuffix(output, len, "ment");
        len = StemSuffix(output, len, "ly");
        return len;
    }

    private static int StemPlural(Span<char> s, int len)
    {
        var span = s[..len];
        if (span.EndsWith("ies") && len > 4)
        {
            s[len - 3] = 'y';
            return len - 2;
        }
        if (span.EndsWith("sses"))
            return len - 2;
        if (span.EndsWith("es") && len > 4)
            return len - 2;
        if (span[^1] == 's' && !span.EndsWith("ss") && !span.EndsWith("us"))
            return len - 1;
        return len;
    }

    private static int StemPastTense(Span<char> s, int len)
    {
        if (!s[..len].EndsWith("ed") || len <= 4)
            return len;

        int stemLen = len - 2;
        if (!ContainsVowel(s, stemLen))
            return len;

        return UndoubleTrailingConsonant(s, stemLen);
    }

    private static int StemProgressive(Span<char> s, int len)
    {
        if (!s[..len].EndsWith("ing") || len <= 5)
            return len;

        int stemLen = len - 3;
        if (!ContainsVowel(s, stemLen))
            return len;

        stemLen = UndoubleTrailingConsonant(s, stemLen);
        var stem = s[..stemLen];
        if (stem.EndsWith("at") || stem.EndsWith("bl") || stem.EndsWith("iz"))
        {
            s[stemLen] = 'e';
            return stemLen + 1;
        }

        return stemLen;
    }

    private static int StemSuffix(Span<char> s, int len, ReadOnlySpan<char> suffix)
    {
        if (s[..len].EndsWith(suffix) && len > suffix.Length + 2)
            return len - suffix.Length;
        return len;
    }

    private static bool ContainsVowel(ReadOnlySpan<char> s, int len)
    {
        var span = s[..len];
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] is 'a' or 'e' or 'i' or 'o' or 'u')
                return true;
        }
        return false;
    }

    private static int UndoubleTrailingConsonant(Span<char> s, int len)
    {
        if (len >= 2
            && s[len - 1] == s[len - 2]
            && s[len - 1] is not ('a' or 'e' or 'i' or 'o' or 'u')
            && s[len - 1] is not ('l' or 's' or 'z'))
        {
            return len - 1;
        }
        return len;
    }
}
