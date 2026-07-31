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
            ("eurs", ""), ("eur", ""), ("ités", ""), ("ité", ""),
            ("ives", ""), ("ive", ""), ("ifs", ""), ("if", ""),
            ("aux", "al"),
        ],
        [
            ("issent", ""), ("issons", ""), ("issez", ""), ("irent", ""),
            ("eront", ""), ("erons", ""), ("erez", ""), ("ent", ""),
            ("ons", ""), ("ez", ""), ("er", ""), ("es", ""),
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
