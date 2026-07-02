namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Spanish Snowball-inspired stemmer. Handles common Spanish inflectional and
/// derivational suffixes. Expects lowercased, UTF-8 normalized input;
/// accented vowels (á, é, í, ó, ú) are treated as distinct characters.
/// </summary>
public sealed class SpanishStemmer : SnowballStemmer
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
