using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document.Fields;

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

    /// <summary>
    /// Whether a historical HNSW graph file (<c>.hnsw</c> v1) is present for this field. 4.0 writers
    /// never set this; Float32 and Int8 queries ignore such graphs and search exactly.
    /// </summary>
    public bool HasHnsw { get; init; }

    /// <summary>
    /// Whether a Float32 vector graph file (<c>.vgr</c>) is present for this field. Set only when the
    /// writer enables <see cref="Index.Indexer.IndexWriterConfig.BuildHnswOnFlush"/> for an unquantised field.
    /// Default: <c>false</c>.
    /// </summary>
    public bool HasGraph { get; init; }

    /// <summary>Quantisation strategy applied to this vector field. Default: <see cref="Codecs.Vectors.VectorQuantisation.None"/>.</summary>
    public VectorQuantisation Quantisation { get; init; } = VectorQuantisation.None;

    /// <summary>
    /// Validates invariants after deserialisation. Throws <see cref="InvalidDataException"/>
    /// when required fields are missing, empty, or out of range.
    /// </summary>
    internal void Validate()
    {
        try
        {
            FieldNameValidator.Validate(FieldName, nameof(FieldName));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Vector field metadata has an invalid FieldName.", ex);
        }

        if (Dimension <= 0)
            throw new InvalidDataException($"Vector field '{FieldName}' has a non-positive Dimension ({Dimension}).");
        if (!Enum.IsDefined(Quantisation))
            throw new InvalidDataException($"Vector field '{FieldName}' has an undefined quantisation value '{Quantisation}'.");
        if (HasGraph && Quantisation != VectorQuantisation.None)
            throw new InvalidDataException($"Vector field '{FieldName}' declares a vector graph for quantisation '{Quantisation}'; graphs are Float32 only.");
    }
}

/// <summary>Identifies the persisted similarity function used by an HNSW graph.</summary>
internal enum VectorScoringGeneration : byte
{
    Float32 = 0,
    Int8Scalar = 1,
    LegacyBbqSign = 2,
    BbqOsqV3 = 3,
}
