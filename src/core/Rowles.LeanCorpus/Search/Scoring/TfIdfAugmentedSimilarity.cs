namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Augmented TF-IDF scoring model. Uses a pivoted term frequency that reduces the
/// impact of high-TF terms within a document. The parameter K controls the pivot point.
///
/// <para>tf_aug = K + (1-K) * tf / (tf + K).</para>
/// <para>len_norm = 1 / sqrt(dl).</para>
/// <para>Score = tf_aug * idf * len_norm.</para>
/// <para>Default: K=0.5.</para>
/// </summary>
public sealed class TfIdfAugmentedSimilarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance with the default pivot (K=0.5).</summary>
    public static readonly TfIdfAugmentedSimilarity Instance = new();

    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => false;

    private readonly float _k;
    private readonly float _oneMinusK;

    /// <summary>Initialises a new instance with the specified pivot parameter K.</summary>
    /// <param name="k">
    /// Pivot point for term frequency augmentation. At tf=K, tf_aug = 0.5 + 0.5*K/(2K) ≈ 0.75.
    /// Typical range: 0.2–1.0. Default 0.5.
    /// </param>
    public TfIdfAugmentedSimilarity(float k = 0.5f)
    {
        if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), k, "k must be positive.");
        _k = k;
        _oneMinusK = 1.0f - k;
    }

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float tfAug = ComputeTfAug(termFreq);
        float idf = ComputeIdf(totalDocCount, docFreq);
        float lengthNorm = 1.0f / MathF.Sqrt(docLength);
        return tfAug * idf * lengthNorm;
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
    {
        float idf = ComputeIdf(totalDocCount, docFreq);
        return (idf, _k);
    }

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
    {
        float idf = factor1;
        float k = factor2;
        float tfAug = k + (1.0f - k) * termFreq / (termFreq + k);
        float lengthNorm = 1.0f / MathF.Sqrt(docLength);
        return tfAug * idf * lengthNorm;
    }

    private float ComputeTfAug(int termFreq)
        => _k + _oneMinusK * termFreq / (termFreq + _k);

    private static float ComputeIdf(int totalDocCount, int docFreq)
        => 1.0f + MathF.Log((float)totalDocCount / (docFreq + 1));
}
