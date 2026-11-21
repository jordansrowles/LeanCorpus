using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.Text.Tests;

// The suffix tables the built-in language stemmers used before #122, kept unchanged as test-only
// SnowballStemmer subclasses. They pin the suffix-table engine that custom stemmers still derive from.

/// <summary>The pre-#122 Dutch suffix tables, unchanged.</summary>
internal sealed class LegacyDutchSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("heden", "heid"), ("heid", ""), ("ingen", ""), ("ing", ""),
            ("lijk", ""), ("baar", ""), ("zaam", ""), ("ster", ""),
            ("achtig", ""), ("erij", ""), ("isme", ""), ("ist", ""),
        ],
        [
            ("enden", ""), ("ende", ""), ("tten", "t"), ("dden", "d"),
            ("ten", ""), ("den", ""), ("tte", "t"), ("dde", "d"),
            ("te", ""), ("de", ""),
        ],
        [
            ("eren", ""), ("ens", ""), ("ers", ""), ("en", ""),
            ("es", ""), ("s", ""),
        ],
    ];

    /// <inheritdoc/>
    protected override int Postprocess(Span<char> buf, int len)
    {
        if (len > 3 && buf[len - 1] == 'e')
            len--;
        return len;
    }
}

/// <summary>The pre-#122 French suffix tables, unchanged.</summary>
internal sealed class LegacyFrenchSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override int MinStemGuard => 2;

    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("issements", ""), ("issement", ""), ("ements", ""), ("ement", ""),
            ("ations", ""), ("ation", ""), ("euses", ""), ("euse", ""),
            ("eurs", ""), ("eur", ""), ("eux", ""), ("ités", ""), ("ité", ""),
            ("ives", ""), ("ive", ""), ("ifs", ""), ("if", ""),
            ("aux", "al"),
        ],
        [
            ("issent", ""), ("issons", ""), ("issez", ""), ("issait", ""), ("issant", ""),
            ("irent", ""), ("eront", ""), ("erons", ""), ("erez", ""), ("aient", ""),
            ("ait", ""), ("ais", ""), ("ent", ""), ("ez", ""), ("er", ""), ("ir", ""),
            ("es", ""), ("és", ""), ("ées", ""), ("ée", ""), ("é", ""), ("it", ""),
        ],
        [
            ("s", ""),
        ],
    ];

    /// <inheritdoc/>
    protected override int Postprocess(Span<char> buf, int len)
    {
        if (len > 2 && buf[len - 1] == 'e')
            len--;
        return len;
    }
}

/// <summary>The pre-#122 German suffix tables, unchanged.</summary>
internal sealed class LegacyGermanSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("erungen", ""), ("erung", ""), ("ungen", ""), ("ung", ""),
            ("heiten", ""), ("heits", ""), ("heit", ""), ("keiten", ""),
            ("keit", ""), ("schaften", ""), ("schaft", ""), ("ismus", ""),
            ("isten", ""), ("ist", ""),
        ],
        [
            ("lichen", ""), ("liche", ""), ("licher", ""), ("lichem", ""),
            ("liches", ""), ("lich", ""),
            ("ischen", ""), ("ische", ""), ("ischer", ""), ("ischem", ""),
            ("isches", ""), ("isch", ""),
            ("igen", ""), ("ige", ""), ("iger", ""), ("igem", ""),
            ("iges", ""), ("ig", ""),
        ],
        [
            ("test", ""), ("etet", ""), ("est", ""), ("tet", ""),
            ("et", ""), ("te", ""), ("nd", ""),
        ],
        [
            ("innen", ""), ("erns", ""), ("ern", ""), ("ens", ""),
            ("ers", ""), ("en", ""), ("em", ""), ("es", ""),
            ("er", ""), ("e", ""), ("s", ""),
        ],
    ];

    /// <inheritdoc/>
    protected override int MaxOutputLength(string word)
    {
        int len = word.Length;
        for (int i = 0; i < word.Length; i++) if (word[i] == 'ß') len++;
        return len;
    }

    /// <inheritdoc/>
    protected override int Preprocess(ReadOnlySpan<char> word, Span<char> output)
    {
        int len = 0;
        foreach (var ch in word)
        {
            switch (ch)
            {
                case 'ä': output[len++] = 'a'; break;
                case 'ö': output[len++] = 'o'; break;
                case 'ü': output[len++] = 'u'; break;
                case 'ß': output[len++] = 's'; output[len++] = 's'; break;
                default: output[len++] = ch; break;
            }
        }
        return len;
    }
}

/// <summary>The pre-#122 Italian suffix tables, unchanged.</summary>
internal sealed class LegacyItalianSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("azioni", ""), ("azione", ""), ("amenti", ""), ("amento", ""),
            ("imenti", ""), ("imento", ""), ("ità", ""), ("mente", ""),
            ("ismi", ""), ("ismo", ""), ("isti", ""), ("ista", ""),
            ("ibili", ""), ("ibile", ""), ("abili", ""), ("abile", ""),
        ],
        [
            ("andosi", ""), ("endosi", ""), ("ando", ""), ("endo", ""),
            ("arono", ""), ("erono", ""), ("irono", ""), ("ati", ""),
            ("ute", ""), ("uti", ""), ("ite", ""), ("iti", ""),
            ("ate", ""), ("ato", ""), ("uta", ""), ("uto", ""),
            ("ita", ""), ("ito", ""), ("avano", ""), ("evano", ""),
            ("ivano", ""), ("anno", ""), ("erei", ""), ("irei", ""),
            ("arsi", ""), ("ersi", ""), ("irsi", ""), ("are", ""),
            ("ere", ""), ("ire", ""),
        ],
        [
            ("osi", ""), ("ose", ""), ("i", ""), ("e", ""), ("a", ""), ("o", ""),
        ],
    ];
}

