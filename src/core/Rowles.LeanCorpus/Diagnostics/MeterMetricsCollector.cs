using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// <see cref="IMetricsCollector"/> backed by <see cref="System.Diagnostics.Metrics.Meter"/>.
/// Instruments are published under the meter name <c>Rowles.LeanCorpus</c> and can be consumed
/// by any <see cref="MeterListener"/> — including OpenTelemetry OTLP exporters.
/// </summary>
/// <remarks>
/// Construct once and assign to both <see cref="Search.Searcher.IndexSearcherConfig.Metrics"/>
/// and <see cref="Index.Indexer.IndexWriterConfig.Metrics"/> so all operations share the same meter.
/// Pass an <see cref="IMeterFactory"/> when using the Microsoft.Extensions.DependencyInjection hosting
/// model; otherwise a standalone <see cref="Meter"/> is created automatically.
/// </remarks>
public sealed class MeterMetricsCollector : IMetricsCollector, IDisposable
{
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    private readonly Histogram<double> _searchDuration;
    private readonly Counter<long> _searchCount;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _flushDuration;
    private readonly Histogram<double> _mergeDuration;
    private readonly Counter<long> _mergeSegments;
    private readonly Histogram<double> _commitDuration;
    private readonly Histogram<double> _hnswSearchDuration;
    private readonly Histogram<long> _hnswNodesVisited;
    private readonly Histogram<long> _hnswRetryCount;
    private readonly Histogram<double> _hnswBuildDuration;
    private readonly Histogram<long> _hnswBuildSize;
    private readonly Histogram<double> _vectorCandidateGenerationDuration;
    private readonly Histogram<double> _vectorRerankingDuration;
    private readonly Counter<long> _vectorExecutionCount;
    private readonly Counter<long> _vectorCandidateCount;
    private readonly Counter<long> _vectorEligibleCount;

#pragma warning disable CS0169 // padding fields for false-sharing prevention
    // ── Cache line 1: Search ──
    private long _snSearchCount;
    private long _snSearchTotalMs;
    private long _snSearchMaxMs;
    private long _padSn1_0, _padSn1_1, _padSn1_2, _padSn1_3, _padSn1_4;

    // ── Cache line 2: Cache ──
    private long _snCacheHits;
    private long _snCacheMisses;
    private long _padSn2_0, _padSn2_1, _padSn2_2, _padSn2_3, _padSn2_4, _padSn2_5;

    // ── Cache line 3: Flush ──
    private long _snFlushCount;
    private long _snFlushTotalMs;
    private long _padSn3_0, _padSn3_1, _padSn3_2, _padSn3_3, _padSn3_4, _padSn3_5;

    // ── Cache line 4: Merge ──
    private long _snMergeCount;
    private long _snMergeSegments;
    private long _snMergeTotalMs;
    private long _padSn4_0, _padSn4_1, _padSn4_2, _padSn4_3, _padSn4_4;

    // ── Cache line 5: Commit ──
    private long _snCommitCount;
    private long _snCommitTotalMs;
    private long _padSn5_0, _padSn5_1, _padSn5_2, _padSn5_3, _padSn5_4, _padSn5_5;

    // ── Cache line 6: HNSW ──
    private long _snHnswSearchCount;
    private long _snHnswSearchTotalMs;
    private long _snHnswNodesVisited;
    private long _snHnswRetryCount;
    private long _snHnswBuildCount;
    private long _snHnswBuildTotalMs;
    private long _snHnswNodesBuilt;
    private long _padSn6_0;

    // ── Cache line 7: Vector execution ──
    private long _snVectorExecutionCount;
    private long _snVectorExactCandidateSetCount;
    private long _snVectorApproximateCandidateSetCount;
    private long _snVectorCandidateCount;
    private long _snVectorEligibleCount;
    private long _snVectorCandidateGenerationTotalMs;
    private long _snVectorRerankingTotalMs;
    private readonly long[] _snVectorStrategyCounts = new long[Enum.GetValues<VectorExecutionStrategy>().Length];
    private readonly long[] _snVectorScorePrecisionCounts = new long[Enum.GetValues<VectorScorePrecision>().Length];

#pragma warning restore CS0169

