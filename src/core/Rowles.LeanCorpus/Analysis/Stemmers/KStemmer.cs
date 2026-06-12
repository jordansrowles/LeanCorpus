using System.Collections.Frozen;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Lexicon-validated English stemmer inspired by Krovetz stemming.
/// </summary>
public sealed class KStemmer : ISpanStemmer
{
    private sealed record MorphRule(
        string Suffix,
        string Replacement,
        int MinRootLength,
        Func<string, bool>? StemCondition = null,
        Func<ReadOnlySpan<char>, bool>? SpanCondition = null);

    private static readonly FrozenDictionary<string, string> Exceptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["are"] = "be",
            ["been"] = "be",
            ["bled"] = "bleed",
            ["blew"] = "blow",
            ["blown"] = "blow",
            ["bore"] = "bear",
            ["borne"] = "bear",
            ["born"] = "bear",
            ["bought"] = "buy",
            ["breaks"] = "break",
            ["broken"] = "break",
            ["brought"] = "bring",
            ["built"] = "build",
            ["came"] = "come",
            ["caught"] = "catch",
            ["children"] = "child",
            ["chosen"] = "choose",
            ["did"] = "do",
            ["dies"] = "die",
            ["drew"] = "draw",
            ["drawn"] = "draw",
            ["drove"] = "drive",
            ["driven"] = "drive",
            ["dying"] = "die",
            ["fallen"] = "fall",
            ["feet"] = "foot",
            ["felt"] = "feel",
            ["flew"] = "fly",
            ["flown"] = "fly",
            ["fought"] = "fight",
            ["forgot"] = "forget",
            ["forgotten"] = "forget",
            ["forgiven"] = "forgive",
            ["froze"] = "freeze",
            ["frozen"] = "freeze",
            ["gave"] = "give",
            ["geese"] = "goose",
            ["given"] = "give",
            ["gone"] = "go",
            ["got"] = "get",
            ["gotten"] = "get",
            ["grew"] = "grow",
            ["grown"] = "grow",
            ["had"] = "have",
            ["has"] = "have",
            ["is"] = "be",
            ["knew"] = "know",
            ["known"] = "know",
            ["leaves"] = "leaf",
            ["led"] = "lead",
            ["left"] = "leave",
            ["lies"] = "lie",
            ["lives"] = "life",
            ["lying"] = "lie",
            ["made"] = "make",
            ["men"] = "man",
            ["mice"] = "mouse",
            ["pies"] = "pie",
            ["ran"] = "run",
            ["ridden"] = "ride",
            ["rode"] = "ride",
            ["rose"] = "rise",
            ["said"] = "say",
            ["saw"] = "see",
            ["seen"] = "see",
            ["shown"] = "show",
            ["skies"] = "sky",
            ["slept"] = "sleep",
            ["sought"] = "seek",
            ["spoken"] = "speak",
            ["sworn"] = "swear",
            ["taken"] = "take",
            ["taught"] = "teach",
            ["teeth"] = "tooth",
            ["ties"] = "tie",
            ["tying"] = "tie",
            ["thought"] = "think",
            ["threw"] = "throw",
            ["thrown"] = "throw",
            ["took"] = "take",
            ["torn"] = "tear",
            ["was"] = "be",
            ["went"] = "go",
            ["were"] = "be",
            ["women"] = "woman",
            ["wolves"] = "wolf",
            ["worn"] = "wear",
            ["written"] = "write",
            ["wrote"] = "write"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ProtectedWords =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "alias", "atlas", "axis", "basis", "bonus", "campus", "census", "chorus", "circus", "corpus",
            "exceed", "focus", "genus", "innings", "news", "nexus", "proceed", "proceedings", "radius",
            "status", "succeed", "syllabus", "virus"
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly MorphRule[] InflectionalRules =
    [
        new("ies", "y", 2),
        new("ies", "ie", 2),
        new("sses", "ss", 2),
        new("ches", "ch", 2),
        new("shes", "sh", 2),
        new("xes", "x", 2),
        new("zes", "z", 2),
        new("oes", "o", 2),
        new("ses", "s", 2),
        new("es", "e", 2),
        new("es", "", 2),
        new("s", "", 3,
            StemCondition: null,
            SpanCondition: static candidate =>
                !candidate.EndsWith("ss") &&
                !candidate.EndsWith("us") &&
                !candidate.EndsWith("is")),
        new("ied", "y", 2),
        new("ied", "ie", 2),
        new("ed", "e", 2),
        new("ed", "", 2,
            StemCondition: null,
            SpanCondition: ContainsVowelSpan),
        new("ing", "e", 2),
        new("ing", "", 2,
            StemCondition: null,
            SpanCondition: ContainsVowelSpan)
    ];

