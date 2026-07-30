namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Quantisation strategy applied to vector data within a segment.
/// Stored in <see cref="Rowles.LeanCorpus.Index.Segment.VectorFieldInfo"/> so the reader
/// knows which codec path to use when opening per-segment vector files.
/// </summary>
public enum VectorQuantisation : byte
{
    /// <summary>No quantisation; vectors are stored as raw float32 in a <c>.vec</c> file.</summary>
    None = 0,

    /// <summary>8-bit scalar quantisation with per-segment min/alpha scaling.</summary>
    Int8 = 1,

    /// <summary>Better Binary Quantisation with per-segment centroid and asymmetric query-side int4.</summary>
    BBQ = 2,

    /// <summary>4-bit scalar quantisation with two values packed into each byte.</summary>
    Int4 = 3,

    /// <summary>
    /// Rejected product-quantisation identifier retained for reading, migration,
    /// internal research fixtures, and format compatibility. New indexes reject
    /// this value under ADR016.
    /// </summary>
    ProductQuantisation = 4,

    /// <summary>
    /// Rejected RaBitQ identifier retained solely for reading and migrating legacy
    /// experimental indexes. New indexes reject this value under ADR016.
    /// </summary>
    RaBitQ = 5,
}

/// <summary>
/// Per-vector error-correction values stored alongside quantised vectors.
/// Applied after the initial dequantisation to recover some of the lost precision.
/// </summary>
/// <param name="Correction">The correction value applied as a final adjustment to the dot product
/// score between the query and this stored vector.</param>
internal readonly record struct QuantisedVectorError(float Correction);
