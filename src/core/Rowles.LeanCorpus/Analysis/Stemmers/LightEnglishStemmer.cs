namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Lightweight English suffix stemmer for common inflections.
/// </summary>
public sealed class LightEnglishStemmer : IStemmer
{
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["skies"] = "sky",
        ["dying"] = "die",
        ["lying"] = "lie",
        ["tying"] = "tie"
    };

    private static readonly HashSet<string> ProtectedWords = new(StringComparer.Ordinal)
    {
        "news", "innings", "proceed", "exceed", "succeed"
    };

    /// <inheritdoc/>
    public string Stem(string word)
    {
        if (word.Length <= 2)
            return word;

        string value = word.ToLowerInvariant();
        if (Exceptions.TryGetValue(value, out var exception))
            return exception;
        if (ProtectedWords.Contains(value))
            return value;

        string candidate = value;
        candidate = StemPlural(candidate);
        candidate = StemPastTense(candidate);
        candidate = StemProgressive(candidate);
        candidate = StemSuffix(candidate, "ness");
        candidate = StemSuffix(candidate, "ment");
        candidate = StemSuffix(candidate, "ly");
        return candidate;
    }

    private static string StemPlural(string value)
    {
        if (value.EndsWith("ies", StringComparison.Ordinal) && value.Length > 4)
            return value[..^3] + "y";
        if (value.EndsWith("sses", StringComparison.Ordinal))
            return value[..^2];
        if (value.EndsWith("es", StringComparison.Ordinal) && value.Length > 4)
            return value[..^2];
        if (value.EndsWith('s') && !value.EndsWith("ss", StringComparison.Ordinal) && !value.EndsWith("us", StringComparison.Ordinal))
            return value[..^1];
        return value;
    }

    private static string StemPastTense(string value)
    {
        if (!value.EndsWith("ed", StringComparison.Ordinal) || value.Length <= 4)
            return value;

        string stem = value[..^2];
        if (!ContainsVowel(stem))
            return value;

        return UndoubleTrailingConsonant(stem);
    }

    private static string StemProgressive(string value)
    {
        if (!value.EndsWith("ing", StringComparison.Ordinal) || value.Length <= 5)
            return value;

        string stem = value[..^3];
        if (!ContainsVowel(stem))
            return value;

        stem = UndoubleTrailingConsonant(stem);
        if (stem.EndsWith("at", StringComparison.Ordinal)
            || stem.EndsWith("bl", StringComparison.Ordinal)
            || stem.EndsWith("iz", StringComparison.Ordinal))
        {
            return stem + "e";
        }

        return stem;
    }

    private static string StemSuffix(string value, string suffix)
    {
        if (value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length + 2)
            return value[..^suffix.Length];
        return value;
    }

    private static bool ContainsVowel(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if ("aeiou".Contains(value[i]))
                return true;
        }

        return false;
    }

    private static string UndoubleTrailingConsonant(string value)
    {
        if (value.Length >= 2
            && value[^1] == value[^2]
            && !"aeiou".Contains(value[^1])
            && value[^1] is not ('l' or 's' or 'z'))
        {
            return value[..^1];
        }

        return value;
    }
}
