using System.Diagnostics;
using System.Globalization;
using Rowles.LeanCorpus.Diagnostics;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Per-iteration ingest observations for benchmarks that build an index in the timed method.
/// It records flush, scheduled-merge and commit events from the writer's metrics collector,
/// stage boundaries (indexing, commit, writer disposal) and process CPU, GC and allocation
/// deltas. The output line is printed outside the timed region so raw iteration order is kept.
/// </summary>
internal sealed class IngestIterationProbe : IMetricsCollector
{
    private readonly string _label;
    private readonly long _startTimestamp;
    private readonly TimeSpan _startCpu;
    private readonly long _startAllocated;
    private readonly int _startGen0;
    private readonly int _startGen1;
    private readonly int _startGen2;
    private long _indexedTimestamp;
    private long _committedTimestamp;
    private long _disposedTimestamp;
    private int _flushes;
    private long _flushTicks;
    private int _mergesScheduled;
    private long _mergeBytesScheduled;
    private long _firstMergeScheduledTimestamp;
    private long _lastMergeScheduledTimestamp;
    private int _commits;

    public IngestIterationProbe(string label)
    {
        _label = label;
        _startCpu = Process.GetCurrentProcess().TotalProcessorTime;
        _startAllocated = GC.GetTotalAllocatedBytes(precise: true);
        _startGen0 = GC.CollectionCount(0);
        _startGen1 = GC.CollectionCount(1);
        _startGen2 = GC.CollectionCount(2);
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void MarkIndexed() => _indexedTimestamp = Stopwatch.GetTimestamp();

    public void MarkCommitted() => _committedTimestamp = Stopwatch.GetTimestamp();

    public void MarkDisposed() => _disposedTimestamp = Stopwatch.GetTimestamp();

    /// <summary>Formats the iteration line. Call after the timed method has returned.</summary>
    public string Describe(int iteration)
    {
        long now = Stopwatch.GetTimestamp();
        double cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - _startCpu).TotalMilliseconds;
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - _startAllocated;
        long firstMerge = Volatile.Read(ref _firstMergeScheduledTimestamp);
        long lastMerge = Volatile.Read(ref _lastMergeScheduledTimestamp);
        return string.Create(CultureInfo.InvariantCulture,
            $"// Ingest iteration: method={_label}, iteration={iteration}, " +
            $"indexMs={Ms(_startTimestamp, _indexedTimestamp):F1}, commitMs={Ms(_indexedTimestamp, _committedTimestamp):F1}, " +
            $"disposeMs={Ms(_committedTimestamp, _disposedTimestamp):F1}, totalMs={Ms(_startTimestamp, _disposedTimestamp):F1}, " +
            $"flushes={Volatile.Read(ref _flushes)}, flushMs={Volatile.Read(ref _flushTicks) * 1000d / Stopwatch.Frequency:F1}, " +
            $"mergesScheduled={Volatile.Read(ref _mergesScheduled)}, mergeBytesScheduled={Volatile.Read(ref _mergeBytesScheduled)}, " +
            $"firstMergeAtMs={(firstMerge == 0 ? -1 : Ms(_startTimestamp, firstMerge)):F1}, " +
            $"lastMergeAtMs={(lastMerge == 0 ? -1 : Ms(_startTimestamp, lastMerge)):F1}, commits={Volatile.Read(ref _commits)}, " +
            $"cpuMs={cpuMs:F1}, allocatedBytes={allocated}, gen0={GC.CollectionCount(0) - _startGen0}, " +
            $"gen1={GC.CollectionCount(1) - _startGen1}, gen2={GC.CollectionCount(2) - _startGen2}, " +
            $"cleanupLagMs={Ms(_disposedTimestamp, now):F1}");
    }

    private static double Ms(long from, long to) => from == 0 || to == 0 ? -1 : (to - from) * 1000d / Stopwatch.Frequency;

    public void RecordFlush(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _flushes);
        Interlocked.Add(ref _flushTicks, (long)(elapsed.TotalSeconds * Stopwatch.Frequency));
    }

    public void RecordMergeBacklog(long pendingBytes, TimeSpan producerStall)
    {
        long now = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _firstMergeScheduledTimestamp, now, 0);
        Volatile.Write(ref _lastMergeScheduledTimestamp, now);
        Interlocked.Increment(ref _mergesScheduled);
        Interlocked.Add(ref _mergeBytesScheduled, pendingBytes);
    }

    public void RecordCommit(TimeSpan elapsed) => Interlocked.Increment(ref _commits);

    public void RecordMerge(TimeSpan elapsed, int segmentsMerged) { }

    public void RecordSearchLatency(TimeSpan elapsed) { }

    public void RecordCacheHit() { }

    public void RecordCacheMiss() { }

    public MetricsSnapshot GetSnapshot() => MetricsSnapshot.Empty;
}
