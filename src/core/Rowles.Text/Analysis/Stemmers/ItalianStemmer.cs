using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Italian stemmer implementing the official Snowball 3.2.0 <c>italian</c> algorithm
/// (<see href="https://snowballstem.org/algorithms/italian/stemmer.html"/>). Removes attached pronouns, standard and verb suffixes within the Snowball regions, and normalises accented vowels.
/// </summary>
/// <remarks>
/// <para>Expects lower-cased input, as <see cref="Filters.LowercaseFilter"/> produces; the language
/// analysers lower-case before stemming. Output matches the official Snowball word list exactly.</para>
/// <para>Holds no mutable state and is safe to share between threads.</para>
/// </remarks>
public sealed class ItalianStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    /// <remarks>Not used: this stemmer runs the Snowball algorithm rather than suffix tables.</remarks>
    protected override (string Suffix, string Replacement)[][] Steps => NoSteps;

    /// <inheritdoc/>
    public override string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.Italian, word);

    /// <inheritdoc/>
    public override int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.Italian, word, output);
}
