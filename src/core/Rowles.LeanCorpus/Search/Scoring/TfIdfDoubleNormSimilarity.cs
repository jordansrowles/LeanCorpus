namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Double-normalised TF-IDF scoring model. Combines augmented/pivoted term frequency
/// (reducing the impact of high-TF terms) with pivoted document length normalisation
/// (less aggressive penalty for long documents).
///
/// <para>tf_norm = K + (1-K) * tf / (tf + K).</para>
/// <para>len_norm = 1 / ((1-s) + s * dl/avgdl).</para>
/// <para>Score = tf_norm * idf * len_norm.</para>
/// <para>Default: K=0.5, s=0.2.</para>
/// </summary>
public sealed class TfIdfDoubleNormSimilarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance with default parameters (K=0.5, s=0.2).</summary>
    public static readonly TfIdfDoubleNormSimilarity Instance = new();
    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => false;

    private readonly float _k;
    private readonly float _s;
    private readonly float _oneMinusK;
    private readonly float _oneMinusS;

    /// <summary>Initialises a new instance with the specified pivot and slope parameters.</summary>
    /// <param name="k">
    /// Pivot point for term frequency augmentation. At tf=K, tf_norm ≈ 0.75.
    /// Typical range: 0.2–1.0. Default 0.5.
    /// </param>
    /// <param name="s">
    /// Slope controlling document length normalisation. Typical range: 0.1–0.3. Default 0.2.
    /// </param>
    public TfIdfDoubleNormSimilarity(float k = 0.5f, float s = 0.2f)
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), k, "k must be positive.");
        if (s < 0 || s > 1) throw new ArgumentOutOfRangeException(nameof(s), s, "s must be in [0, 1].");
        _k = k;
        _s = s;
        _oneMinusK = 1.0f - k;
        _oneMinusS = 1.0f - s;
    }

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float tfNorm = ComputeTfNorm(termFreq);
        float idf = ComputeIdf(totalDocCount, docFreq);
        float lengthNorm = 1.0f / (_oneMinusS + _s * docLength / avgDocLength);
        return tfNorm * idf * lengthNorm;
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
        float tfNorm = _k + _oneMinusK * termFreq / (termFreq + _k);
        float lengthNorm = 1.0f / (_oneMinusS + sDivAvgDL * docLength);
        return tfNorm * idf * lengthNorm;
    }

    private float ComputeTfNorm(int termFreq)
        => _k + _oneMinusK * termFreq / (termFreq + _k);

    private static float ComputeIdf(int totalDocCount, int docFreq)
        => 1.0f + MathF.Log((float)totalDocCount / (docFreq + 1));
}
