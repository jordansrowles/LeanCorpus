namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Porter Stemming Algorithm implementation as an ITokenFilter.
/// Based on the Porter 1980 specification for English stemming.
/// Operates on tokens in-place, replacing text with stemmed form.
/// </summary>
public sealed class PorterStemmerFilter : ITokenFilter
{
    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var stemmed = Stem(t.Text);
            if (!ReferenceEquals(stemmed, t.Text))
                tokens[i] = new Token(stemmed, t.StartOffset, t.EndOffset);
        }
    }

    internal static string Stem(string word)
    {
        if (word.Length <= 2) return word;

        Span<char> buf = word.Length <= 64
            ? stackalloc char[word.Length]
            : new char[word.Length];
        word.AsSpan().CopyTo(buf);
        int len = buf.Length;

        len = Step1a(buf, len);
        len = Step1b(buf, len);
        len = Step1c(buf, len);
        len = Step2(buf, len);
        len = Step3(buf, len);
        len = Step4(buf, len);
        len = Step5a(buf, len);
        len = Step5b(buf, len);

        var result = buf[..len];
        return result.SequenceEqual(word.AsSpan()) ? word : new string(result);
    }

    private static bool IsConsonant(ReadOnlySpan<char> s, int i)
    {
        char c = s[i];
        if (c is 'a' or 'e' or 'i' or 'o' or 'u') return false;
        if (c == 'y') return i == 0 || !IsConsonant(s, i - 1);
        return true;
    }

    /// <summary>Counts consonant-vowel sequences (measure) in s[0..len).</summary>
    private static int Measure(ReadOnlySpan<char> s, int len)
    {
        int n = 0, i = 0;
        while (true)
        {
            if (i >= len) return n;
            if (!IsConsonant(s, i)) break;
            i++;
        }
        i++;
        while (true)
        {
            while (true)
            {
                if (i >= len) return n;
                if (IsConsonant(s, i)) break;
                i++;
            }
            i++;
            n++;
            while (true)
            {
                if (i >= len) return n;
                if (!IsConsonant(s, i)) break;
                i++;
            }
            i++;
        }
    }

    private static bool ContainsVowel(ReadOnlySpan<char> s, int len)
    {
        for (int i = 0; i < len; i++)
            if (!IsConsonant(s, i)) return true;
        return false;
    }

    private static bool EndsWithDouble(ReadOnlySpan<char> s, int len)
    {
        if (len < 2) return false;
        return s[len - 1] == s[len - 2] && IsConsonant(s, len - 1);
    }

    /// <summary>CVC: consonant-vowel-consonant at end, with last consonant not w/x/y.</summary>
    private static bool CVC(ReadOnlySpan<char> s, int len)
    {
        if (len < 3) return false;
        if (!IsConsonant(s, len - 1) || IsConsonant(s, len - 2) || !IsConsonant(s, len - 3)) return false;
        char c = s[len - 1];
        return c is not ('w' or 'x' or 'y');
    }

    private static bool EndsWith(ReadOnlySpan<char> s, int len, ReadOnlySpan<char> suffix)
    {
        if (len < suffix.Length) return false;
        return s.Slice(len - suffix.Length, suffix.Length).SequenceEqual(suffix);
    }

    private static int ReplaceSuffix(Span<char> s, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        int stemLen = len - suffix.Length;
        replacement.CopyTo(s[stemLen..]);
        return stemLen + replacement.Length;
    }

    // Step 1a: Plurals
    private static int Step1a(Span<char> s, int len)
    {
        if (EndsWith(s, len, "sses")) return len - 2; // sses → ss
        if (EndsWith(s, len, "ies")) return ReplaceSuffix(s, len, "ies", "i");
        if (EndsWith(s, len, "ss")) return len; // ss → ss
        if (len > 1 && s[len - 1] == 's') return len - 1; // s → (remove)
        return len;
    }

    // Step 1b: -ed, -ing
    private static int Step1b(Span<char> s, int len)
    {
        if (EndsWith(s, len, "eed"))
        {
            int stemLen = len - 3;
            if (Measure(s, stemLen) > 0)
                return len - 1; // eed → ee
            return len;
        }

        bool found = false;
        int newLen = len;

        if (EndsWith(s, len, "ed"))
        {
            int stemLen = len - 2;
            if (ContainsVowel(s, stemLen))
            {
                newLen = stemLen;
                found = true;
            }
        }
        else if (EndsWith(s, len, "ing"))
        {
            int stemLen = len - 3;
            if (ContainsVowel(s, stemLen))
            {
                newLen = stemLen;
                found = true;
            }
        }

        if (!found) return len;

        if (EndsWith(s, newLen, "at") || EndsWith(s, newLen, "bl") || EndsWith(s, newLen, "iz"))
        {
            s[newLen] = 'e';
            return newLen + 1;
        }

        if (EndsWithDouble(s, newLen))
        {
            char c = s[newLen - 1];
            if (c is not ('l' or 's' or 'z'))
                return newLen - 1;
        }

        if (Measure(s, newLen) == 1 && CVC(s, newLen))
        {
            s[newLen] = 'e';
            return newLen + 1;
        }

        return newLen;
    }

    // Step 1c: y → i
    private static int Step1c(Span<char> s, int len)
    {
        if (s[len - 1] == 'y' && ContainsVowel(s, len - 1))
        {
            s[len - 1] = 'i';
        }
        return len;
    }

    // Step 2: Map double suffixes
    private static int Step2(Span<char> s, int len)
    {
        if (len < 4) return len;
        return s[len - 2] switch
        {
            'a' => TryReplace2(s, len, "ational", "ate") ?? TryReplace2(s, len, "tional", "tion") ?? len,
            'c' => TryReplace2(s, len, "enci", "ence") ?? TryReplace2(s, len, "anci", "ance") ?? len,
            'e' => TryReplace2(s, len, "izer", "ize") ?? len,
            'l' => TryReplace2(s, len, "abli", "able") ?? TryReplace2(s, len, "alli", "al") ??
                   TryReplace2(s, len, "entli", "ent") ?? TryReplace2(s, len, "eli", "e") ??
                   TryReplace2(s, len, "ousli", "ous") ?? len,
            'o' => TryReplace2(s, len, "ization", "ize") ?? TryReplace2(s, len, "ation", "ate") ??
                   TryReplace2(s, len, "ator", "ate") ?? len,
            's' => TryReplace2(s, len, "alism", "al") ?? TryReplace2(s, len, "iveness", "ive") ??
                   TryReplace2(s, len, "fulness", "ful") ?? TryReplace2(s, len, "ousness", "ous") ?? len,
            't' => TryReplace2(s, len, "aliti", "al") ?? TryReplace2(s, len, "iviti", "ive") ??
                   TryReplace2(s, len, "biliti", "ble") ?? len,
            _ => len
        };
    }

    private static int? TryReplace2(Span<char> s, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (!EndsWith(s, len, suffix)) return null;
        int stemLen = len - suffix.Length;
        if (Measure(s, stemLen) > 0)
            return ReplaceSuffix(s, len, suffix, replacement);
        return null;
    }

    // Step 3: Further reductions
    private static int Step3(Span<char> s, int len)
    {
        if (len < 4) return len;
        return s[len - 1] switch
        {
            'e' => TryReplace3(s, len, "icate", "ic") ?? TryReplace3(s, len, "ative", "") ??
                   TryReplace3(s, len, "alize", "al") ?? len,
            'i' => TryReplace3(s, len, "iciti", "ic") ?? len,
            'l' => TryReplace3(s, len, "ical", "ic") ?? TryReplace3(s, len, "ful", "") ?? len,
            's' => TryReplace3(s, len, "ness", "") ?? len,
            _ => len
        };
    }

    private static int? TryReplace3(Span<char> s, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (!EndsWith(s, len, suffix)) return null;
        int stemLen = len - suffix.Length;
        if (Measure(s, stemLen) > 0)
        {
            if (replacement.Length == 0) return stemLen;
            return ReplaceSuffix(s, len, suffix, replacement);
        }
        return null;
    }

    // Step 4: Remove -ant, -ence, -er, etc.
    private static int Step4(Span<char> s, int len)
    {
        if (len < 3) return len;

        ReadOnlySpan<char> suffix = s[len - 2] switch
        {
            'a' => EndsWith(s, len, "al") ? "al" : default,
            'c' => EndsWith(s, len, "ance") ? "ance" : EndsWith(s, len, "ence") ? "ence" : default,
            'e' => EndsWith(s, len, "er") ? "er" : default,
            'i' => EndsWith(s, len, "ic") ? "ic" : default,
            'l' => EndsWith(s, len, "able") ? "able" : EndsWith(s, len, "ible") ? "ible" : default,
            'n' => EndsWith(s, len, "ant") ? "ant" : EndsWith(s, len, "ement") ? "ement" :
                   EndsWith(s, len, "ment") ? "ment" : EndsWith(s, len, "ent") ? "ent" : default,
            'o' => EndsWith(s, len, "ion") && len >= 4 && s[len - 4] is 's' or 't' ? "ion" :
                   EndsWith(s, len, "ou") ? "ou" : default,
            's' => EndsWith(s, len, "ism") ? "ism" : default,
            't' => EndsWith(s, len, "ate") ? "ate" : EndsWith(s, len, "iti") ? "iti" : default,
            'u' => EndsWith(s, len, "ous") ? "ous" : default,
            'v' => EndsWith(s, len, "ive") ? "ive" : default,
            'z' => EndsWith(s, len, "ize") ? "ize" : default,
            _ => default
        };

        if (suffix.Length == 0) return len;

        int stemLen = len - suffix.Length;
        if (Measure(s, stemLen) > 1)
            return stemLen;
        return len;
    }

    // Step 5a: Remove trailing 'e'
    private static int Step5a(Span<char> s, int len)
    {
        if (s[len - 1] != 'e') return len;
        int stemLen = len - 1;
        int m = Measure(s, stemLen);
        if (m > 1) return stemLen;
        if (m == 1 && !CVC(s, stemLen)) return stemLen;
        return len;
    }

    // Step 5b: -ll → -l
    private static int Step5b(Span<char> s, int len)
    {
        if (len > 1 && s[len - 1] == 'l' && s[len - 2] == 'l' && Measure(s, len - 1) > 1)
            return len - 1;
        return len;
    }
}
