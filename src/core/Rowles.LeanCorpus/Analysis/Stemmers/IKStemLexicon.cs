namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Provides the base-form lexicon used by <see cref="KStemmer"/> to validate stem candidates.
/// </summary>
public interface IKStemLexicon
{
    /// <summary>Returns <see langword="true"/> when <paramref name="word"/> is a known base form.</summary>
    bool Contains(string word);
}
