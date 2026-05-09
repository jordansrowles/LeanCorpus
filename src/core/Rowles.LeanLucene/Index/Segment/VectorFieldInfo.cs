namespace Rowles.LeanLucene.Index.Segment;

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
}
