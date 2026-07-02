namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// German Snowball-inspired stemmer. Handles common German inflectional and
/// derivational suffixes. Folds umlauts (ä→a, ö→o, ü→u) and ß→ss as a
/// preliminary step, mirroring Snowball's approach for German.
/// </summary>
public sealed class GermanStemmer : SnowballStemmer
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
