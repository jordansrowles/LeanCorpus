using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// The direct exact-phrase counters must equal the general exact helper for strictly increasing
/// query positions: every occurrence counts, overlaps included, and duplicate positions count once.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class StrictExactPhraseCounterTests
{
    [Theory]
    [InlineData(new[] { 1, 3, 5 }, new[] { 2, 4, 6 }, 1, 3)]
    [InlineData(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }, 1, 2)]
    [InlineData(new[] { 4, 4, 9 }, new[] { 5, 5 }, 1, 1)]
    [InlineData(new[] { 0 }, new[] { 2 }, 1, 0)]
    [InlineData(new[] { 0 }, new[] { 2 }, 2, 1)]
    [InlineData(new[] { int.MaxValue }, new[] { int.MaxValue }, 1, 0)]
    [InlineData(new int[0], new[] { 1 }, 1, 0)]
    public void TwoTerms_CountsEveryOccurrence(int[] first, int[] second, int offset, int expected)
    {
        Assert.Equal(expected, IndexSearcher.CountStrictExactPhrase(first, second, offset));
        if (first.Length > 0 && second.Length > 0)
            Assert.Equal(expected, General([first, second], [0, offset]));
    }

    [Fact]
    public void RandomisedLists_MatchTheGeneralExactHelper()
    {
        var random = new Random(20261118); // NOSONAR: deterministic test data, not security sensitive
        for (int iteration = 0; iteration < 5000; iteration++)
        {
            int termCount = random.Next(2, 4);
            var queryPositions = new int[termCount];
            for (int i = 1; i < termCount; i++)
                queryPositions[i] = queryPositions[i - 1] + random.Next(1, 4);

            bool repeatedTerm = random.Next(4) == 0;
            var lists = new int[termCount][];
            for (int i = 0; i < termCount; i++)
                lists[i] = repeatedTerm && i > 0 ? lists[0] : RandomPositions(random);

            int expected = General(lists, queryPositions);
            int actual = termCount == 2
                ? IndexSearcher.CountStrictExactPhrase(lists[0], lists[1], queryPositions[1])
                : IndexSearcher.CountStrictExactPhrase(lists[0], lists[1], lists[2], queryPositions[1], queryPositions[2]);
            Assert.Equal(expected, actual);
        }
    }

    private static int[] RandomPositions(Random random)
    {
        int length = random.Next(1, 12);
        var positions = new int[length];
        int position = random.Next(0, 4);
        for (int i = 0; i < length; i++)
        {
            positions[i] = position;
            // A zero step leaves a duplicate physical position in the list.
            position += random.Next(0, 3);
        }

        return positions;
    }

    // Exact frequencies are whole occurrence counts, so the conversion is lossless.
    private static int General(int[][] lists, int[] queryPositions)
        => (int)MathF.Round(IndexSearcher.ComputePhraseFrequency(lists, queryPositions, slop: 0));
}
