namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// Stemming contract. Implementations reduce a word to its root form.
/// </summary>
public interface IStemmer
{
    /// <summary>Returns the stemmed form of the word.</summary>
    string Stem(string word);
}
