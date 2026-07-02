namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Dutch Snowball-inspired stemmer. Handles common Dutch inflectional and
/// derivational suffixes. Expects lowercased input. Dutch vowel sequences (ij, oe,
/// eu, ui) are not decomposed here; apply normalisation upstream if needed.
/// </summary>
public sealed class DutchStemmer : SnowballStemmer
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
