namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>Reason the HNSW search completed an underfilled result with an exhaustive scan.</summary>
internal enum HnswExhaustiveFallbackReason : byte
{
    None = 0,
    UnderfilledAfterTraversal = 1,
}

/// <summary>Per-call graph visits, retries and exhaustive fallback statistics.</summary>
internal readonly record struct HnswSearchStats(
    int NodesVisited,
    int LayersDescended,
    int ScannedVectors,
    bool UsedExhaustiveFallback,
    HnswExhaustiveFallbackReason FallbackReason);
