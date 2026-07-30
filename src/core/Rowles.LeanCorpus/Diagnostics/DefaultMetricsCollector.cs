namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Lock-free metrics collector using Interlocked operations.
/// </summary>
public sealed class DefaultMetricsCollector : IMetricsCollector
{
#pragma warning disable CS0169 // padding fields for false-sharing prevention
    // ── Cache line 1: Search counters (updated on every search) ──
    private long _searchCount;
    private long _searchTotalMs;
    private long _searchMaxMs;
    private long _pad1_0, _pad1_1, _pad1_2, _pad1_3, _pad1_4; // pad to 64 B

    // ── Cache line 2: Cache counters (updated on every query) ──
    private long _cacheHits;
    private long _cacheMisses;
    private long _pad2_0, _pad2_1, _pad2_2, _pad2_3, _pad2_4, _pad2_5;

    // ── Cache line 3: Flush counters ──
    private long _flushCount;
    private long _flushTotalMs;
    private long _pad3_0, _pad3_1, _pad3_2, _pad3_3, _pad3_4, _pad3_5;

    // ── Cache line 4: Merge counters ──
    private long _mergeCount;
    private long _mergeSegments;
    private long _mergeTotalMs;
    private long _pad4_0, _pad4_1, _pad4_2, _pad4_3, _pad4_4;

    // ── Cache line 5: Commit counters ──
    private long _commitCount;
    private long _commitTotalMs;
    private long _pad5_0, _pad5_1, _pad5_2, _pad5_3, _pad5_4, _pad5_5;

    // ── Cache line 6: HNSW counters ──
    private long _hnswSearchCount;
    private long _hnswSearchTotalMs;
    private long _hnswNodesVisited;
    private long _hnswRetryCount;
    private long _hnswBuildCount;
    private long _hnswBuildTotalMs;
    private long _hnswNodesBuilt;
    private long _pad6_0;

    // ── Cache line 7: Vector execution facts ──
    private long _vectorExecutionCount;
    private long _vectorExactCandidateSetCount;
    private long _vectorApproximateCandidateSetCount;
    private long _vectorCandidateCount;
    private long _vectorEligibleCount;
    private long _vectorCandidateGenerationTotalMs;
    private long _vectorRerankingTotalMs;
    private readonly long[] _vectorStrategyCounts = new long[Enum.GetValues<VectorExecutionStrategy>().Length];
    private readonly long[] _vectorScorePrecisionCounts = new long[Enum.GetValues<VectorScorePrecision>().Length];

#pragma warning restore CS0169

    // ── Cache line 7: Latency histogram (8-long array = 64 B, already aligned) ──
    private readonly long[] _latencyBuckets = new long[8];
    private static readonly int[] BucketThresholdsMs = [1, 5, 10, 50, 100, 500, 1000];

    /// <inheritdoc/>
    public void RecordSearchLatency(TimeSpan elapsed)
    {
        long ms = (long)elapsed.TotalMilliseconds;
        Interlocked.Increment(ref _searchCount);
        Interlocked.Add(ref _searchTotalMs, ms);
        InterlockedMax(ref _searchMaxMs, ms);

        int bucket = 0;
        for (int i = 0; i < BucketThresholdsMs.Length; i++)
        {
            if (ms < BucketThresholdsMs[i]) { bucket = i; break; }
            bucket = i + 1;
        }
        Interlocked.Increment(ref _latencyBuckets[bucket]);
    }

    /// <inheritdoc/>
    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

