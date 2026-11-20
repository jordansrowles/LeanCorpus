using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Arabic stemmer implementing the official Snowball 3.2.0 <c>arabic</c> algorithm
/// (<see href="https://snowballstem.org/algorithms/arabic/stemmer.html"/>). It normalises
/// diacritics, hamza forms and Arabic-Indic digits, then removes noun and verb prefixes and suffixes.
/// It does not perform root extraction.
/// </summary>
/// <remarks>Holds no mutable state and is safe to share between threads.</remarks>
public sealed class ArabicStemmer : IShareableSpanStemmer
{
    /// <summary>Convenience overload returning a stemmed string.</summary>
    public string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.Arabic, word);

    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.Arabic, word, output);
}
