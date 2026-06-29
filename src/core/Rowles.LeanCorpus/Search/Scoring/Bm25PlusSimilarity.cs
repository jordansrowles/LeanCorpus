namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// BM25+ scoring model. Extends BM25 with a lower-bound delta constant to fix
/// BM25's tendency to produce near-zero scores for very short documents.
///
/// <para>Score = idf * (normalised_tf + delta).</para>
/// <para>Default: k1=1.2, b=0.75, delta=1.0.</para>
/// </summary>
public sealed class Bm25PlusSimilarity : ISimilarity
{
    /// <summary>Gets the shared singleton instance with default parameters (k1=1.2, b=0.75, delta=1.0).</summary>
    public static readonly Bm25PlusSimilarity Instance = new();

    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => false;

    private readonly float _k1;
    private readonly float _b;
    private readonly float _delta;
    private readonly float _k1Plus1;
    private readonly float _k1TimesOneMinusB;

    /// <summary>Initialises a new instance with the specified BM25+ parameters.</summary>
    /// <param name="k1">Term frequency saturation parameter. Default 1.2.</param>
    /// <param name="b">Document length normalisation parameter. Default 0.75.</param>
    /// <param name="delta">Lower-bound score delta. Default 1.0.</param>
    public Bm25PlusSimilarity(float k1 = 1.2f, float b = 0.75f, float delta = 1.0f)
    {
        if (k1 < 0) throw new ArgumentOutOfRangeException(nameof(k1), k1, "k1 must not be negative.");
        if (b < 0 || b > 1) throw new ArgumentOutOfRangeException(nameof(b), b, "b must be in [0, 1].");
        if (delta < 0) throw new ArgumentOutOfRangeException(nameof(delta), delta, "delta must not be negative.");
        _k1 = k1;
        _b = b;
        _delta = delta;
        _k1Plus1 = k1 + 1.0f;
        _k1TimesOneMinusB = k1 * (1.0f - b);
    }

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float idf = ComputeIdf(totalDocCount, docFreq);
        float normalisedTf = ComputeNormalisedTf(termFreq, docLength, avgDocLength);
        return idf * (normalisedTf + _delta);
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
    {
        float idf = ComputeIdf(totalDocCount, docFreq);
        float k1BOverAvgDL = _k1 * _b / avgDocLength;
        return (idf, k1BOverAvgDL);
    }

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
    {
        float idf = factor1;
        float k1BOverAvgDL = factor2;
        float tf = termFreq;
        float normalisedTf = (tf * _k1Plus1) / (tf + _k1TimesOneMinusB + k1BOverAvgDL * docLength);
        return idf * (normalisedTf + _delta);
    }

    private static float ComputeIdf(int totalDocCount, int docFreq)
        => MathF.Log(1.0f + (totalDocCount - docFreq + 0.5f) / (docFreq + 0.5f));

    private float ComputeNormalisedTf(int termFreq, int docLength, float avgDocLength)
    {
        float tf = termFreq;
        return (tf * _k1Plus1) / (tf + _k1 * (1.0f - _b + _b * (docLength / avgDocLength)));
    }
}
