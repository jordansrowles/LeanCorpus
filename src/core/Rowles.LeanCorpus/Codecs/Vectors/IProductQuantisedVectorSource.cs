namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Provides code-native product-quantised distance operations for HNSW.</summary>
internal interface IProductQuantisedVectorSource
{
    /// <summary>Prepares one asymmetric distance lookup table for a query.</summary>
    ProductQuantisationQuery PrepareQuery(
        ReadOnlySpan<float> query,
        VectorSimilarityFunction similarity,
        bool normalised);

    /// <summary>Computes a code-native distance between two stored vectors.</summary>
    float StoredDistance(
        int leftDocId,
        int rightDocId,
        VectorSimilarityFunction similarity,
        bool normalised);
}
