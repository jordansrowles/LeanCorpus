using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>Per-field vector storage, scoring, and graph configuration.</summary>
public sealed class VectorFieldConfig
{
    /// <summary>Similarity used for indexing and search. Default: cosine.</summary>
    public VectorSimilarityFunction Similarity { get; init; } = VectorSimilarityFunction.Cosine;

    /// <summary>Whether Float32 vectors are normalised during indexing.</summary>
    public bool Normalise { get; init; } = true;

    /// <summary>Storage quantisation. Default: none.</summary>
    public VectorQuantisation Quantisation { get; init; } = VectorQuantisation.None;

    /// <summary>Whether to build an HNSW graph when the segment is flushed.</summary>
    public bool BuildHnsw { get; init; } = true;

    /// <summary>HNSW build parameters for this field.</summary>
    public HnswBuildConfig HnswBuildConfig { get; init; } = new();

    /// <summary>
    /// Whether a Float32 sidecar is retained when the primary vector encoding is quantised.
    /// The sidecar supplies exact reranking scores.
    /// </summary>
    public bool RetainFullPrecision { get; init; }

    internal void Validate(string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(HnswBuildConfig);
        if (!Enum.IsDefined(Quantisation))
            throw new ArgumentOutOfRangeException(
                nameof(Quantisation), Quantisation, "Unknown vector quantisation strategy.");
        if (Quantisation is VectorQuantisation.ProductQuantisation or VectorQuantisation.RaBitQ)
            throw new NotSupportedException(
                $"Vector quantisation '{Quantisation}' was rejected by ADR016 and cannot be selected for new indexes.");

        if (Similarity == VectorSimilarityFunction.Hamming && Quantisation != VectorQuantisation.BBQ)
            throw new ArgumentException(
                $"Hamming similarity for field '{fieldName}' requires a binary-compatible encoding.",
                nameof(Similarity));
    }
}
