using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Language-model similarity with absolute discounting smoothing.
/// Subtracts a constant δ from each observed term count and redistributes
/// the probability mass to unseen terms via the collection model.
///
/// <para>Score = log((max(tf - δ, 0) + δ · |d| · P(t|C)) / |d|), where P(t|C) = collectionFrequency / totalTermsInCollection.</para>
/// <para>Default δ = 0.7.</para>
/// </summary>
public sealed class LMAbsoluteDiscountingSimilarity : ISimilarity
{
    /// <summary>Gets a shared singleton instance with the default δ = 0.7.</summary>
    public static readonly LMAbsoluteDiscountingSimilarity Instance = new();

    private readonly float _delta;

    /// <summary>Initialises a new instance with the specified discount parameter δ.</summary>
    /// <param name="delta">Absolute discount constant. Typical range: 0.5–0.9. Default 0.7.</param>
    public LMAbsoluteDiscountingSimilarity(float delta = 0.7f)
    {
        if (delta < 0 || delta >= 1) throw new ArgumentOutOfRangeException(nameof(delta), delta, "delta must be in [0, 1).");
        _delta = delta;
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
        => (_delta, 0f);

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
        return (_delta, 0f, collectionProb);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ScoreLmPrecomputed(float delta, float _, float collectionProb, int termFreq, int docLength)
    {
        // Absolute discounting: P(t|d) = (max(tf - δ, 0) + δ · |d| · P(t|C)) / |d|
        float discounted = MathF.Max(termFreq - delta, 0f);
        float numerator = discounted + delta * docLength * collectionProb;
        return MathF.Log(numerator / docLength);
    }
}