    private static readonly MorphRule[] DerivationalRules =
    [
        new("izations", "ize", 4),
        new("isations", "ise", 4),
        new("fulness", "ful", 3),
        new("ousness", "ous", 3),
        new("iveness", "ive", 3),
        new("ization", "ize", 4),
        new("isation", "ise", 4),
        new("ational", "ate", 4),
        new("ational", "ation", 4),
        new("alities", "al", 4),
        new("alities", "ality", 4),
        new("fulness", "", 4),
        new("fulness", "ful", 4),
        new("nesses", "", 3),
        new("iness", "y", 3),
        new("ations", "ate", 3),
        new("ations", "ation", 3),
        new("ation", "ate", 3),
        new("ation", "ation", 3),
        new("ators", "ate", 3),
        new("ator", "ate", 3),
        new("alism", "al", 4),
        new("alist", "al", 4),
        new("alists", "al", 4),
        new("alize", "al", 4),
        new("alizes", "al", 4),
        new("alized", "al", 4),
        new("alizing", "al", 4),
        new("ments", "", 3),
        new("ment", "", 3),
        new("ities", "ity", 3),
        new("ities", "", 3),
        new("ity", "", 3),
        new("izers", "ize", 3),
        new("izer", "ize", 3),
        new("isers", "ise", 3),
        new("iser", "ise", 3),
        new("izes", "ize", 3),
        new("izes", "ise", 3),
        new("ized", "ize", 3),
        new("ised", "ise", 3),
        new("izing", "ize", 3),
        new("ising", "ise", 3),
        new("ables", "able", 3),
        new("able", "", 3),
        new("ibles", "ible", 3),
        new("ible", "", 3),
        new("ership", "er", 3),
        new("ships", "ship", 3),
        new("ship", "", 4),
        new("hoods", "hood", 3),
        new("hood", "", 4),
        new("wards", "ward", 3),
        new("ward", "", 4),
        new("ings", "", 4),
        new("fully", "", 3),
        new("iness", "y", 3),
        new("ily", "y", 3),
        new("ers", "er", 3),
        new("ers", "", 3),
        new("er", "", 3),
        new("ors", "or", 3),
        new("ors", "", 3),
        new("or", "", 3),
        new("ives", "ive", 3),
        new("ive", "", 3),
        new("ous", "", 3),
        new("ful", "", 3),
        new("less", "", 3),
        new("ness", "", 3),
        new("ism", "", 3),
        new("ist", "", 3),
        new("ary", "", 4),
        new("ory", "", 4),
        new("ery", "", 4),
        new("ics", "ic", 3),
        new("ical", "ic", 3),
        new("ical", "", 3),
        new("ic", "", 3),
        new("ances", "", 4),
        new("ences", "", 4),
        new("ance", "", 4),
        new("ence", "", 4),
        new("ancies", "", 4),
        new("encies", "", 4),
        new("ancy", "", 4),
        new("ency", "", 4),
        new("als", "", 4),
        new("al", "", 4),
        new("ly", "", 4)
    ];

    private readonly IKStemLexicon _lexicon;

