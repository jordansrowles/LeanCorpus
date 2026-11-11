namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// French Snowball-inspired stemmer. Handles common French suffixes.
/// </summary>
public sealed class FrenchStemmer : SnowballStemmer
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
