namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// English stemmer wrapping the existing Porter stemmer implementation.
/// </summary>
public sealed class EnglishStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
        => PorterStemmerFilter.Stem(word, output);

    /// <summary>Convenience overload returning a stemmed string.</summary>
    public string Stem(string word) => PorterStemmerFilter.Stem(word);
}
