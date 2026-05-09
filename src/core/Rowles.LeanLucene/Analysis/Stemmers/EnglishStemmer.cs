namespace Rowles.LeanLucene.Analysis.Stemmers;

/// <summary>
/// English stemmer wrapping the existing Porter stemmer implementation.
/// </summary>
public sealed class EnglishStemmer : IStemmer
{
    /// <inheritdoc/>
    public string Stem(string word) => PorterStemmerFilter.Stem(word);
}
