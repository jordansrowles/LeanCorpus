using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// French stemmer implementing the official Snowball 3.2.0 <c>french</c> algorithm
/// (<see href="https://snowballstem.org/algorithms/french/stemmer.html"/>). Handles elision forms such as <c>l'</c> and <c>qu'</c>, and marks vowels used as consonants before applying the standard, residual and verb suffix steps.
/// </summary>
/// <remarks>
/// <para>Expects lower-cased input, as <see cref="Filters.LowercaseFilter"/> produces; the language
/// analysers lower-case before stemming. Output matches the official Snowball word list exactly.</para>
/// <para>Holds no mutable state and is safe to share between threads.</para>
/// </remarks>
public sealed class FrenchStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    /// <remarks>Not used: this stemmer runs the Snowball algorithm rather than suffix tables.</remarks>
    protected override (string Suffix, string Replacement)[][] Steps => NoSteps;

    /// <inheritdoc/>
    public override string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.French, word);

    /// <inheritdoc/>
    public override int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.French, word, output);
}
