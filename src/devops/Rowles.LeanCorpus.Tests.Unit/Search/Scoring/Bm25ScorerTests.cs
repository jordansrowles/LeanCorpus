using System.Numerics;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Scoring;

[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class Bm25ScorerTests
{
    [Fact(DisplayName = "Bm25Scorer: direct and precomputed scores agree")]
    public void Score_DirectAndPrecomputedPathsAgree()
    {
        const int docCount = 100;
        const int docFreq = 12;
        const int termFreq = 3;
        const int docLength = 18;
        const float averageDocLength = 12.5f;

        float direct = Bm25Scorer.Score(termFreq, docLength, averageDocLength, docCount, docFreq);
        var factors = Bm25Scorer.PrecomputeFactors(docCount, docFreq, averageDocLength);
        float precomputed = Bm25Scorer.ScorePrecomputed(
            factors.Idf,
            factors.K1BOverAvgDL,
            termFreq,
            docLength);

        Assert.Equal(Bm25Scorer.Idf(docCount, docFreq), factors.Idf);
        Assert.Equal(direct, precomputed, precision: 5);
        Assert.Equal(1.2f * 0.75f / averageDocLength, factors.K1BOverAvgDL, precision: 5);
    }

    [Fact(DisplayName = "Bm25Scorer: higher frequency scores higher and zero frequency is zero")]
    public void Score_HigherFrequencyScoresHigherAndZeroFrequencyIsZero()
    {
        float one = Bm25Scorer.Score(1, 10, 10, 20, 5);
        float three = Bm25Scorer.Score(3, 10, 10, 20, 5);
        float zero = Bm25Scorer.Score(0, 10, 10, 20, 5);

        Assert.True(one > 0);
        Assert.True(three > one);
        Assert.Equal(0, zero);
    }

    [Fact(DisplayName = "Bm25Scorer: shorter documents score higher at equal frequency")]
    public void Score_ShorterDocumentsScoreHigherAtEqualFrequency()
    {
        float shortDocument = Bm25Scorer.Score(2, 5, 10, 20, 5);
        float averageDocument = Bm25Scorer.Score(2, 10, 10, 20, 5);
        float longDocument = Bm25Scorer.Score(2, 20, 10, 20, 5);

        Assert.True(shortDocument > averageDocument);
        Assert.True(averageDocument > longDocument);
    }

    [Fact(DisplayName = "Bm25Scorer: batch scores match individual precomputed scores")]
    public void ScorePrecomputedBatch_MatchesIndividualScores()
    {
        int length = Vector<float>.Count * 2 + 3;
        var termFrequencies = Enumerable.Range(0, length).Select(static value => (value % 5) + 1).ToArray();
        var documentLengths = Enumerable.Range(0, length).Select(static value => 5 + value * 2).ToArray();
        var scores = Enumerable.Repeat(-1.0f, length + 2).ToArray();
        const float idf = 1.25f;
        const float k1BOverAverageDocLength = 0.09f;

        Bm25Scorer.ScorePrecomputedBatch(
            idf,
            k1BOverAverageDocLength,
            termFrequencies,
            documentLengths,
            scores);

        for (int i = 0; i < length; i++)
        {
            float expected = Bm25Scorer.ScorePrecomputed(
                idf,
                k1BOverAverageDocLength,
                termFrequencies[i],
                documentLengths[i]);
            Assert.Equal(expected, scores[i], precision: 5);
        }

        Assert.Equal(-1.0f, scores[^2]);
        Assert.Equal(-1.0f, scores[^1]);
    }

    [Fact(DisplayName = "Bm25Scorer: batch scoring handles a scalar tail")]
    public void ScorePrecomputedBatch_HandlesScalarTail()
    {
        int length = Math.Max(1, Vector<float>.Count - 1);
        var termFrequencies = Enumerable.Repeat(2, length).ToArray();
        var documentLengths = Enumerable.Repeat(10, length).ToArray();
        var scores = new float[length];

        Bm25Scorer.ScorePrecomputedBatch(0.8f, 0.1f, termFrequencies, documentLengths, scores);

        float expected = Bm25Scorer.ScorePrecomputed(0.8f, 0.1f, 2, 10);
        Assert.All(scores, score => Assert.Equal(expected, score, precision: 5));
    }

    [Fact(DisplayName = "Bm25Scorer: BM25F normalisation applies field weight")]
    public void NormaliseFieldTermFrequency_AppliesFieldWeight()
    {
        float unweighted = Bm25Scorer.NormaliseFieldTermFrequency(2.0f, 10, 10.0f);
        float weighted = Bm25Scorer.NormaliseFieldTermFrequency(2.0f, 10, 10.0f, 2.0f);

        Assert.Equal(2.0f, unweighted, precision: 5);
        Assert.Equal(4.0f, weighted, precision: 5);
    }

    [Fact(DisplayName = "Bm25Scorer: BM25F normalisation clamps invalid denominator")]
    public void NormaliseFieldTermFrequency_InvalidDenominatorReturnsZero()
    {
        float score = Bm25Scorer.NormaliseFieldTermFrequency(2.0f, -1, 1.0f);

        Assert.Equal(0.0f, score);
    }

    [Fact(DisplayName = "Bm25Scorer: combined score rejects non-positive pseudo frequency")]
    public void ScoreCombinedWithIdf_NonPositivePseudoFrequencyReturnsZero()
    {
        Assert.Equal(0.0f, Bm25Scorer.ScoreCombinedWithIdf(1.5f, 0.0f));
        Assert.Equal(0.0f, Bm25Scorer.ScoreCombinedWithIdf(1.5f, -1.0f));
    }

    [Fact(DisplayName = "Bm25Scorer: combined score uses BM25 saturation")]
    public void ScoreCombinedWithIdf_UsesBm25Saturation()
    {
        const float idf = 1.5f;
        const float pseudoTermFrequency = 3.0f;
        float expected = idf * (pseudoTermFrequency * 2.2f) / (pseudoTermFrequency + 1.2f);

        Assert.Equal(
            expected,
            Bm25Scorer.ScoreCombinedWithIdf(idf, pseudoTermFrequency),
            precision: 5);
    }
}
