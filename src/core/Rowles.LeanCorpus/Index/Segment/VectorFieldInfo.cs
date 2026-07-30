using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Persisted metadata for a single vector field within a segment. The reader uses this to
/// open the corresponding per-field <c>.vec</c> and <c>.hnsw</c> files lazily.
/// </summary>
public sealed class VectorFieldInfo
{
    /// <summary>Logical name of the vector field as supplied by the application.</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Dimension of every vector in this field.</summary>
    public int Dimension { get; init; }

    /// <summary>Whether vectors were L2-normalised at write time. When true, dot product equals cosine similarity.</summary>
    public bool Normalised { get; init; }

    /// <summary>Whether a built HNSW graph file is present for this field.</summary>
    public bool HasHnsw { get; init; }

    /// <summary>Quantisation strategy applied to this vector field. Default: <see cref="Codecs.Vectors.VectorQuantisation.None"/>.</summary>
    public VectorQuantisation Quantisation { get; init; } = VectorQuantisation.None;

    /// <summary>Similarity function used by this field.</summary>
    public VectorSimilarityFunction Similarity { get; init; } = VectorSimilarityFunction.Cosine;

    /// <summary>Whether a Float32 sidecar is retained for exact reranking.</summary>
    public bool RetainsFullPrecision { get; init; }

    /// <summary>HNSW upper-layer degree used to build this field.</summary>
    public int HnswM { get; init; } = 16;

    /// <summary>HNSW layer-zero degree used to build this field. Zero means twice <see cref="HnswM"/>.</summary>
    public int HnswM0 { get; init; }

    /// <summary>HNSW construction candidate count used to build this field.</summary>
    public int HnswEfConstruction { get; init; } = 100;

    /// <summary>
    /// Validates invariants after deserialisation. Throws <see cref="InvalidDataException"/>
    /// when required fields are missing, empty, or out of range.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(FieldName))
            throw new InvalidDataException("Vector field metadata has a null or empty FieldName.");
        if (Dimension <= 0)
            throw new InvalidDataException($"Vector field '{FieldName}' has a non-positive Dimension ({Dimension}).");
        if (HnswM is < 2 or > 100 ||
            HnswM0 != 0 && (HnswM0 is < 2 or > 200) ||
            HnswEfConstruction is < 1 or > 2000)
            throw new InvalidDataException($"Vector field '{FieldName}' has invalid HNSW build metadata.");
    }
}
