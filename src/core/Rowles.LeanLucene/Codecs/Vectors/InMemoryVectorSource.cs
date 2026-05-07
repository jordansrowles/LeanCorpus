namespace Rowles.LeanLucene.Codecs.Vectors;

/// <summary>
/// In-memory <see cref="IVectorSource"/> backed by an indexed dictionary of vectors.
/// Used during HNSW build before the .vec file has been written.
/// </summary>
internal sealed class InMemoryVectorSource : IVectorSource
{
    private readonly Dictionary<int, ReadOnlyMemory<float>> _vectors;

    public InMemoryVectorSource(Dictionary<int, ReadOnlyMemory<float>> vectors, int dimension)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        _vectors = vectors;
        Dimension = dimension;
    }

    public int Dimension { get; }

    public int Count => _vectors.Count;

    public ReadOnlySpan<float> GetVector(int docId)
    {
        if (!_vectors.TryGetValue(docId, out var vec))
            throw new KeyNotFoundException($"No vector buffered for docId {docId}.");
        return vec.Span;
    }
}
