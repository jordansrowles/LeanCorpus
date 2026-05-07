namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>BM25 scoring (default). Delegates to <see cref="Bm25Scorer"/>.</summary>
public sealed class Bm25Similarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance of the BM25 similarity.</summary>
    public static readonly Bm25Similarity Instance = new();

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
        => Bm25Scorer.Score(termFreq, docLength, avgDocLength, totalDocCount, docFreq);

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
        => Bm25Scorer.PrecomputeFactors(totalDocCount, docFreq, avgDocLength);

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
        => Bm25Scorer.ScorePrecomputed(factor1, factor2, termFreq, docLength);
}