/// <summary>The pre-#122 Portuguese suffix tables, unchanged.</summary>
internal sealed class LegacyPortugueseSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("amentos", ""), ("imento", ""), ("amento", ""), ("imentos", ""),
            ("ações", ""), ("ação", ""), ("idades", ""), ("idade", ""),
            ("mente", ""), ("ismos", ""), ("ismo", ""), ("istas", ""),
            ("ista", ""), ("áveis", ""), ("ável", ""), ("íveis", ""),
            ("ível", ""),
        ],
        [
            ("ando", ""), ("endo", ""), ("indo", ""),
            ("aram", ""), ("eram", ""), ("iram", ""),
            ("adas", ""), ("idas", ""), ("ados", ""), ("idos", ""),
            ("ada", ""), ("ida", ""), ("ido", ""), ("ado", ""),
            ("avam", ""), ("amos", ""), ("emos", ""), ("imos", ""),
            ("ava", ""), ("ias", ""), ("ia", ""),
            ("ar", ""), ("er", ""), ("ir", ""),
        ],
        [
            ("os", ""), ("as", ""), ("es", ""), ("o", ""), ("a", ""),
        ],
    ];
}

/// <summary>The pre-#122 Russian suffix tables, unchanged.</summary>
internal sealed class LegacyRussianSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override int MinWordLength => 2;

    /// <inheritdoc/>
    protected override int MinStemGuard => 2;

    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        // Step 1: Perfective gerunds
        [
            ("ывшись", ""), ("ившись", ""), ("ывши", ""), ("ивши", ""),
            ("вшись", ""), ("вши", ""),
        ],
        // Step 2: Reflexive endings
        [
            ("ться", ""), ("тся", ""),
        ],
        // Step 3: Adjective / participle endings
        [
            ("ующего", ""), ("ующему", ""), ("ующими", ""), ("ующих", ""),
            ("ующим", ""), ("ующей", ""), ("ующую", ""), ("ующее", ""),
            ("ующие", ""), ("ующий", ""), ("ующая", ""),
            ("ованного", ""), ("ованному", ""), ("ованными", ""), ("ованных", ""),
            ("ованным", ""), ("ованной", ""), ("ованную", ""), ("ованное", ""),
            ("ованные", ""), ("ованный", ""), ("ованная", ""), ("ованно", ""),
            ("ованн", ""),
            ("ого", ""), ("ому", ""), ("ыми", ""), ("ими", ""),
            ("ых", ""), ("их", ""), ("ым", ""), ("им", ""),
            ("ей", ""), ("ой", ""), ("ую", ""), ("ые", ""),
            ("ие", ""), ("ый", ""), ("ий", ""), ("ая", ""),
            ("яя", ""),
        ],
        // Step 4: Verb endings
        [
            ("ывайте", ""), ("ивайте", ""), ("ывать", ""), ("ивать", ""),
            ("ываю", ""), ("иваю", ""), ("овать", ""), ("евать", ""),
            ("уйте", ""), ("ейте", ""), ("ите", ""), ("ешь", ""),
            ("ишь", ""), ("ают", ""), ("яют", ""), ("ют", ""),
            ("ут", ""), ("ал", ""), ("ял", ""), ("ала", ""),
            ("яла", ""), ("али", ""), ("яли", ""), ("ать", ""),
            ("ять", ""), ("ить", ""),
        ],
        // Step 5: Noun endings
        [
            ("ости", ""), ("ость", ""), ("ений", ""), ("ения", ""),
            ("ению", ""), ("ение", ""), ("аний", ""), ("ания", ""),
            ("анию", ""), ("ание", ""),
            ("ами", ""), ("ями", ""), ("ах", ""), ("ях", ""),
            ("ам", ""), ("ям", ""), ("ов", ""), ("ев", ""),
            ("ей", ""), ("ий", ""), ("ая", ""), ("яя", ""),
            ("ом", ""), ("ем", ""), ("и", ""), ("е", ""),
            ("а", ""), ("я", ""), ("ы", ""), ("у", ""),
            ("ю", ""),
        ],
        // Step 6: Derivational -ость / -ь
        [
            ("ость", ""), ("ь", ""),
        ],
    ];
}

/// <summary>The pre-#122 Spanish suffix tables, unchanged.</summary>
internal sealed class LegacySpanishSuffixStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    protected override (string Suffix, string Replacement)[][] Steps { get; } =
    [
        [
            ("amientos", ""), ("imientos", ""), ("amiento", ""), ("imiento", ""),
            ("aciones", ""), ("ución", ""), ("uciones", ""), ("ación", ""),
            ("idades", ""), ("idad", ""), ("mente", ""), ("ismos", ""),
            ("ismo", ""), ("istas", ""), ("ista", ""), ("ibles", ""),
            ("ible", ""), ("ables", ""), ("able", ""),
        ],
        [
            ("ándose", ""), ("iéndose", ""), ("ándome", ""), ("ando", ""),
            ("iendo", ""), ("aron", ""), ("ieron", ""),
            ("adas", ""), ("idas", ""), ("ados", ""), ("idos", ""),
            ("ada", ""), ("ida", ""), ("ado", ""), ("ido", ""),
            ("aban", ""), ("ían", ""), ("arán", ""), ("erán", ""),
            ("irán", ""), ("aré", ""), ("eré", ""), ("iré", ""),
            ("amos", ""), ("emos", ""), ("imos", ""),
            ("abas", ""), ("aba", ""), ("ías", ""), ("ía", ""),
            ("ar", ""), ("er", ""), ("ir", ""),
        ],
        [
            ("os", ""), ("as", ""), ("es", ""), ("o", ""), ("a", ""),
        ],
    ];
}
