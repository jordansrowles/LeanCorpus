using System.Collections.Concurrent;
using System.Threading;
using Rowles.LeanCorpus.Diagnostics;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>A read-heavy cache that drops a whole generation after its soft entry cap is exceeded.</summary>
internal sealed class BoundedGenerationCache<TKey, TValue>
    where TKey : notnull
    where TValue : notnull
{
    private readonly int _maximumEntries;
    private Generation _generation = new();
    private long _evictionCount;

    internal BoundedGenerationCache(int maximumEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        _maximumEntries = maximumEntries;
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

    internal TValue GetOrAdd<TArgument>(
        TKey key,
        TArgument argument,
        Func<TKey, TArgument, TValue> valueFactory)
    {
        var generation = Volatile.Read(ref _generation);
        if (generation.Values.TryGetValue(key, out var cached))
            return cached;

        TValue value = valueFactory(key, argument);
        if (!generation.Values.TryAdd(key, value))
            return generation.Values.GetOrAdd(key, value);

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
        internal ConcurrentDictionary<TKey, TValue> Values { get; } = new();
        internal int EntryCount;
    }
}
