using System.Buffers;
using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Slovak stemmer.
/// Expects lower-cased NFC input with diacritics.
/// </summary>
/// <remarks>
/// <para>
/// The stem's final consonant is always written hard (<c>ď</c>, <c>ť</c>, <c>ň</c> and <c>ľ</c> become
/// <c>d</c>, <c>t</c>, <c>n</c> and <c>l</c>). Slovak spells a soft consonant without its caron before
/// <c>e</c>, <c>i</c>, <c>í</c> and <c>ia</c>, so the ending alone cannot say whether a stem is soft;
/// one hard spelling lets <c>mesto</c> and <c>meste</c>, and <c>kosť</c> and <c>kosti</c>, share a term.
/// </para>
/// <para>
/// A word with no ending (a nominative or a genitive plural) keeps a long <c>á</c> or <c>ú</c>, because
/// masculine nominatives such as <c>štát</c> and <c>súd</c> keep it in every form. It shortens <c>ô</c>,
/// <c>ie</c> and <c>ŕ</c>, and <c>í</c> before <c>n</c>, <c>t</c>, <c>c</c> or <c>h</c>, the genitive
/// plurals of the feminine <c>-ina</c>, <c>-ita</c>, <c>-ica</c> and <c>-iha</c> nouns. An <c>ie</c>
/// inserted between two consonants of a genitive plural (<c>okien</c>, <c>matiek</c>) is removed.
/// </para>
/// </remarks>
public sealed class SlovakStemmer : IShareableSpanStemmer
{
    /// <summary>
    /// Endings, tried in order; the first that leaves a root with a vowel or syllabic liquid is removed.
    /// The order is part of the analysis identity.
    /// </summary>
    private static readonly string[] suffixes =
    [
        // -ovať verbs: infinitive, present and past share the root (pracovať, pracuje, pracoval).
        "ovať", "ujem", "uješ", "ujeme", "ujete", "ujú", "uje", "ovala", "ovalo", "ovali", "oval",
        // Infinitives and present-tense persons that join the present stem.
        "ieť", "iť", "ať", "uť", "ýť",
        "íme", "íte", "íš", "áš", "eš", "ajú", "em",
        // Adjective endings shortened after a long syllable (rýchleho, rýchlym) and soft adjective endings.
        "ieho", "iemu", "eho", "emu", "ymi", "ych", "ym", "ích", "ími",
        // Noun and adjective case endings.
        "encami", "atami", "ätami", "iami", "ými", "ovi", "ati", "äti", "eniec", "ence",
        "ie", "aťom", "äťom", "encom", "atám", "ätám", "iam", "ím", "ým", "encoch",
        "atách", "ätách", "iach", "ých", "aťa", "äťa", "ovia", "atá", "ätá", "aťu",
        "äťu", "ému", "iu", "iou", "ov", "at", "ät", "ä", "ého", "ý",
        "y", "ií", "ej", "ú", "é",
        "e", "om", "ami", "ám", "och", "ach", "ách", "ia", "á", "ou",
        "o", "ii", "í",
        "mi", "a", "u",
        "i",
    ];

    /// <summary>
    /// <see cref="suffixes"/> grouped by their last letter in upper case, in their original order. A suffix
    /// can only match a word that ends in the same letter, and OrdinalIgnoreCase compares upper-case forms.
    /// </summary>
    private static readonly FrozenDictionary<char, string[]> suffixesByLastLetter = suffixes
        .GroupBy(static suffix => char.ToUpperInvariant(suffix[^1]))
        .ToFrozenDictionary(static group => group.Key, static group => group.ToArray());

