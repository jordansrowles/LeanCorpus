using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Slovak stemmer.
/// Expects UTF-8 normalized input with diacritics.
/// </summary>
public sealed class SlovakStemmer : ISpanStemmer
{
    private static readonly List<string[]> suffixesGroups =
    [
        new string[]
        {
            "encami", "atami", "ätami", "iami", "ými", "ovi", "ati", "äti", "eniec", "ence",
            "ie", "aťom", "äťom", "encom", "atám", "ätám", "iam", "ím", "ým", "encoch",
            "atách", "ätách", "iach", "ých", "aťa", "äťa", "ovia", "atá", "ätá", "aťu",
            "äťu", "ému", "iu", "iou", "ov", "at", "ät", "ä", "ého", "ý",
            "y", "ií", "ej", "ú", "é"
        },
        new string[]
        {
            "e", "om", "ami", "ám", "och", "ach", "ách", "ia", "á", "ou",
            "o", "ii", "í"
        },
        new string[] { "mi", "a", "u" },
        new string[] { "i" },
    ];

    private static readonly (string LongLetter, string ShortLetter)[] longToShortDictionary =
    [
        ("á", "a" ),
        ("ie", "e" ),
        ("ŕ", "r" ),
        ("ľ", "l" ),
        ("í", "i" ),
        ("ú", "u" ),
        ("ň", "n" ),
        ("ô", "o" )
    ];

    private static readonly string[] lettersRandL =
    [
       "r", "ŕ", "l", "í"
    ];

    private static readonly string[] eiSuffix =
    [
       "e", "i", "iam", "iach", "iami", "í", "ia", "ie", "iu", "ím"
    ];

    private static readonly string[] vowels =
    [
       "a", "á", "ä", "e", "é", "i", "í", "o", "ó", "u",
       "ú", "y", "ý", "ô", "ia", "ie", "iu"
    ];

    public SlovakStemmer()
    {
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length < 4)
        {
            if (output.Length < word.Length)
            {
                return -1;
            }

            word.CopyTo(output);
            return word.Length;
        }

        return RemoveSuffixes(word, output);
    }

    private int RemoveSuffixes(ReadOnlySpan<char> word, Span<char> output)
    {
        foreach (string[] suffixesGroup in suffixesGroups)
        {
            foreach (string suffix in suffixesGroup)
            {
                if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsDtnl(suffix))
                    {
                        return RestoreDtnl(RemoveSuffix(word, suffix), output);
                    }
                    if (suffix.EndsWith("i", StringComparison.OrdinalIgnoreCase) && IsForeignWord(word, suffix))
                    {
                        return TryChangeSuffix(word, suffix, "i", output);
                    }
                    if (IsOverStemming(word, suffix))
                    {
                        word.CopyTo(output);
                        return word.Length;
                    }

                    ReadOnlySpan<char> stem = RemoveSuffix(word, suffix);
                    if (stem.Length > output.Length)
                    {
                        return -1;
                    }

                    stem.CopyTo(output);
                    return stem.Length;
                }
            }
        }

        if (word.EndsWith("er", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "er", "r", output);
        }

        if (word.EndsWith("ok", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "ok", "k", output);
        }

        if (word.EndsWith("zeň", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "eň", "ň", output);
        }

        if (word.EndsWith("ol", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "ol", "l", output);
        }

        if (word.EndsWith("ic", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "c", "k", output);
        }

        if (word.EndsWith("ec", StringComparison.OrdinalIgnoreCase))
        {
            return TryChangeSuffix(word, "ec", "c", output);
        }

        if (word.EndsWith("um", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<char> stem = RemoveSuffix(word, "um");
            stem.CopyTo(output);
            return stem.Length;
        }

        return ProcessGentivPlural(word, output);
    }

    private bool IsOverStemming(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix)
    {
        ReadOnlySpan<char> text = RemoveSuffix(word, suffix);
        foreach (string vowel in vowels)
        {
            if (text.Contains(vowel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        foreach (string letterRandL in lettersRandL)
        {
            if (text.Contains(letterRandL, StringComparison.OrdinalIgnoreCase) && !text.EndsWith(letterRandL, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private int ProcessGentivPlural(ReadOnlySpan<char> word, Span<char> output)
    {
        foreach ((string longLetters, string shortLetters) in longToShortDictionary)
        {
            if (word.Contains(longLetters, StringComparison.OrdinalIgnoreCase) && IsLastSyllable(word, longLetters))
            {
                int writes = ChangeLastLetter(word, longLetters, shortLetters, output);
                if (writes == -1)
                {
                    return -1;
                }

                word = output.Slice(0, writes);

                break;
            }
        }

        word.CopyTo(output);
        return word.Length;
    }

    private bool IsLastSyllable(ReadOnlySpan<char> word, ReadOnlySpan<char> longVowel)
    {
        int startIndex = word.LastIndexOf(longVowel);
        if (startIndex == -1)
        {
            return false;
        }

        ReadOnlySpan<char> text = word.Slice(startIndex + longVowel.Length);

        foreach (string value in vowels)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private int ChangeLastLetter(ReadOnlySpan<char> word, ReadOnlySpan<char> replacedLetter, ReadOnlySpan<char> newLetter, Span<char> output)
    {
        int num = word.LastIndexOf(replacedLetter, StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<char> suffix = word.Slice(num + replacedLetter.Length);
        if (num + newLetter.Length + suffix.Length > output.Length)
        {
            return -1;
        }

        word.Slice(0, num).CopyTo(output);
        newLetter.CopyTo(output.Slice(num));
        suffix.CopyTo(output.Slice(num + newLetter.Length));

        return num + newLetter.Length + suffix.Length;
    }

    private bool IsForeignWord(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix)
    {
        ReadOnlySpan<char> check = RemoveSuffix(word, suffix);
        if (check.Length == 0)
        {
            return false;
        }

        return check[^1] switch
        {
            'c' => true,
            'C' => true,
            'z' => true,
            'Z' => true,
            'g' => true,
            'G' => true,
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> RemoveSuffix(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix)
    {
        if (!word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return word;
        }

        return word.Slice(0, word.Length - suffix.Length);
    }

    private int TryChangeSuffix(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix, ReadOnlySpan<char> newSuffix, Span<char> output)
    {
        if (!word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            word.CopyTo(output);
            return word.Length;
        }

        int stemLen = word.Length - suffix.Length;
        word.Slice(0, stemLen).CopyTo(output);
        newSuffix.CopyTo(output.Slice(stemLen));

        return stemLen + newSuffix.Length;
    }

    private bool IsDtnl(ReadOnlySpan<char> suffix)
    {
        foreach (string value in eiSuffix)
        {
            if (suffix.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private int RestoreDtnl(ReadOnlySpan<char> wordRoot, Span<char> output)
    {
        wordRoot.CopyTo(output);
        output[wordRoot.Length - 1] = wordRoot[^1] switch
        {
            'd' => 'ď',
            'D' => 'ď',
            't' => 'ť',
            'T' => 'ť',
            'n' => 'ň',
            'N' => 'ň',
            'l' => 'ľ',
            'L' => 'ľ',
            char c => c
        };

        return wordRoot.Length;
    }
}
