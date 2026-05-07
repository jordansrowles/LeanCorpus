namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Chinese stemmer — identity implementation.
/// </summary>
/// <remarks>
/// <para>
/// Mandarin Chinese is an isolating language: words do not inflect via suffixes,
/// so suffix-stripping stemming is linguistically inappropriate. The morphological
/// unit in Chinese is the character (字) or multi-character word (词), not a stem
/// produced by affix removal.
/// </para>
/// <para>
/// Meaningful normalisation for Chinese search involves:
/// <list type="bullet">
///   <item>Word segmentation (e.g. jieba, Lucene's CJK analyser, or a dictionary-based tokeniser)</item>
///   <item>Simplified ↔ Traditional character conversion</item>
///   <item>Full-width → half-width normalisation</item>
/// </list>
/// This class is provided so the <c>IStemmer</c> pipeline compiles uniformly
/// across all supported languages. Wire up proper segmentation as a pre-tokenisation
/// step before passing tokens here.
/// </para>
/// </remarks>
public sealed class ChineseStemmer : IStemmer
{
    /// <inheritdoc/>
    /// <remarks>Returns <paramref name="word"/> unchanged.</remarks>
    public string Stem(string word) => word;
}