    /// <summary>Soft adjective endings: a comparative (novší, rýchlejší) declines with these.</summary>
    private static readonly FrozenSet<string> softAdjectiveEndings =
        new[] { "í", "ia", "ie", "ieho", "iemu", "iu", "ích", "ím", "ími", "ej" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Comparative stems in -š whose positive has another root; they keep the -š.</summary>
    private static readonly string[] irregularComparatives =
    [
        "lepš", "horš", "väčš", "menš", "krajš", "vyšš", "nižš", "bližš", "ďalš", "kratš",
        "užš", "hlbš", "ľahš", "tenš", "širš", "dlhš", "radš", "sladš", "ťažš", "hustš",
    ];

    /// <summary>Irregular forms no suffix rule can join: fleeting vowels, suppletion and palatalised plurals.</summary>
    private static readonly FrozenDictionary<string, string> exceptions = BuildExceptions();

    private static readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> exceptionLookup =
        exceptions.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly int longestException = exceptions.Keys.Max(static form => form.Length);

    private static readonly (string LongLetter, char ShortLetter)[] longToShort =
    [
        ("á", 'a'),
        ("ie", 'e'),
        ("ŕ", 'r'),
        ("í", 'i'),
        ("ú", 'u'),
        ("ô", 'o'),
    ];

    private static readonly string[] lettersRandL = ["r", "ŕ", "l", "ĺ"];

    private static readonly string[] vowels =
    [
       "a", "á", "ä", "e", "é", "i", "í", "o", "ó", "u",
       "ú", "y", "ý", "ô", "ia", "ie", "iu"
    ];

    private const string VowelLetters = "aáäeéiíoóuúyýô";

    private static FrozenDictionary<string, string> BuildExceptions()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string stem, string forms)
        {
            foreach (string form in forms.Split(' '))
            {
                map[form] = stem;
            }
        }

