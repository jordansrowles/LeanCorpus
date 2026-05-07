namespace Rowles.LeanLucene.Codecs.Vectors;

/// <summary>
/// Adapts a <see cref="VectorReader"/> to the <see cref="IVectorSource"/> contract so an
/// HNSW graph loaded from disc can resolve vector data on demand.
/// </summary>
internal sealed class VectorReaderSource : IVectorSource
{
    private readonly VectorReader _reader;
    private readonly int _dimension;

    public VectorReaderSource(VectorReader reader)
    {
        _reader = reader;
        _dimension = reader.Dimension;
    }

    public int Dimension => _dimension;

    public int Count => _reader.VectorCount;

    public ReadOnlySpan<float> GetVector(int docId) => _reader.ReadVector(docId);
}
