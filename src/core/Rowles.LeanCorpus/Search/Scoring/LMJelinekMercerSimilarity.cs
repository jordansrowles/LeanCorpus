using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Language-model similarity with Jelinek-Mercer (linear interpolation) smoothing.
/// Interpolates the document model with the collection model using a coefficient λ.
///
/// <para>Score = log((1-λ) · (tf/|d|) + λ · P(t|C)), where P(t|C) = collectionFrequency / totalTermsInCollection.</para>
/// <para>Default λ = 0.1.</para>
/// </summary>
public sealed class LMJelinekMercerSimilarity : ISimilarity
{
    /// <summary>Gets a shared singleton instance with the default λ = 0.1.</summary>
    public static readonly LMJelinekMercerSimilarity Instance = new();

    private readonly float _lambda;

    /// <summary>Initialises a new instance with the specified interpolation coefficient λ.</summary>
    /// <param name="lambda">Interpolation weight for the collection model. Range (0, 1). Default 0.1.</param>
    public LMJelinekMercerSimilarity(float lambda = 0.1f)
    {
        if (lambda < 0 || lambda > 1) throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "lambda must be in [0, 1].");
        _lambda = lambda;
    }

    /// <inheritdoc/>
    public bool RequiresCollectionStatistics => true;

    /// <inheritdoc/>
    public float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq)
    {
        float tfNorm = termFreq / (float)docLength;
        return MathF.Log(1f + tfNorm);
    }

    /// <inheritdoc/>
    public (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength)
        => (_lambda, 0f);

    /// <inheritdoc/>
    public float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength)
        => ScoreLmPrecomputed(factor1, factor2, 0f, termFreq, docLength);

    /// <inheritdoc/>
    public (float Factor1, float Factor2, float CollectionProb) PrecomputeLmFactors(
        int totalDocCount, int docFreq, float avgDocLength,
        long collectionFrequency, long totalTermsInCollection)
    {
        float collectionProb = totalTermsInCollection > 0
            ? (float)collectionFrequency / totalTermsInCollection
            : 0f;
        return (_lambda, 0f, collectionProb);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ScoreLmPrecomputed(float lambda, float _, float collectionProb, int termFreq, int docLength)
    {
        // Jelinek-Mercer: P(t|d) = (1-λ)·(tf/|d|) + λ·P(t|C)
        float docModel = termFreq / (float)docLength;
        float interpolated = (1f - lambda) * docModel + lambda * collectionProb;
        return MathF.Log(interpolated);
    }
}
