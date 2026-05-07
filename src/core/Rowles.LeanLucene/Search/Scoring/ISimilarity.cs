namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Pluggable scoring model. Allows replacing BM25 with alternative similarity functions.
/// </summary>
public interface ISimilarity
{
    /// <summary>Computes the score for a single term occurrence in a document.</summary>
    float Score(int termFreq, int docLength, float avgDocLength, int totalDocCount, int docFreq);

    /// <summary>Precomputes factors constant for a given term across all documents.</summary>
    (float Factor1, float Factor2) PrecomputeFactors(int totalDocCount, int docFreq, float avgDocLength);

    /// <summary>Scores using precomputed factors for hot-path scoring.</summary>
    float ScorePrecomputed(float factor1, float factor2, int termFreq, int docLength);
}
