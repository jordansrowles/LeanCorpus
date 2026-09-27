namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>Reports the current entry count and lifetime evictions for a bounded cache.</summary>
/// <param name="EntryCount">The approximate entries in the current cache generation.</param>
/// <param name="EvictionCount">The number of cache generations evicted since creation.</param>
public readonly record struct CacheMetricsSnapshot(int EntryCount, long EvictionCount);
