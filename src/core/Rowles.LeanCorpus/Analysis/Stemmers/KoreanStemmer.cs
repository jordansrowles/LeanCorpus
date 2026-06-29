namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Korean stemmer — identity implementation.
/// </summary>
/// <remarks>
/// <para>
/// Korean is a highly agglutinative language where grammatical information
/// (tense, case, honorific level, negation, aspect) is encoded in chains of
/// bound morphemes attached to a content root — for example, 먹다 (eat) →
/// 먹었습니다, 먹고 싶어요, 먹히다. The boundaries between morphemes can require
/// phonological rules (e.g. consonant assimilation) that cannot be resolved
/// by simple string suffix removal.
/// </para>
/// <para>
/// Recommended pre-processing for Korean search:
/// <list type="bullet">
///   <item>POS-tagging and morpheme segmentation with Mecab-ko, Komoran, or Nori</item>
///   <item>Lemmatisation to dictionary base form (원형)</item>
///   <item>Jamo decomposition for sub-syllable indexing when required</item>
/// </list>
/// This class is provided so the <c>ISpanStemmer</c> pipeline compiles uniformly
/// across all supported languages.
/// </para>
/// </remarks>
public sealed class KoreanStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    /// <remarks>Returns <paramref name="word"/> unchanged.</remarks>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        word.CopyTo(output);
        return word.Length;
    }

    /// <summary>Convenience overload returning the stemmed string.</summary>
    /// <remarks>Returns <paramref name="word"/> unchanged.</remarks>
    public string Stem(string word) => word;
}
