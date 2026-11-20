using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// English stemmer implementing the official Snowball 3.2.0 <c>english</c> algorithm, also known as
/// Porter2 (<see href="https://snowballstem.org/algorithms/english/stemmer.html"/>).
/// <c>AnalyserFactory.Create("en")</c> uses it.
/// </summary>
/// <remarks>
/// <para>Porter2 replaces the original 1980 Porter algorithm used before LeanCorpus 4.0. For the
/// original behaviour, use <see cref="PorterStemmer"/>, <see cref="Filters.PorterStemmerFilter"/> or
/// <see cref="Analysers.StemmerAnalyser.Porter"/>.</para>
/// <para>Expects lower-cased input. Holds no mutable state and is safe to share between threads.</para>
/// </remarks>
public sealed class EnglishStemmer : IShareableSpanStemmer
{
    /// <inheritdoc/>
    public int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.English, word, output);

    /// <summary>Convenience overload returning a stemmed string.</summary>
    public string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.English, word);
}
