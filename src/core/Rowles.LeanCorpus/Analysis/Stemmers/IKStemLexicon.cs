namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Provides the base-form lexicon used by <see cref="KStemmer"/> to validate stem candidates.
/// </summary>
public interface IKStemLexicon
{
    /// <summary>Returns <see langword="true"/> when <paramref name="word"/> is a known base form.</summary>
    bool Contains(string word);

    /// <summary>Returns <see langword="true"/> when <paramref name="word"/> is a known base form.</summary>
    bool Contains(ReadOnlySpan<char> word);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="word"/> is a known base form.
    /// The caller guarantees the input is already lowercased, so the implementation
    /// can skip the internal <c>ToLowerInvariant</c> step.
    /// </summary>
    bool ContainsPreLowered(ReadOnlySpan<char> word) => Contains(word);
}
