using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>Adapter that exposes Porter stemming through <see cref="ISpanStemmer"/>.</summary>
public sealed class PorterStemmer : ISpanStemmer
{
    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output) =>
        PorterStemmerFilter.Stem(word, output);

    /// <summary>Convenience overload returning a stemmed string.</summary>
    public string Stem(string word) => PorterStemmerFilter.Stem(word);
}
