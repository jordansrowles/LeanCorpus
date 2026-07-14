namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Extended vector source for BBQ (Better Binary Quantisation) that exposes
/// raw bit-packed data and the per-segment centroid, enabling PopCount-based
/// distance computation without dequantising to float32.
/// </summary>
/// <remarks>
/// Implemented by both <see cref="QuantisedVectorSource"/> (search-time, disk-backed)
/// and <see cref="BBQMemoryVectorSource"/> (build-time, in-memory). This avoids
/// coupling <see cref="Hnsw.HnswGraph"/> to concrete source types.
/// </remarks>
internal interface IBBQVectorSource : IVectorSource
{
    /// <summary>Raw bit-packed vector data for the given document. Length = ceil(Dimension / 8).</summary>
    ReadOnlySpan<byte> GetRawVector(int docId);

    /// <summary>Per-segment centroid used during binary quantisation. Length = Dimension.</summary>
    ReadOnlySpan<float> Centroid { get; }
}
