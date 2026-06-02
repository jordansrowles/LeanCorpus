namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Adapts a <see cref="QuantisedVectorReader"/> to the <see cref="IVectorSource"/> contract
/// so an HNSW graph can resolve vector data on demand. Dequantisation produces a freshly
/// allocated float array per call, matching the allocation behaviour of <see cref="VectorReader"/>.
/// </summary>
internal sealed class QuantisedVectorSource : IBBQVectorSource, IInt8VectorSource
{
    private readonly QuantisedVectorReader _reader;
    private readonly int _dimension;
    private readonly VectorQuantisation _quantisation;

    public QuantisedVectorSource(QuantisedVectorReader reader)
    {
        _reader = reader;
        _dimension = reader.Dimension;
        _quantisation = reader.Quantisation;
    }

    public int Dimension => _dimension;
    public int Count => _reader.DocCount;
    public VectorQuantisation Quantisation => _quantisation;

    /// <summary>
    /// Returns a dequantised float vector. For int8, this is min + alpha * qv[i].
    /// For BBQ, this is centroid[i] ± 1. The caller owns the returned array.
    /// </summary>
    public ReadOnlySpan<float> GetVector(int docId) => _reader.ReadVector(docId);

    /// <summary>Exposes the underlying reader for distance-computer access.</summary>
    internal QuantisedVectorReader Reader => _reader;

    /// <inheritdoc cref="IBBQVectorSource.GetRawVector"/>
    ReadOnlySpan<byte> IBBQVectorSource.GetRawVector(int docId) => _reader.GetRawBBQVector(docId);

    /// <inheritdoc cref="IInt8VectorSource.GetRawVector"/>
    ReadOnlySpan<byte> IInt8VectorSource.GetRawVector(int docId) => _reader.GetRawInt8Vector(docId);

    /// <inheritdoc cref="IInt8VectorSource.Min"/>
    float IInt8VectorSource.Min => _reader.Min;

    /// <inheritdoc cref="IInt8VectorSource.Alpha"/>
    float IInt8VectorSource.Alpha => _reader.Alpha;

    /// <inheritdoc cref="IBBQVectorSource.Centroid"/>
    ReadOnlySpan<float> IBBQVectorSource.Centroid => _reader.Centroid;
}