    // ── Cache line 7: Latency (array = 64 B) ──
    private readonly long[] _snLatencyBuckets = new long[8];
    private static readonly int[] BucketThresholdsMs = [1, 5, 10, 50, 100, 500, 1000];

    /// <summary>
    /// Initialises a <see cref="MeterMetricsCollector"/> using the provided <see cref="IMeterFactory"/>
    /// (for hosted / DI scenarios). If <paramref name="meterFactory"/> is <see langword="null"/> a
    /// standalone <see cref="Meter"/> is created and owned by this instance.
    /// </summary>
    public MeterMetricsCollector(IMeterFactory? meterFactory = null)
    {
        if (meterFactory is not null)
        {
            _meter = meterFactory.Create("Rowles.LeanCorpus");
            _ownsMeter = false;
        }
        else
        {
            _meter = LeanCorpusMaintenanceMetrics.Meter;
            _ownsMeter = false;
        }

        _searchDuration = _meter.CreateHistogram<double>(
            "leancorpus.search.duration", unit: "ms",
            description: "Elapsed time for each search operation.");

        _searchCount = _meter.CreateCounter<long>(
            "leancorpus.search.count", unit: "{query}",
            description: "Total number of search operations executed.");

        _cacheHits = _meter.CreateCounter<long>(
            "leancorpus.cache.hits", unit: "{hit}",
            description: "Number of query cache hits.");

        _cacheMisses = _meter.CreateCounter<long>(
            "leancorpus.cache.misses", unit: "{miss}",
            description: "Number of query cache misses.");

        _flushDuration = _meter.CreateHistogram<double>(
            "leancorpus.index.flush.duration", unit: "ms",
            description: "Elapsed time for each segment flush.");

        _mergeDuration = _meter.CreateHistogram<double>(
            "leancorpus.index.merge.duration", unit: "ms",
            description: "Elapsed time for each segment merge.");

        _mergeSegments = _meter.CreateCounter<long>(
            "leancorpus.index.merge.segments", unit: "{segment}",
            description: "Total number of segments consumed by merge operations.");

        _commitDuration = _meter.CreateHistogram<double>(
            "leancorpus.index.commit.duration", unit: "ms",
            description: "Elapsed time for each index commit.");

        _hnswSearchDuration = _meter.CreateHistogram<double>(
            "leancorpus.hnsw.search.duration", unit: "ms",
            description: "Elapsed time for each HNSW graph traversal.");

        _hnswNodesVisited = _meter.CreateHistogram<long>(
            "leancorpus.hnsw.search.nodes_visited", unit: "{node}",
            description: "Distinct nodes visited per HNSW search; primary recall-vs-cost signal.");

        _hnswRetryCount = _meter.CreateHistogram<long>(
            "leancorpus.hnsw.search.retries", unit: "{retry}",
            description: "Post-filter retry traversals per HNSW search.");

        _hnswBuildDuration = _meter.CreateHistogram<double>(
            "leancorpus.hnsw.build.duration", unit: "ms",
            description: "Elapsed time for each HNSW graph build (flush or merge).");

        _hnswBuildSize = _meter.CreateHistogram<long>(
            "leancorpus.hnsw.build.nodes", unit: "{node}",
            description: "Nodes inserted per HNSW build operation.");

        _vectorCandidateGenerationDuration = _meter.CreateHistogram<double>(
            "leancorpus.vector.candidate_generation.duration", unit: "ms",
            description: "Candidate-generation time per searched vector segment.");
        _vectorRerankingDuration = _meter.CreateHistogram<double>(
            "leancorpus.vector.reranking.duration", unit: "ms",
            description: "Exact or reconstructed reranking time per searched vector segment.");
        _vectorExecutionCount = _meter.CreateCounter<long>(
            "leancorpus.vector.execution.count", unit: "{segment}",
            description: "Vector candidate-generation executions by segment.");
        _vectorCandidateCount = _meter.CreateCounter<long>(
            "leancorpus.vector.execution.candidates", unit: "{candidate}",
            description: "Candidates considered by vector execution.");
        _vectorEligibleCount = _meter.CreateCounter<long>(
            "leancorpus.vector.execution.eligible", unit: "{candidate}",
            description: "Eligible filtered candidates observed by vector execution.");
    }

