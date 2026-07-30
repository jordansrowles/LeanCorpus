using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Scoring;

public sealed class PreparedVectorScorerTests
{
    [Theory]
    [InlineData(VectorSimilarityFunction.Cosine)]
    [InlineData(VectorSimilarityFunction.DotProduct)]
    [InlineData(VectorSimilarityFunction.Euclidean)]
    [InlineData(VectorSimilarityFunction.MaximumInnerProduct)]
    [InlineData(VectorSimilarityFunction.Hamming)]
    public void Score_MatchesReference(VectorSimilarityFunction similarity)
    {
        float[] query = [1f, -2f, 3f, 0f, 5f, -1f, 2f, 4f, -3f];
        float[] candidate = [2f, -1f, 1f, 0f, 8f, -4f, 2f, 3f, -2f];
        var scorer = new PreparedVectorScorer(query, similarity);

        float expected = VectorQuery.ComputeSimilarity(query, candidate, similarity);

        Assert.Equal(expected, scorer.Score(candidate), precision: 5);
    }

    [Fact]
    public void Score_DoesNotObserveCallerMutation()
    {
        float[] query = [1f, 0f];
        var scorer = new PreparedVectorScorer(query, VectorSimilarityFunction.DotProduct);
        query[0] = 99f;

        Assert.Equal(1f, scorer.Score([1f, 0f]));
    }

    [Fact]
    public void Score_ReturnsZeroForDimensionMismatch()
    {
        var scorer = new PreparedVectorScorer([1f, 0f]);

        Assert.Equal(0f, scorer.Score([1f]));
    }
}
