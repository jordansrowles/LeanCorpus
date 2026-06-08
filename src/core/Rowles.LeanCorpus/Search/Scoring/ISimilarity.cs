namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Pluggable scoring model. Supports both classic similarities (BM25, TF-IDF) and
/// language-model similarities (Dirichlet, Jelinek-Mercer, Absolute Discounting).
/// </summary>
public interface ISimilarity
{
    /// <summary>Whether this similarity requires collection-level statistics
    /// (total term frequency and total terms in collection) for scoring.</summary>
    bool RequiresCollectionStatistics => false;

    /// <summary>Computes the score for a single term occurrence in a document.</summary>
    float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq);

    /// <summary>Precomputes factors constant for a given term across all documents.</summary>
    (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength);

    /// <summary>Scores using precomputed factors for hot-path scoring.</summary>
    float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength);

    /// <summary>
    /// Precomputes factors including collection-level term statistics for language-model similarities.
    /// Default simply delegates to <see cref="PrecomputeFactors"/> and sets CollectionProb to 0.
    /// </summary>
    (float Factor1, float Factor2, float CollectionProb) PrecomputeLmFactors(
        int totalDocCount, int docFreq, float avgDocLength,
        long collectionFrequency, long totalTermsInCollection)
    {
        var (f1, f2) = PrecomputeFactors(totalDocCount, docFreq, avgDocLength);
        return (f1, f2, 0f);
    }

    /// <summary>
    /// Scores using precomputed language-model factors.
    /// Default delegates to <see cref="ScorePrecomputed"/>, ignoring <paramref name="collectionProb"/>.
    /// </summary>
    float ScoreLmPrecomputed(float factor1, float factor2, float collectionProb,
        int termFreq, int docLength)
        => ScorePrecomputed(factor1, factor2, termFreq, docLength);
}