    /// <inheritdoc/>
    public void RecordSearchLatency(TimeSpan elapsed)
    {
        double ms = elapsed.TotalMilliseconds;
        _searchDuration.Record(ms);
        _searchCount.Add(1);

        Interlocked.Increment(ref _snSearchCount);
        long msLong = (long)ms;
        Interlocked.Add(ref _snSearchTotalMs, msLong);
        InterlockedMax(ref _snSearchMaxMs, msLong);

        int bucket = 0;
        for (int i = 0; i < BucketThresholdsMs.Length; i++)
        {
            if (msLong < BucketThresholdsMs[i]) { bucket = i; break; }
            bucket = i + 1;
        }
        Interlocked.Increment(ref _snLatencyBuckets[bucket]);
    }

    /// <inheritdoc/>
    public void RecordCacheHit()
    {
        _cacheHits.Add(1);
        Interlocked.Increment(ref _snCacheHits);
    }

    /// <inheritdoc/>
    public void RecordCacheMiss()
    {
        _cacheMisses.Add(1);
        Interlocked.Increment(ref _snCacheMisses);
    }

    /// <inheritdoc/>
    public void RecordFlush(TimeSpan elapsed)
    {
        double ms = elapsed.TotalMilliseconds;
        _flushDuration.Record(ms);

        Interlocked.Increment(ref _snFlushCount);
        Interlocked.Add(ref _snFlushTotalMs, (long)ms);
    }

    /// <inheritdoc/>
    public void RecordMerge(TimeSpan elapsed, int segmentsMerged)
    {
        double ms = elapsed.TotalMilliseconds;
        _mergeDuration.Record(ms);
        _mergeSegments.Add(segmentsMerged);

        Interlocked.Increment(ref _snMergeCount);
        Interlocked.Add(ref _snMergeSegments, segmentsMerged);
        Interlocked.Add(ref _snMergeTotalMs, (long)ms);
    }

    /// <inheritdoc/>
    public void RecordCommit(TimeSpan elapsed)
    {
        double ms = elapsed.TotalMilliseconds;
        _commitDuration.Record(ms);

        Interlocked.Increment(ref _snCommitCount);
        Interlocked.Add(ref _snCommitTotalMs, (long)ms);
    }

    /// <inheritdoc/>
    public void RecordHnswSearch(TimeSpan elapsed, int nodesVisited)
        => RecordHnswSearch(elapsed, nodesVisited, retryCount: 0);

    /// <inheritdoc/>
    public void RecordHnswSearch(TimeSpan elapsed, int nodesVisited, int retryCount)
    {
        double ms = elapsed.TotalMilliseconds;
        _hnswSearchDuration.Record(ms);
        _hnswNodesVisited.Record(nodesVisited);
        _hnswRetryCount.Record(retryCount);

        Interlocked.Increment(ref _snHnswSearchCount);
        Interlocked.Add(ref _snHnswSearchTotalMs, (long)ms);
        Interlocked.Add(ref _snHnswNodesVisited, nodesVisited);
        Interlocked.Add(ref _snHnswRetryCount, retryCount);
    }

    /// <inheritdoc/>
    public void RecordHnswBuild(TimeSpan elapsed, int nodes)
    {
        double ms = elapsed.TotalMilliseconds;
        _hnswBuildDuration.Record(ms);
        _hnswBuildSize.Record(nodes);

        Interlocked.Increment(ref _snHnswBuildCount);
        Interlocked.Add(ref _snHnswBuildTotalMs, (long)ms);
        Interlocked.Add(ref _snHnswNodesBuilt, nodes);
    }

