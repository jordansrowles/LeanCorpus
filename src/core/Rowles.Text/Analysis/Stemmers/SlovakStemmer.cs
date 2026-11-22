using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Slovak stemmer.
/// Expects lower-cased NFC input with diacritics.
/// </summary>
/// <remarks>
/// The stem's final consonant is always written hard (<c>ď</c>, <c>ť</c>, <c>ň</c> and <c>ľ</c> become
/// <c>d</c>, <c>t</c>, <c>n</c> and <c>l</c>). Slovak spells a soft consonant without its caron before
/// <c>e</c>, <c>i</c>, <c>í</c> and <c>ia</c>, so the ending alone cannot say whether a stem is soft;
/// one hard spelling lets <c>mesto</c> and <c>meste</c>, and <c>kosť</c> and <c>kosti</c>, share a term.
/// </remarks>
public sealed class SlovakStemmer : IShareableSpanStemmer
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
        ("í", "i" ),
        ("ú", "u" ),
        ("ô", "o" )
    ];

    private static readonly string[] lettersRandL =
    [
       "r", "ŕ", "l", "ĺ"
    ];

    private static readonly string[] vowels =
    [
       "a", "á", "ä", "e", "é", "i", "í", "o", "ó", "u",
       "ú", "y", "ý", "ô", "ia", "ie", "iu"
    ];

    /// <summary>
    /// Stems the given word and returns the stemmed result as a string.
    /// Short words (fewer than 4 characters) keep their letters apart from a hard final consonant.
    /// </summary>
    public string Stem(string word)
    {
        int maxLen = word.Length;
        Span<char> buf = maxLen <= 64 ? stackalloc char[maxLen] : new char[maxLen];
        int len = Stem(word.AsSpan(), buf);
        return len < 0 ? word : new string(buf[..len]);
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
            return Harden(output, word.Length);
        }

        // No rule lengthens a word, so a word-sized buffer always holds the stem. A shorter output gets
        // the stem only if it fits, and is left untouched otherwise.
        if (output.Length >= word.Length)
        {
            return Harden(output, RemoveSuffixes(word, output));
        }

        char[]? rented = null;
        Span<char> scratch = word.Length <= 64 ? stackalloc char[64] : (rented = ArrayPool<char>.Shared.Rent(word.Length));
        try
        {
            int length = Harden(scratch, RemoveSuffixes(word, scratch));
            if (length < 0 || length > output.Length)
            {
                return -1;
            }

            scratch[..length].CopyTo(output);
            return length;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Writes the stem's final soft consonant hard, so soft and hard spellings share a term.</summary>
    private static int Harden(Span<char> stem, int length)
    {
        if (length > 0)
        {
            stem[length - 1] = stem[length - 1] switch
            {
                'ď' => 'd',
                'ť' => 't',
                'ň' => 'n',
                'ľ' => 'l',
                'Ď' => 'D',
                'Ť' => 'T',
                'Ň' => 'N',
                'Ľ' => 'L',
                char c => c,
            };
        }

        return length;
    }

    private static int RemoveSuffixes(ReadOnlySpan<char> word, Span<char> output)
    {
        foreach (string[] suffixesGroup in suffixesGroups)
        {
            foreach (string suffix in suffixesGroup)
            {
                // A suffix must leave a root: a token that is only a suffix, such as "iach", tries the next one.
                if (suffix.Length < word.Length && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    // "-at" is the genitive plural of neuter "-a" nouns (dievčat), but part of the root in
                    // "-omat" nouns such as automat and bankomat, whose other forms keep it.
                    if (suffix == "at" && word.EndsWith("omat", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
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

    private static bool IsOverStemming(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix)
    {
        ReadOnlySpan<char> rootWord = RemoveSuffix(word, suffix);
        foreach (string vowel in vowels)
        {
            if (rootWord.Contains(vowel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        foreach (string letterRandL in lettersRandL)
        {
            if (rootWord.Contains(letterRandL, StringComparison.OrdinalIgnoreCase) && !rootWord.EndsWith(letterRandL, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static int ProcessGentivPlural(ReadOnlySpan<char> word, Span<char> output)
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

    private static bool IsLastSyllable(ReadOnlySpan<char> word, ReadOnlySpan<char> longVowel)
    {
        int lastVowelIndex = word.LastIndexOf(longVowel, StringComparison.OrdinalIgnoreCase);
        if (lastVowelIndex == -1)
        {
            return false;
        }

        ReadOnlySpan<char> wordWihoutVowel = word.Slice(lastVowelIndex + longVowel.Length);

        foreach (string value in vowels)
        {
            if (wordWihoutVowel.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int ChangeLastLetter(ReadOnlySpan<char> word, ReadOnlySpan<char> replacedLetter, ReadOnlySpan<char> newLetter, Span<char> output)
    {
        int replacedLetterIndex = word.LastIndexOf(replacedLetter, StringComparison.OrdinalIgnoreCase);
        ReadOnlySpan<char> suffix = word.Slice(replacedLetterIndex + replacedLetter.Length);
        if (replacedLetterIndex + newLetter.Length + suffix.Length > output.Length)
        {
            return -1;
        }

        word.Slice(0, replacedLetterIndex).CopyTo(output);
        newLetter.CopyTo(output.Slice(replacedLetterIndex));
        if (char.IsUpper(word[replacedLetterIndex]))
        {
            // Keep the case of the letter being shortened: MIEST becomes MEST, not MeST.
            for (int i = replacedLetterIndex; i < replacedLetterIndex + newLetter.Length; i++)
            {
                output[i] = char.ToUpperInvariant(output[i]);
            }
        }
        suffix.CopyTo(output.Slice(replacedLetterIndex + newLetter.Length));

        return replacedLetterIndex + newLetter.Length + suffix.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> RemoveSuffix(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix)
    {
        if (!word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return word;
        }

        return word.Slice(0, word.Length - suffix.Length);
    }

    private static int TryChangeSuffix(ReadOnlySpan<char> word, ReadOnlySpan<char> suffix, ReadOnlySpan<char> newSuffix, Span<char> output)
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
}
