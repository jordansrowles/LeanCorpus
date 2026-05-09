namespace Rowles.LeanLucene.Codecs.Vectors;

/// <summary>
/// Provides random access to vectors during HNSW graph operations.
/// Implementations include an in-memory list adapter (used at build time)
/// and a memory-mapped <see cref="VectorReader"/> adapter (used at search and merge time).
/// </summary>
internal interface IVectorSource
{
    /// <summary>Vector dimension; every vector returned has exactly this length.</summary>
    int Dimension { get; }

    /// <summary>Total number of vectors addressable by this source.</summary>
    int Count { get; }

    /// <summary>Returns the vector for a document identifier as a read-only span.</summary>
    ReadOnlySpan<float> GetVector(int docId);
}
