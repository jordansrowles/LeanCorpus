namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>Classic TF-IDF scoring model.</summary>
public sealed class TfIdfSimilarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance of the TF-IDF similarity.</summary>
    public static readonly TfIdfSimilarity Instance = new();

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float tf = MathF.Sqrt(termFreq);
        float idf = 1.0f + MathF.Log((float)totalDocCount / (docFreq + 1));
        float lengthNorm = 1.0f / MathF.Sqrt(docLength);
        return tf * idf * lengthNorm;
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
    {
        float idf = 1.0f + MathF.Log((float)totalDocCount / (docFreq + 1));
        return (idf, 0f);
    }

    /// <inheritdoc/>
    public float ScorePrecomputed(float idf, float _, int termFreq, int docLength)
    {
        float tf = MathF.Sqrt(termFreq);
        float lengthNorm = 1.0f / MathF.Sqrt(docLength);
        return tf * idf * lengthNorm;
    }
}
