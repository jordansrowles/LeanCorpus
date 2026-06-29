namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Pivoted document length normalisation TF-IDF scoring model (Singhal et al.).
/// Uses a linear interpolation between constant and dl/avgdl instead of 1/sqrt(dl),
/// which penalises long documents less aggressively.
///
/// <para>tf = sqrt(tf).</para>
/// <para>len_norm = 1 / ((1-s) + s * dl/avgdl).</para>
/// <para>Score = tf * idf * len_norm.</para>
/// <para>Default: s=0.2.</para>
/// </summary>
public sealed class TfIdfPivotedSimilarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance with the default slope (s=0.2).</summary>
    public static readonly TfIdfPivotedSimilarity Instance = new();
    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => false;

    private readonly float _s;
    private readonly float _oneMinusS;

    /// <summary>Initialises a new instance with the specified pivoted normalisation slope.</summary>
    /// <param name="s">
    /// Slope controlling document length normalisation. When s=0, no length normalisation;
    /// when s=1, normalisation is fully proportional to dl/avgdl. Typical range: 0.1–0.3. Default 0.2.
    /// </param>
    public TfIdfPivotedSimilarity(float s = 0.2f)
    {
        if (s < 0 || s > 1) throw new ArgumentOutOfRangeException(nameof(s), s, "s must be in [0, 1].");
        _s = s;
        _oneMinusS = 1.0f - s;
    }

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float tf = MathF.Sqrt(termFreq);
        float idf = ComputeIdf(totalDocCount, docFreq);
        float lengthNorm = 1.0f / (_oneMinusS + _s * docLength / avgDocLength);
        return tf * idf * lengthNorm;
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
    {
        float idf = ComputeIdf(totalDocCount, docFreq);
        float sDivAvgDL = _s / avgDocLength;
        return (idf, sDivAvgDL);
    }

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
    {
        float idf = factor1;
        float sDivAvgDL = factor2;
        float tf = MathF.Sqrt(termFreq);
        float lengthNorm = 1.0f / (_oneMinusS + sDivAvgDL * docLength);
        return tf * idf * lengthNorm;
    }

    private static float ComputeIdf(int totalDocCount, int docFreq)
        => 1.0f + MathF.Log((float)totalDocCount / (docFreq + 1));
}
