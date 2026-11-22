using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Russian stemmer implementing the official Snowball 3.2.0 <c>russian</c> algorithm
/// (<see href="https://snowballstem.org/algorithms/russian/stemmer.html"/>). Treats <c>ё</c> as <c>е</c>. Removes perfective gerund, adjectival, reflexive, verb and noun endings within the Snowball regions.
/// </summary>
/// <remarks>
/// <para>Expects lower-cased input, as <see cref="Filters.LowercaseFilter"/> produces; the language
/// analysers lower-case before stemming. Output matches the official Snowball word list exactly.</para>
/// <para>Holds no mutable state and is safe to share between threads.</para>
/// </remarks>
public sealed class RussianStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    /// <remarks>Not used: this stemmer runs the Snowball algorithm rather than suffix tables.</remarks>
    protected override (string Suffix, string Replacement)[][] Steps => NoSteps;

    /// <inheritdoc/>
    public override string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.Russian, word);

    /// <inheritdoc/>
    public override int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.Russian, word, output);
}
