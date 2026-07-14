namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Italian Snowball-inspired stemmer. Handles common Italian inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class ItalianStemmer : SnowballStemmer
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
