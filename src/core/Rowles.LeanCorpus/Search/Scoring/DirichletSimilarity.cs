using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Language-model similarity with Dirichlet (Bayesian) smoothing.
/// Smooths the document model towards the collection model using a prior parameter μ.
///
/// <para>Score = log((tf + μ · P(t|C)) / (|d| + μ)), where P(t|C) = collectionFrequency / totalTermsInCollection.</para>
/// <para>Default μ = 2000 (optimised for short to medium-length documents).</para>
/// </summary>
public sealed class DirichletSimilarity : ISimilarity
{
    /// <summary>Gets a shared singleton instance with the default μ = 2000.</summary>
    public static readonly DirichletSimilarity Instance = new();

    private readonly float _mu;

    /// <summary>Initialises a new instance with the specified Dirichlet prior μ.</summary>
    /// <param name="mu">Smoothing parameter. Typical range: 100–3000. Default 2000.</param>
    public DirichletSimilarity(float mu = 2000f)
    {
        if (mu <= 0) throw new ArgumentOutOfRangeException(nameof(mu), mu, "mu must be positive.");
        _mu = mu;
    }

    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => true;

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        // Collection stats unavailable in the simple Score path; use a degenerate fallback.
        float tfNorm = termFreq / (float)docLength;
        return MathF.Log(1f + tfNorm);
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
    {
        // Deprecated for LM; call PrecomputeLmFactors instead.
        return (0f, _mu);
    }

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
    {
        // Deprecated for LM; call ScoreLmPrecomputed instead.
        return ScoreLmPrecomputed(0f, factor2, 0f, termFreq, docLength);
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2, float CollectionProb) PrecomputeLmFactors(
        int totalDocCount, int docFreq, float avgDocLength,
        long collectionFrequency, long totalTermsInCollection)
    {
        float collectionProb = totalTermsInCollection > 0
            ? (float)collectionFrequency / totalTermsInCollection
            : 0f;
        return (0f, _mu, collectionProb);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ScoreLmPrecomputed(float factor1, float mu, float collectionProb, int termFreq, int docLength)
    {
        // Dirichlet: P(t|d) = (tf + μ · P(t|C)) / (|d| + μ)
        float numerator = termFreq + mu * collectionProb;
        float denominator = docLength + mu;
        return MathF.Log(numerator / denominator);
    }
}
