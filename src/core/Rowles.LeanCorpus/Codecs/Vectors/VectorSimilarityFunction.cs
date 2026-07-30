namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Similarity function used to index and search a vector field.</summary>
public enum VectorSimilarityFunction : byte
{
    /// <summary>Cosine similarity in the range -1 to 1.</summary>
    Cosine = 0,

    /// <summary>Raw dot-product similarity.</summary>
    DotProduct = 1,

    /// <summary>Euclidean similarity represented as <c>1 / (1 + squared distance)</c>.</summary>
    Euclidean = 2,

    /// <summary>Maximum inner product transformed to a positive similarity.</summary>
    MaximumInnerProduct = 3,

    /// <summary>Hamming similarity for compatible byte or binary encodings.</summary>
    Hamming = 4,
}
