namespace Rowles.LeanLucene.Codecs.Hnsw;

/// <summary>
/// Result entry returned from HNSW search.
/// </summary>
internal readonly record struct HnswSearchResult(int DocId, float Score);