    /// <inheritdoc/>
    public void RecordVectorExecution(VectorExecutionMetrics metrics)
    {
        var tags = new TagList
        {
            { "strategy", metrics.Strategy.ToString() },
            { "precision", metrics.ScorePrecision.ToString() },
            { "exact_candidate_set", metrics.ExactCandidateSet },
        };
        _vectorCandidateGenerationDuration.Record(
            metrics.CandidateGenerationElapsed.TotalMilliseconds, tags);
        _vectorRerankingDuration.Record(metrics.RerankingElapsed.TotalMilliseconds, tags);
        _vectorExecutionCount.Add(1, tags);
        _vectorCandidateCount.Add(metrics.CandidateCount, tags);
        _vectorEligibleCount.Add(metrics.EligibleCount, tags);

        Interlocked.Increment(ref _snVectorExecutionCount);
        if (metrics.ExactCandidateSet)
            Interlocked.Increment(ref _snVectorExactCandidateSetCount);
        else
            Interlocked.Increment(ref _snVectorApproximateCandidateSetCount);
        Interlocked.Add(ref _snVectorCandidateCount, metrics.CandidateCount);
        Interlocked.Add(ref _snVectorEligibleCount, metrics.EligibleCount);
        Interlocked.Add(ref _snVectorCandidateGenerationTotalMs,
            (long)metrics.CandidateGenerationElapsed.TotalMilliseconds);
        Interlocked.Add(ref _snVectorRerankingTotalMs,
            (long)metrics.RerankingElapsed.TotalMilliseconds);
        Interlocked.Increment(ref _snVectorStrategyCounts[(int)metrics.Strategy]);
        Interlocked.Increment(ref _snVectorScorePrecisionCounts[(int)metrics.ScorePrecision]);
    }

    /// <inheritdoc/>
    public MetricsSnapshot GetSnapshot()
    {
        long searchCount = Interlocked.Read(ref _snSearchCount);
        long hits = Interlocked.Read(ref _snCacheHits);
        long misses = Interlocked.Read(ref _snCacheMisses);
        long total = hits + misses;

        var buckets = new long[_snLatencyBuckets.Length];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = Interlocked.Read(ref _snLatencyBuckets[i]);

        return new MetricsSnapshot
        {
            SearchCount = searchCount,
            SearchTotalMs = Interlocked.Read(ref _snSearchTotalMs),
            SearchMaxMs = Interlocked.Read(ref _snSearchMaxMs),
            SearchAvgMs = searchCount > 0 ? (double)Interlocked.Read(ref _snSearchTotalMs) / searchCount : 0,
            CacheHits = hits,
            CacheMisses = misses,
            CacheHitRate = total > 0 ? (double)hits / total : 0,
            FlushCount = Interlocked.Read(ref _snFlushCount),
            FlushTotalMs = Interlocked.Read(ref _snFlushTotalMs),
            MergeCount = Interlocked.Read(ref _snMergeCount),
            MergeSegments = Interlocked.Read(ref _snMergeSegments),
            MergeTotalMs = Interlocked.Read(ref _snMergeTotalMs),
            CommitCount = Interlocked.Read(ref _snCommitCount),
            CommitTotalMs = Interlocked.Read(ref _snCommitTotalMs),
            LatencyHistogram = buckets,
            HnswSearchCount = Interlocked.Read(ref _snHnswSearchCount),
            HnswSearchTotalMs = Interlocked.Read(ref _snHnswSearchTotalMs),
            HnswNodesVisited = Interlocked.Read(ref _snHnswNodesVisited),
            HnswRetryCount = Interlocked.Read(ref _snHnswRetryCount),
            HnswBuildCount = Interlocked.Read(ref _snHnswBuildCount),
            HnswBuildTotalMs = Interlocked.Read(ref _snHnswBuildTotalMs),
            HnswNodesBuilt = Interlocked.Read(ref _snHnswNodesBuilt),
            VectorExecutionCount = Interlocked.Read(ref _snVectorExecutionCount),
            VectorExactCandidateSetCount = Interlocked.Read(ref _snVectorExactCandidateSetCount),
            VectorApproximateCandidateSetCount = Interlocked.Read(ref _snVectorApproximateCandidateSetCount),
            VectorCandidateCount = Interlocked.Read(ref _snVectorCandidateCount),
            VectorEligibleCount = Interlocked.Read(ref _snVectorEligibleCount),
            VectorCandidateGenerationTotalMs = Interlocked.Read(ref _snVectorCandidateGenerationTotalMs),
            VectorRerankingTotalMs = Interlocked.Read(ref _snVectorRerankingTotalMs),
            VectorStrategyCounts = SnapshotCounts<VectorExecutionStrategy>(_snVectorStrategyCounts),
            VectorScorePrecisionCounts = SnapshotCounts<VectorScorePrecision>(_snVectorScorePrecisionCounts)
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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsMeter) _meter.Dispose();
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
