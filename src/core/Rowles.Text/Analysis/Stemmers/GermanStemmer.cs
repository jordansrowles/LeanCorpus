using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// German stemmer implementing the official Snowball 3.2.0 <c>german</c> algorithm
/// (<see href="https://snowballstem.org/algorithms/german/stemmer.html"/>). Folds <c>ä</c>, <c>ö</c>, <c>ü</c> and the <c>ae</c>, <c>oe</c>, <c>ue</c> spellings, and expands <c>ß</c> to <c>ss</c>, so a stem can be longer than its word (<c>überläßt</c> becomes <c>uberlasst</c>).
/// </summary>
/// <remarks>
/// <para>Expects lower-cased input, as <see cref="Filters.LowercaseFilter"/> produces; the language
/// analysers lower-case before stemming. Output matches the official Snowball word list exactly.</para>
/// <para>Holds no mutable state and is safe to share between threads.</para>
/// </remarks>
public sealed class GermanStemmer : SnowballStemmer
{
    /// <inheritdoc/>
    /// <remarks>Not used: this stemmer runs the Snowball algorithm rather than suffix tables.</remarks>
    protected override (string Suffix, string Replacement)[][] Steps => NoSteps;

    /// <inheritdoc/>
    public override string Stem(string word) => SnowballPort.Stem(SnowballAlgorithm.German, word);

    /// <inheritdoc/>
    public override int Stem(ReadOnlySpan<char> word, Span<char> output)
        => SnowballPort.Stem(SnowballAlgorithm.German, word, output);
}
