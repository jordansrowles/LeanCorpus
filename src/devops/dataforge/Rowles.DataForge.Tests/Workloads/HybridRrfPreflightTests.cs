using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.DataForge.Tests.Workloads;

/// <summary>
/// The hybrid benchmark's independent RRF check. Two disjoint 50-document branches fuse into pairs
/// of equal scores, one pair per rank, so a 25-document page cuts the 13th pair: either of its members
/// is a valid result under RRF's unspecified tie order, but no other document is.
/// </summary>
public sealed class HybridRrfPreflightTests
{
    private const int K = 60;
    private const int BranchLength = 50;

    private static ScoreDoc[][] DisjointBranches() =>
    [
        Enumerable.Range(0, BranchLength).Select(static rank => new ScoreDoc(rank, 0f)).ToArray(),
        Enumerable.Range(0, BranchLength).Select(static rank => new ScoreDoc(1_000 + rank, 0f)).ToArray(),
    ];

    [Fact]
    public void ReferenceKeepsEveryFusedDocument()
    {
        var complete = HybridSearchBenchmarks.FuseReciprocalRanks(DisjointBranches(), K);

        Assert.Equal(2 * BranchLength, complete.Length);
        Assert.True(HybridSearchBenchmarks.SameScore(complete[24].Score, complete[25].Score));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(1_012)]
    public void CutTie_AcceptsEitherMember(int boundaryDocument)
    {
        var complete = HybridSearchBenchmarks.FuseReciprocalRanks(DisjointBranches(), K);
        var page = Page(complete, 25, boundaryDocument);

        HybridSearchBenchmarks.CompareFusedOrder("cut-tie", page, complete, 25);
    }

    [Fact]
    public void CutTie_RejectsAnOutsider()
    {
        var complete = HybridSearchBenchmarks.FuseReciprocalRanks(DisjointBranches(), K);
        var page = Page(complete, 25, boundaryDocument: 12);
        page[^1] = new ScoreDoc(13, page[^1].Score);

        Assert.Throws<InvalidOperationException>(() => HybridSearchBenchmarks.CompareFusedOrder("cut-tie", page, complete, 25));
    }

    [Fact]
    public void CutTie_RejectsADuplicate()
    {
        var complete = HybridSearchBenchmarks.FuseReciprocalRanks(DisjointBranches(), K);
        var page = Page(complete, 26, boundaryDocument: 12);
        page[^1] = new ScoreDoc(page[^2].DocId, page[^1].Score);

        Assert.Throws<InvalidOperationException>(() => HybridSearchBenchmarks.CompareFusedOrder("cut-tie", page, complete, 26));
    }

    [Fact]
    public void CompleteTieGroup_RejectsAnotherMemberSet()
    {
        var complete = HybridSearchBenchmarks.FuseReciprocalRanks(DisjointBranches(), K);
        var page = Page(complete, 26, boundaryDocument: 12);
        page[0] = new ScoreDoc(12, page[0].Score);

        Assert.Throws<InvalidOperationException>(() => HybridSearchBenchmarks.CompareFusedOrder("complete-tie", page, complete, 26));
    }

    /// <summary>
    /// The engine's page: the reference order, with the final cut pair resolved to
    /// <paramref name="boundaryDocument"/> when the page ends inside it.
    /// </summary>
    private static ScoreDoc[] Page((int DocId, float Score)[] complete, int topN, int boundaryDocument)
    {
        var page = complete.Take(topN).Select(static item => new ScoreDoc(item.DocId, item.Score)).ToArray();
        if (topN % 2 == 1)
            page[^1] = new ScoreDoc(boundaryDocument, page[^1].Score);
        return page;
    }
}
