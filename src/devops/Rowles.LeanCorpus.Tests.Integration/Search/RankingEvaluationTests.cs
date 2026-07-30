using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

public sealed class RankingEvaluationTests
{
    [Fact]
    public void Metrics_MatchHandCalculatedJudgementsAndReportUnjudged()
    {
        var metrics = RankingMetrics.Calculate(["a", "b", "c"], [
            new RelevanceJudgement("q", "a", 3),
            new RelevanceJudgement("q", "c", 1),
            new RelevanceJudgement("q", "d", 2)], 3);
        Assert.Equal(1d, metrics.PrecisionAtK, 6);
        Assert.Equal(2d / 3d, metrics.RecallAtK, 6);
        Assert.Equal(1d, metrics.ReciprocalRank, 6);
        Assert.Equal(2, metrics.JudgedResults);
        Assert.Equal(1, metrics.UnjudgedResults);
        Assert.InRange(metrics.Ndcg, 0, 1);
    }

    [Fact]
    public void Mmr_ZeroNoveltyPreservesRelevanceOrder()
    {
        var selected = MaximumMarginalRelevance.Select([
            new ScoreDoc(3, 4), new ScoreDoc(1, 3), new ScoreDoc(2, 2)], 3, 1,
            static (_, _) => 1);
        Assert.Equal([3, 1, 2], selected.Select(static s => s.Document.DocId));
    }

    [Fact]
    public void Mmr_DiversifiesSimilarCandidate()
    {
        var selected = MaximumMarginalRelevance.Select([
            new ScoreDoc(10, 10), new ScoreDoc(11, 9), new ScoreDoc(12, 8)], 2, .25f,
            static (left, right) => left == 11 || right == 11 ? .99f : 0f);
        Assert.Equal([10, 12], selected.Select(static s => s.Document.DocId));
        Assert.Equal(0f, selected[0].MaximumSimilarity);
        Assert.Equal(0f, selected[1].MaximumSimilarity);
    }

    [Fact]
    public void Mmr_MissingSimilarityCanExcludeCandidates()
    {
        var selected = MaximumMarginalRelevance.Select([
            new ScoreDoc(1, 2), new ScoreDoc(2, 1)], 2, .5f,
            static (_, _) => null, MissingSimilarityPolicy.ExcludeCandidate);
        Assert.Single(selected);
        Assert.Equal(1, selected[0].Document.DocId);
    }
}
