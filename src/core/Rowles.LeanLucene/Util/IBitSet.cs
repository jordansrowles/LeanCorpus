namespace Rowles.LeanLucene.Util;

/// <summary>
/// Narrow read-only abstraction over a set of document identifiers.
/// Used by HNSW search for filter masks (allow-list and post-filter mask).
/// </summary>
internal interface IBitSet
{
    /// <summary>Returns true when the document identifier is contained in the set.</summary>
    bool Contains(int docId);

    /// <summary>Number of identifiers currently contained in the set.</summary>
    int Cardinality { get; }
}
