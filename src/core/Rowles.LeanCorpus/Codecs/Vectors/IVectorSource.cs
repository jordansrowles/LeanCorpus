namespace Rowles.LeanCorpus.Codecs.Vectors;

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

    /// <summary>Returns a read-only vector span, which may borrow storage owned by this source.</summary>
    /// <remarks>The span must not be retained after this source or its backing reader is disposed.</remarks>
    ReadOnlySpan<float> GetVector(int docId);

    /// <summary>Copies a vector into caller-owned scratch storage without requiring a new array.</summary>
    void CopyVectorTo(int docId, Span<float> destination)
    {
        if (destination.Length != Dimension)
            throw new ArgumentException($"Destination length {destination.Length} != vector dimension {Dimension}.", nameof(destination));
        GetVector(docId).CopyTo(destination);
    }
}