    /// <summary>
    /// Initialises a new <see cref="KStemmer"/> with the supplied base-form lexicon.
    /// </summary>
    /// <remarks>
    /// A lexicon file is available in the repository under <c>lexicons/kstem-dict.txt</c>.
    /// Load it with <see cref="KStemLexicon.FromFile"/>.
    /// </remarks>
    public KStemmer(IKStemLexicon lexicon)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
    }

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= 2)
        {
            word.CopyTo(output);
            return word.Length;
        }

        // Lowercase into output, then delegate to the shared core.
        int len = word.Length;
        for (int i = 0; i < len; i++)
            output[i] = char.ToLowerInvariant(word[i]);

        return StemCore(output, len);
    }

    /// <summary>
    /// Same as <c>Stem(ReadOnlySpan{char}, Span{char})</c> but assumes the input is already lowercased.
    /// Callers who have already lowercased via <see cref="Filters.LowercaseFilter"/>
    /// can use this to avoid a redundant per-character <c>ToLowerInvariant</c> pass.
    /// </summary>
    public int StemPreLowered(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= 2)
        {
            word.CopyTo(output);
            return word.Length;
        }

        // Pre-filter: all stemming suffixes end in one of these characters.
        // Words whose last character isn't in this set can't possibly be stemmed
        // and don't need the output buffer populated (caller uses len==text.Length to
        // forward the original span, skipping the buffer read).
        char last = word[word.Length - 1];
        bool mayStem = last is 's' or 'd' or 'g' or 'r' or 'y' or 't' or 'l' or 'e' or 'c' or 'm' or 'p';

        if (!mayStem)
            return word.Length;

        // Fast path: if the word is in the lexicon it won't be stemmed.
        // (ProtectedWords like "corpus" are covered by SpanConditions on the rules —
        // e.g. the "s" rule excludes endings of "us"/"ss"/"is".)
        if (_lexicon.ContainsPreLowered(word))
            return word.Length;

        // Exception words also bypass the full stemming pipeline.
        var exLookup = Exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exLookup.TryGetValue(word, out var replacement))
        {
            if (output.Length < replacement.Length) return -1;
            replacement.AsSpan().CopyTo(output);
            return replacement.Length;
        }

        // Non-dictionary word: copy into output buffer and run the stemming rules.
        word.CopyTo(output);
        return StemCore(output, word.Length);
    }

    /// <summary>Core stemming logic operating on the already-lowered word in <paramref name="output"/>.</summary>
    private int StemCore(Span<char> output, int len)
    {
        var lowered = output[..len];

        // Check exceptions.
        var exLookup = Exceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exLookup.TryGetValue(lowered, out var replacement))
        {
            if (output.Length < replacement.Length) return -1;
            replacement.AsSpan().CopyTo(output);
            return replacement.Length;
        }

        // Check lexicon (ProtectedWords handled by rule SpanConditions).
        if (_lexicon.ContainsPreLowered(lowered))
            return len;

        // Apply inflectional rules, then derivational.
        if (TryRulesSpan(output, len, InflectionalRules, out int inflectedLen))
            len = inflectedLen;

        if (TryRulesSpan(output, len, DerivationalRules, out int derivedLen))
            len = derivedLen;

        return len;
    }

    /// <summary>
    /// Convenience overload returning a stemmed string.
    /// </summary>
    public string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        int bufSize = word.Length + 4; // +4 for rare expansion (e.g. "mice"→"mouse")
        Span<char> buf = bufSize <= 128
            ? stackalloc char[bufSize]
            : new char[bufSize];
        int len = Stem(word.AsSpan(), buf);
        return new string(buf[..len]);
    }

    private bool TryRulesSpan(Span<char> output, int len, IReadOnlyList<MorphRule> rules, out int resultLen)
    {
        var word = output[..len];

        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!word.EndsWith(rule.Suffix))
                continue;

            int rootLength = len - rule.Suffix.Length;
            if (rootLength < rule.MinRootLength)
                continue;

            var root = word[..rootLength];

            // Build candidate in a stackalloc buffer.
#pragma warning disable CA2014 // stackalloc in loop — bounded by word length (<128 typically)
            Span<char> candBuf = stackalloc char[len];
#pragma warning restore CA2014
            root.CopyTo(candBuf);
            int candLen = rootLength;

            if (rule.Replacement.Length > 0)
            {
                rule.Replacement.AsSpan().CopyTo(candBuf[candLen..]);
                candLen += rule.Replacement.Length;
            }

            var candidate = candBuf[..candLen];

            // Check StemCondition (string-based, backwards compat) or SpanCondition (zero-alloc).
            if (rule.SpanCondition is not null)
            {
                if (!rule.SpanCondition(candidate))
                    continue;
            }
            else if (rule.StemCondition is not null)
            {
                string candStr = candidate.ToString();
                if (!rule.StemCondition(candStr))
                    continue;
            }

            if (_lexicon.ContainsPreLowered(candidate))
            {
                candidate.CopyTo(output);
                resultLen = candLen;
                return true;
            }

            // Try undoubled.
            int undLen = UndoubleTrailingConsonantSpan(candBuf, candLen);
            if (undLen != candLen && _lexicon.ContainsPreLowered(candBuf[..undLen]))
            {
                candBuf[..undLen].CopyTo(output);
                resultLen = undLen;
                return true;
            }

            // Try with 'e' appended.
            if (candLen < candBuf.Length)
            {
                candBuf[candLen] = 'e';
                int withELen = candLen + 1;
                if (_lexicon.ContainsPreLowered(candBuf[..withELen]))
                {
                    candBuf[..withELen].CopyTo(output);
                    resultLen = withELen;
                    return true;
                }
            }
        }

        resultLen = 0;
        return false;
    }

    private static int UndoubleTrailingConsonantSpan(Span<char> value, int len)
    {
        if (len < 2)
            return len;

        char last = value[len - 1];
        if (last == value[len - 2] && !IsVowel(last) && last is not ('l' or 's' or 'z'))
            return len - 1;

        return len;
    }

    private static bool ContainsVowel(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (IsVowel(value[i]))
                return true;
        }

        return false;
    }

    private static bool ContainsVowelSpan(ReadOnlySpan<char> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (IsVowel(value[i]))
                return true;
        }

        return false;
    }

    private static bool IsVowel(char value)
        => value is 'a' or 'e' or 'i' or 'o' or 'u';
}