        Add("den", "deň dňa dňu dni dní dňom dňoch dňami");
        Add("pes", "pes psa psovi psom psi psy psov psoch psami");
        Add("ok", "oko oka oku oči očí očiam očiach očami");
        Add("uch", "uši uší ušiam ušiach ušami");
        Add("človek", "ľudia ľudí ľuďom ľuďoch ľuďmi");
        Add("diet", "deti detí deťom deťoch deťmi");
        Add("ruk", "rúk");
        Add("noh", "nôh");
        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stems the given word and returns the stemmed result as a string.
    /// </summary>
    public string Stem(string word)
    {
        if (TryGetIrregular(word, out string? irregular))
        {
            return IsAllUpper(word) ? irregular.ToUpperInvariant() : irregular;
        }

        int maxLen = word.Length;
        Span<char> buf = maxLen <= 64 ? stackalloc char[maxLen] : new char[maxLen];
        int len = Stem(word.AsSpan(), buf);
        return len < 0 ? word : new string(buf[..len]);
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (TryGetIrregular(word, out string? irregular))
        {
            if (output.Length < irregular.Length)
            {
                return -1;
            }

            bool upper = IsAllUpper(word);
            for (int i = 0; i < irregular.Length; i++)
            {
                output[i] = upper ? char.ToUpperInvariant(irregular[i]) : irregular[i];
            }

            return irregular.Length;
        }

        // No other rule lengthens a word, so a word-sized scratch buffer always holds the stem. Only the
        // stem is written to the output, and only if it fits; a shorter output is left untouched.
        char[]? rented = null;
        Span<char> scratch = word.Length <= 64 ? stackalloc char[64] : (rented = ArrayPool<char>.Shared.Rent(word.Length));
        try
        {
            int length = StemInto(word, scratch);
            if (length > output.Length)
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

    private static bool TryGetIrregular(ReadOnlySpan<char> word, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? stem)
    {
        if (word.Length > longestException)
        {
            stem = null;
            return false;
        }

        return exceptionLookup.TryGetValue(word, out stem);
    }

    /// <summary>Stems <paramref name="word"/> into <paramref name="stem"/>, which holds at least the word.</summary>
    private static int StemInto(ReadOnlySpan<char> word, Span<char> stem)
    {
        word.CopyTo(stem);
        int length = word.Length;
        if (length < 4)
        {
            // Short words keep their letters apart from a long diphthong (nôh) and a hard final consonant.
            return Harden(stem, ShortenLastLongVowel(stem, length, diphthongsOnly: true, out _));
        }

        string? suffix = FindSuffix(word);
        if (suffix is not null)
        {
            length -= suffix.Length;

            // Animate -ník plurals write k as c before -i (pracovníci); the other forms keep k.
            if (word.EndsWith("níci", StringComparison.OrdinalIgnoreCase))
            {
                stem[length - 1] = MatchCase('k', stem[length - 1]);
            }

            if (softAdjectiveEndings.Contains(suffix))
            {
                length = RemoveComparative(stem, length);
            }

            // A stem that kept a fleeting-vowel ending gets the rewrite its bare form gets, so a
            // word with a fixed vowel (kostol, kostola) still shares one term.
            if (length >= 5 && HasFleetingVowelEnding(stem, length) && !EndsWith(stem, length, "ic") && !EndsWith(stem, length, "zeň"))
            {
                length = RewriteFleetingVowel(stem, length);
            }
        }
        else
        {
            length = StemBareWord(stem, length);
        }

        // Nouns in -ém (systém, problém) lose it in every form, so systému joins systém.
        if (length >= 5 && EndsWith(stem, length, "ém"))
        {
            length -= 2;
        }

        return Harden(stem, length);
    }

    private static string? FindSuffix(ReadOnlySpan<char> word)
    {
        if (!suffixesByLastLetter.TryGetValue(char.ToUpperInvariant(word[^1]), out string[]? candidates))
        {
            return null;
        }

        foreach (string suffix in candidates)
        {
            // A suffix must leave a root: a token that is only a suffix, such as "iach", tries the next one.
            if (suffix.Length >= word.Length || !word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // "-at" is the genitive plural of neuter "-a" nouns (dievčat), but part of the root in
            // "-omat" nouns such as automat and bankomat, whose other forms keep it.
            if (suffix == "at" && word.EndsWith("omat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A root with no vowel or syllabic liquid would be over-stemmed; a shorter ending may still fit.
            if (IsOverStemming(word[..^suffix.Length]))
            {
                continue;
            }

            return suffix;
        }

        return null;
    }

    /// <summary>A word with no ending: inserted vowels, fleeting vowels, Latin -um and long vowels.</summary>
    private static int StemBareWord(Span<char> stem, int length)
    {
        int epenthesis = RemoveInsertedIe(stem, length);
        if (epenthesis != length)
        {
            return epenthesis;
        }

        if (HasFleetingVowelEnding(stem, length))
        {
            return RewriteFleetingVowel(stem, length);
        }

        if (EndsWith(stem, length, "um"))
        {
            return length - 2;
        }

        int shortened = ShortenLastLongVowel(stem, length, diphthongsOnly: true, out bool changed);
        if (changed)
        {
            return shortened;
        }

        // The genitive plural of -ina, -ita, -ica and -iha nouns lengthens the i (hodín, aktivít, ulíc, kníh).
        char last = char.ToLowerInvariant(stem[length - 1]);
        if (char.ToLowerInvariant(stem[length - 2]) == 'í' && last is ('n' or 't' or 'c' or 'h'))
        {
            stem[length - 2] = MatchCase('i', stem[length - 2]);
        }

        return length;
    }

    /// <summary>
    /// Removes an <c>ie</c> inserted between the last two consonants of a genitive plural (okno, okien;
    /// matka, matiek; farba, farieb; božstvo, božstiev).
    /// </summary>
    private static int RemoveInsertedIe(Span<char> stem, int length)
    {
        if (length < 5
            || char.ToLowerInvariant(stem[length - 3]) != 'i'
            || char.ToLowerInvariant(stem[length - 2]) != 'e'
            || char.ToLowerInvariant(stem[length - 1]) is not ('k' or 'b' or 'v' or 'n')
            || IsVowelLetter(stem[length - 4])
            || !ContainsVowel(stem[..(length - 3)]))
        {
            return length;
        }

        stem[length - 3] = stem[length - 1];
        return length - 2;
    }

    /// <summary>
    /// The fleeting vowel of -er, -ok, -ol and -ec (majster, majstra; otec, otca), -zeň (mrazeň) and
    /// -ic, which writes the k of -ik plurals as c (public).
    /// </summary>
    private static int RewriteFleetingVowel(Span<char> stem, int length)
    {
        if (EndsWith(stem, length, "ic"))
        {
            stem[length - 1] = MatchCase('k', stem[length - 1]);
            return length;
        }

        stem[length - 2] = stem[length - 1];
        return length - 1;
    }

    private static bool HasFleetingVowelEnding(Span<char> stem, int length) =>
        EndsWith(stem, length, "er") || EndsWith(stem, length, "ok") || EndsWith(stem, length, "zeň")
        || EndsWith(stem, length, "ol") || EndsWith(stem, length, "ic") || EndsWith(stem, length, "ec");

    /// <summary>
    /// Removes the comparative -ejš or -š and the superlative naj-, so novší, najnovší and rýchlejší join
    /// their positive. Irregular comparatives keep their stem.
    /// </summary>
    private static int RemoveComparative(Span<char> stem, int length)
    {
        int cut;
        if (length - 3 >= 3 && EndsWith(stem, length, "ejš"))
        {
            cut = 3;
        }
        else if (length - 1 >= 3 && EndsWith(stem, length, "š") && !IsIrregularComparative(stem[..length]))
        {
            cut = 1;
        }
        else
        {
            return length;
        }

        length -= cut;
        if (length - 3 >= 3 && stem[..length].StartsWith("naj", StringComparison.OrdinalIgnoreCase))
        {
            stem[3..length].CopyTo(stem);
            length -= 3;
        }

        return length;
    }

    private static bool IsIrregularComparative(ReadOnlySpan<char> stem)
    {
        if (stem.StartsWith("naj", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[3..];
        }

        foreach (string irregular in irregularComparatives)
        {
            if (stem.Equals(irregular, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private static bool IsOverStemming(ReadOnlySpan<char> root)
    {
        foreach (string vowel in vowels)
        {
            if (root.Contains(vowel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        foreach (string letterRandL in lettersRandL)
        {
            if (root.Contains(letterRandL, StringComparison.OrdinalIgnoreCase) && !root.EndsWith(letterRandL, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Shortens the long vowel of the last syllable (<c>ie</c>, <c>ŕ</c> and <c>ô</c>, or every long vowel
    /// when <paramref name="diphthongsOnly"/> is false) and returns the new length.
    /// </summary>
    private static int ShortenLastLongVowel(Span<char> stem, int length, bool diphthongsOnly, out bool changed)
    {
        changed = false;
        ReadOnlySpan<char> word = stem[..length];
        foreach ((string longLetter, char shortLetter) in longToShort)
        {
            if (diphthongsOnly && longLetter is not ("ie" or "ŕ" or "ô"))
            {
                continue;
            }

            int index = word.LastIndexOf(longLetter, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || ContainsVowel(word[(index + longLetter.Length)..]))
            {
                continue;
            }

            // Keep the case of the letter being shortened: MIEST becomes MEST, not MeST.
            stem[index] = MatchCase(shortLetter, stem[index]);
            changed = true;
            int removed = longLetter.Length - 1;
            if (removed > 0)
            {
                stem[(index + longLetter.Length)..length].CopyTo(stem[(index + 1)..]);
            }

            return length - removed;
        }

        return length;
    }

    private static bool ContainsVowel(ReadOnlySpan<char> text)
    {
        foreach (string vowel in vowels)
        {
            if (text.Contains(vowel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVowelLetter(char c) => VowelLetters.Contains(char.ToLowerInvariant(c));

    private static bool EndsWith(Span<char> stem, int length, string suffix) =>
        ((ReadOnlySpan<char>)stem[..length]).EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static char MatchCase(char replacement, char original) =>
        char.IsUpper(original) ? char.ToUpperInvariant(replacement) : replacement;

    private static bool IsAllUpper(ReadOnlySpan<char> word)
    {
        bool sawLetter = false;
        foreach (char c in word)
        {
            if (char.IsLower(c))
            {
                return false;
            }

            sawLetter |= char.IsLetter(c);
        }

        return sawLetter;
    }
}
