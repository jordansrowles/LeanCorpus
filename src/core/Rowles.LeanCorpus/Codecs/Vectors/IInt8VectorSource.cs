namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Extended vector source for Int8 scalar-quantised vectors that exposes
/// raw byte data and dequantisation parameters, enabling fused dot-product
/// computation without allocating intermediate float arrays.
/// </summary>
/// <remarks>
/// Implemented by both <see cref="QuantisedVectorSource"/> (search-time, disk-backed)
/// and <see cref="Int8QuantisedMemoryVectorSource"/> (build-time, in-memory). This avoids
/// coupling <see cref="Hnsw.HnswGraph"/> to concrete source types.
/// </remarks>
internal interface IInt8VectorSource : IVectorSource
{
    /// <summary>Raw int8-quantised vector bytes for the given document. Length = Dimension.</summary>
    ReadOnlySpan<byte> GetRawVector(int docId);

    /// <summary>Per-segment minimum value used during int8 quantisation.</summary>
    float Min { get; }

    /// <summary>Per-segment scale factor <c>(max - min) / 255</c> used during int8 quantisation.</summary>
    float Alpha { get; }
}
