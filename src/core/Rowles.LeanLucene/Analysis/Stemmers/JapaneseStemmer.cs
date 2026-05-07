namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Japanese stemmer — identity implementation.
/// </summary>
/// <remarks>
/// <para>
/// Japanese morphology is handled by conjugation paradigms (活用), not detachable
/// suffixes of the kind that a Snowball-style stemmer can reliably strip. Inflected
/// verb and adjective forms are interleaved with auxiliary morphemes (e.g. 食べ<b>られ</b>る,
/// 走<b>って</b>いる) that require a full morphological analyser to decompose correctly.
/// </para>
/// <para>
/// Recommended pre-processing for Japanese search:
/// <list type="bullet">
///   <item>Morphological analysis with MeCab, Kuromoji (Lucene's <c>JapaneseAnalyzer</c>), or SudachiPy</item>
///   <item>Lemmatisation using the analyser's dictionary base-form output</item>
///   <item>Kana normalisation (hiragana ↔ katakana, full-width → half-width)</item>
/// </list>
/// This class is provided so the <c>IStemmer</c> pipeline compiles uniformly
/// across all supported languages.
/// </para>
/// </remarks>
public sealed class JapaneseStemmer : IStemmer
{
    /// <inheritdoc/>
    /// <remarks>Returns <paramref name="word"/> unchanged.</remarks>
    public string Stem(string word) => word;
}
