using System.Collections.Concurrent;
using System.Threading;
using Rowles.LeanCorpus.Diagnostics;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Thread-safe cache for interned qualified term strings ("field\0term").
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}.AlternateLookup{TAlternateKey}"/>
/// for zero-allocation lookups with <see cref="ReadOnlySpan{T}"/> keys.
/// </summary>
internal sealed class QualifiedTermCache
{
    internal const int DefaultMaximumEntries = 4096;
    internal const int MaximumCachedQualifiedTermLength = 512;

    private readonly int _maximumEntries;
    private readonly int _maximumQualifiedTermLength;
    private Generation _generation = new();
    private long _evictionCount;

    public QualifiedTermCache(
        int maximumEntries = DefaultMaximumEntries,
        int maximumQualifiedTermLength = MaximumCachedQualifiedTermLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumQualifiedTermLength, 1);
        _maximumEntries = maximumEntries;
        _maximumQualifiedTermLength = maximumQualifiedTermLength;
    }

    internal CacheMetricsSnapshot Metrics
    {
        get
        {
            var generation = Volatile.Read(ref _generation);
            return new CacheMetricsSnapshot(
                Volatile.Read(ref generation.EntryCount),
                Interlocked.Read(ref _evictionCount));
        }
    }

    /// <summary>
    /// Returns the cached string for the given qualified term span, or interns a new one.
    /// Zero-allocation on cache hit.
    /// </summary>
    public string GetOrAdd(ReadOnlySpan<char> qualifiedTerm)
    {
        if (qualifiedTerm.Length > _maximumQualifiedTermLength)
            return qualifiedTerm.ToString();

        var generation = Volatile.Read(ref _generation);
        var cache = generation.Values;
        if (cache.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(qualifiedTerm, out string? cached))
            return cached;

        string value = qualifiedTerm.ToString();
        if (!cache.TryAdd(value, value))
            return cache.GetOrAdd(value, value);

        int count = Interlocked.Increment(ref generation.EntryCount);
        if (count > _maximumEntries)
            EvictGeneration(generation);

        return value;
    }

    private void EvictGeneration(Generation generation)
    {
        if (Volatile.Read(ref generation.EntryCount) <= _maximumEntries)
            return;

        if (ReferenceEquals(
                Interlocked.CompareExchange(ref _generation, new Generation(), generation),
                generation))
        {
            Interlocked.Increment(ref _evictionCount);
        }
    }

    private sealed class Generation
    {
        internal ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal int EntryCount;
    }
}
