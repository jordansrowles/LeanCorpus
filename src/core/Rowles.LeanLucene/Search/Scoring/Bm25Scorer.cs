namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// BM25 scorer. Computes relevance scores for term matches.
/// </summary>
public static class Bm25Scorer
{
    private const float K1 = 1.2f;
    private const float B = 0.75f;

    /// <summary>
    /// Computes the BM25 score for a single term in a single document.
    /// </summary>
    public static float Score(int termFreq, int docLength, float avgDocLength, int docCount, int docFreq)
    {
        float idf = MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));
        float tf = termFreq;
        float normalisedTf = (tf * (K1 + 1.0f)) / (tf + K1 * (1.0f - B + B * (docLength / avgDocLength)));
        return idf * normalisedTf;
    }

    /// <summary>Precomputes the IDF component (constant for a given term across all documents).</summary>
    public static float Idf(int docCount, int docFreq)
        => MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));

    /// <summary>
    /// Precomputes factors that are constant for a given field/term.
    /// Returns (idf, k1BOverAvgDL) where k1BOverAvgDL = K1 * B / avgDocLength.
    /// </summary>
    public static (float Idf, float K1BOverAvgDL) PrecomputeFactors(int docCount, int docFreq, float avgDocLength)
    {
        float idf = MathF.Log(1.0f + (docCount - docFreq + 0.5f) / (docFreq + 0.5f));
        float k1BOverAvgDL = K1 * B / avgDocLength;
        return (idf, k1BOverAvgDL);
    }

    /// <summary>
    /// Scores with precomputed IDF and normalisation factor. Avoids per-doc division.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static float ScorePrecomputed(float idf, float k1BOverAvgDL, int termFreq, int docLength)
    {
        float tf = termFreq;
        const float k1Plus1 = K1 + 1.0f;
        const float k1TimesOneMinusB = K1 * (1.0f - B);
        float normalisedTf = (tf * k1Plus1) / (tf + k1TimesOneMinusB + k1BOverAvgDL * docLength);
        return idf * normalisedTf;
    }
}
