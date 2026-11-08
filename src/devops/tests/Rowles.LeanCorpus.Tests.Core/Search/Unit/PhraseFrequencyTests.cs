using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class PhraseFrequencyTests
{
    [Fact(DisplayName = "Phrase frequency: one sloppy gap contributes inverse distance")]
    public void SloppyPhraseFrequency_WeightsOneGap()
    {
        float frequency = IndexSearcher.ComputePhraseFrequency(
            new[] { new[] { 0 }, new[] { 2 } },
            new[] { 0, 1 },
            1);

        Assert.Equal(0.5f, frequency);
    }

    [Fact(DisplayName = "Phrase frequency: exact matches count every start position")]
    public void ExactPhraseFrequency_CountsEveryOccurrence()
    {
        float frequency = IndexSearcher.ComputePhraseFrequency(
            new[] { new[] { 0, 2 }, new[] { 1, 3 } },
            new[] { 0, 1 },
            0);

        Assert.Equal(2f, frequency);
    }

    [Fact(DisplayName = "Phrase frequency: exact repeated terms count overlapping occurrences")]
    public void ExactRepeatedTermPhraseFrequency_CountsOverlappingOccurrences()
    {
        float frequency = IndexSearcher.ComputePhraseFrequency(
            new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 2 } },
            new[] { 0, 1 },
            0);

        Assert.Equal(2f, frequency);
    }

    [Fact(DisplayName = "Phrase frequency: repeated terms use distinct positions")]
    public void SloppyRepeatedTermPhraseFrequency_UsesDistinctPositions()
    {
        float frequency = IndexSearcher.ComputePhraseFrequency(
            new[] { new[] { 0, 2 }, new[] { 0, 2 } },
            new[] { 0, 1 },
            1);

        Assert.Equal(0.5f, frequency);
    }

    [Fact(DisplayName = "Phrase frequency: one occurrence cannot fill repeated query slots")]
    public void SloppyRepeatedTermPhraseFrequency_RejectsReusedPosition()
    {
        float frequency = IndexSearcher.ComputePhraseFrequency(
            new[] { new[] { 0 }, new[] { 0 } },
            new[] { 0, 1 },
            1);

        Assert.Equal(0f, frequency);
    }
}
