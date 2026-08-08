namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Portuguese Snowball-inspired stemmer. Handles common Portuguese inflectional
/// and derivational suffixes. Covers both European (pt-PT) and Brazilian (pt-BR)
/// variants. Expects lowercased, UTF-8 normalized input.
/// </summary>
public sealed class PortugueseStemmer : SnowballStemmer
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