    /// <inheritdoc/>
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    /// <inheritdoc/>
    public void RecordFlush(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _flushCount);
        Interlocked.Add(ref _flushTotalMs, (long)elapsed.TotalMilliseconds);
    }

    /// <inheritdoc/>
    public void RecordMerge(TimeSpan elapsed, int segmentsMerged)
    {
        Interlocked.Increment(ref _mergeCount);
        Interlocked.Add(ref _mergeSegments, segmentsMerged);
        Interlocked.Add(ref _mergeTotalMs, (long)elapsed.TotalMilliseconds);
    }

    /// <inheritdoc/>
    public void RecordCommit(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _commitCount);
        Interlocked.Add(ref _commitTotalMs, (long)elapsed.TotalMilliseconds);
    }

    /// <inheritdoc/>
    public void RecordHnswSearch(TimeSpan elapsed, int nodesVisited)
        => RecordHnswSearch(elapsed, nodesVisited, retryCount: 0);

    /// <inheritdoc/>
    public void RecordHnswSearch(TimeSpan elapsed, int nodesVisited, int retryCount)
    {
        Interlocked.Increment(ref _hnswSearchCount);
        Interlocked.Add(ref _hnswSearchTotalMs, (long)elapsed.TotalMilliseconds);
        Interlocked.Add(ref _hnswNodesVisited, nodesVisited);
        Interlocked.Add(ref _hnswRetryCount, retryCount);
    }

    /// <inheritdoc/>
    public void RecordHnswBuild(TimeSpan elapsed, int nodes)
    {
        Interlocked.Increment(ref _hnswBuildCount);
        Interlocked.Add(ref _hnswBuildTotalMs, (long)elapsed.TotalMilliseconds);
        Interlocked.Add(ref _hnswNodesBuilt, nodes);
    }

    /// <inheritdoc/>
    public void RecordVectorExecution(VectorExecutionMetrics metrics)
    {
        Interlocked.Increment(ref _vectorExecutionCount);
        if (metrics.ExactCandidateSet)
            Interlocked.Increment(ref _vectorExactCandidateSetCount);
        else
            Interlocked.Increment(ref _vectorApproximateCandidateSetCount);
        Interlocked.Add(ref _vectorCandidateCount, metrics.CandidateCount);
        Interlocked.Add(ref _vectorEligibleCount, metrics.EligibleCount);
        Interlocked.Add(ref _vectorCandidateGenerationTotalMs,
            (long)metrics.CandidateGenerationElapsed.TotalMilliseconds);
        Interlocked.Add(ref _vectorRerankingTotalMs,
            (long)metrics.RerankingElapsed.TotalMilliseconds);
        Interlocked.Increment(ref _vectorStrategyCounts[(int)metrics.Strategy]);
        Interlocked.Increment(ref _vectorScorePrecisionCounts[(int)metrics.ScorePrecision]);
    }

    /// <inheritdoc/>
    public MetricsSnapshot GetSnapshot()
    {
        long searchCount = Interlocked.Read(ref _searchCount);
        long hits = Interlocked.Read(ref _cacheHits);
        long misses = Interlocked.Read(ref _cacheMisses);
        long totalCacheLookups = hits + misses;

        var buckets = new long[_latencyBuckets.Length];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = Interlocked.Read(ref _latencyBuckets[i]);

        return new MetricsSnapshot
        {
            SearchCount = searchCount,
            SearchTotalMs = Interlocked.Read(ref _searchTotalMs),
            SearchMaxMs = Interlocked.Read(ref _searchMaxMs),
            SearchAvgMs = searchCount > 0 ? (double)Interlocked.Read(ref _searchTotalMs) / searchCount : 0,
            CacheHits = hits,
            CacheMisses = misses,
            CacheHitRate = totalCacheLookups > 0 ? (double)hits / totalCacheLookups : 0,
            FlushCount = Interlocked.Read(ref _flushCount),
            FlushTotalMs = Interlocked.Read(ref _flushTotalMs),
            MergeCount = Interlocked.Read(ref _mergeCount),
            MergeSegments = Interlocked.Read(ref _mergeSegments),
            MergeTotalMs = Interlocked.Read(ref _mergeTotalMs),
            CommitCount = Interlocked.Read(ref _commitCount),
            CommitTotalMs = Interlocked.Read(ref _commitTotalMs),
            LatencyHistogram = buckets,
            HnswSearchCount = Interlocked.Read(ref _hnswSearchCount),
            HnswSearchTotalMs = Interlocked.Read(ref _hnswSearchTotalMs),
            HnswNodesVisited = Interlocked.Read(ref _hnswNodesVisited),
            HnswRetryCount = Interlocked.Read(ref _hnswRetryCount),
            HnswBuildCount = Interlocked.Read(ref _hnswBuildCount),
            HnswBuildTotalMs = Interlocked.Read(ref _hnswBuildTotalMs),
            HnswNodesBuilt = Interlocked.Read(ref _hnswNodesBuilt),
            VectorExecutionCount = Interlocked.Read(ref _vectorExecutionCount),
            VectorExactCandidateSetCount = Interlocked.Read(ref _vectorExactCandidateSetCount),
            VectorApproximateCandidateSetCount = Interlocked.Read(ref _vectorApproximateCandidateSetCount),
            VectorCandidateCount = Interlocked.Read(ref _vectorCandidateCount),
            VectorEligibleCount = Interlocked.Read(ref _vectorEligibleCount),
            VectorCandidateGenerationTotalMs = Interlocked.Read(ref _vectorCandidateGenerationTotalMs),
            VectorRerankingTotalMs = Interlocked.Read(ref _vectorRerankingTotalMs),
            VectorStrategyCounts = SnapshotCounts<VectorExecutionStrategy>(_vectorStrategyCounts),
            VectorScorePrecisionCounts = SnapshotCounts<VectorScorePrecision>(_vectorScorePrecisionCounts)
        };
    }

    private static IReadOnlyDictionary<TEnum, long> SnapshotCounts<TEnum>(long[] counters)
        where TEnum : struct, Enum
    {
        var result = new Dictionary<TEnum, long>(counters.Length);
        foreach (TEnum value in Enum.GetValues<TEnum>())
            result[value] = Interlocked.Read(ref counters[Convert.ToInt32(value)]);
        return result;
    }

    private static void InterlockedMax(ref long location, long value)
    {
        long current = Interlocked.Read(ref location);
        while (value > current)
        {
            long prev = Interlocked.CompareExchange(ref location, value, current);
            if (prev == current) break;
            current = prev;
        }
    }
}
