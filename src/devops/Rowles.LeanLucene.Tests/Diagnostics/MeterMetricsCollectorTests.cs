using System.Diagnostics.Metrics;
using Rowles.LeanLucene.Diagnostics;

namespace Rowles.LeanLucene.Tests.Diagnostics;

/// <summary>
/// Contains unit tests for Meter Metrics Collector.
/// </summary>
public sealed class MeterMetricsCollectorTests : IDisposable
{
    private readonly MeterMetricsCollector _collector = new();

    public void Dispose() => _collector.Dispose();

    /// <summary>
    /// Verifies the Record Search Latency: Updates Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Record Search Latency: Updates Snapshot")]
    public void RecordSearchLatency_UpdatesSnapshot()
    {
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(10));
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(30));

        var snap = _collector.GetSnapshot();
        Assert.Equal(2, snap.SearchCount);
        Assert.Equal(40, snap.SearchTotalMs);
        Assert.Equal(30, snap.SearchMaxMs);
        Assert.Equal(20.0, snap.SearchAvgMs, 1);
    }

    /// <summary>
    /// Verifies the Record Cache Hit Miss: Updates Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Record Cache Hit Miss: Updates Snapshot")]
    public void RecordCacheHitMiss_UpdatesSnapshot()
    {
        _collector.RecordCacheHit();
        _collector.RecordCacheHit();
        _collector.RecordCacheMiss();

        var snap = _collector.GetSnapshot();
        Assert.Equal(2, snap.CacheHits);
        Assert.Equal(1, snap.CacheMisses);
        Assert.InRange(snap.CacheHitRate, 0.66, 0.68);
    }

    /// <summary>
    /// Verifies the Record Flush: Updates Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Record Flush: Updates Snapshot")]
    public void RecordFlush_UpdatesSnapshot()
    {
        _collector.RecordFlush(TimeSpan.FromMilliseconds(50));
        _collector.RecordFlush(TimeSpan.FromMilliseconds(20));

        var snap = _collector.GetSnapshot();
        Assert.Equal(2, snap.FlushCount);
        Assert.Equal(70, snap.FlushTotalMs);
    }

    /// <summary>
    /// Verifies the Record Merge: Updates Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Record Merge: Updates Snapshot")]
    public void RecordMerge_UpdatesSnapshot()
    {
        _collector.RecordMerge(TimeSpan.FromMilliseconds(100), 3);
        _collector.RecordMerge(TimeSpan.FromMilliseconds(200), 5);

        var snap = _collector.GetSnapshot();
        Assert.Equal(2, snap.MergeCount);
        Assert.Equal(8, snap.MergeSegments);
        Assert.Equal(300, snap.MergeTotalMs);
    }

    /// <summary>
    /// Verifies the Record Commit: Updates Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Record Commit: Updates Snapshot")]
    public void RecordCommit_UpdatesSnapshot()
    {
        _collector.RecordCommit(TimeSpan.FromMilliseconds(40));

        var snap = _collector.GetSnapshot();
        Assert.Equal(1, snap.CommitCount);
        Assert.Equal(40, snap.CommitTotalMs);
    }

    /// <summary>
    /// Verifies the Latency Histogram: Buckets Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Latency Histogram: Buckets Correctly")]
    public void LatencyHistogram_BucketsCorrectly()
    {
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(0.5));  // bucket 0 (<1ms)
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(3));    // bucket 1 (<5ms)
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(7));    // bucket 2 (<10ms)
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(30));   // bucket 3 (<50ms)
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(2000)); // bucket 7 (>=1000ms)

        var snap = _collector.GetSnapshot();
        Assert.NotNull(snap.LatencyHistogram);
        Assert.Equal(8, snap.LatencyHistogram.Length);
        Assert.Equal(1, snap.LatencyHistogram[0]);
        Assert.Equal(1, snap.LatencyHistogram[1]);
        Assert.Equal(1, snap.LatencyHistogram[2]);
        Assert.Equal(1, snap.LatencyHistogram[3]);
        Assert.Equal(1, snap.LatencyHistogram[7]);
    }

    /// <summary>
    /// Verifies the Meter Listener: Receives Search Duration Measurement scenario.
    /// </summary>
    [Fact(DisplayName = "Meter Listener: Receives Search Duration Measurement")]
    public void MeterListener_ReceivesSearchDurationMeasurement()
    {
        var measurements = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Rowles.LeanLucene" &&
                instrument.Name == "leanlucene.search.duration")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(25));
        _collector.RecordSearchLatency(TimeSpan.FromMilliseconds(75));

        listener.RecordObservableInstruments();

        Assert.Equal(2, measurements.Count);
        Assert.Contains(25.0, measurements);
        Assert.Contains(75.0, measurements);
    }

    /// <summary>
    /// Verifies the Meter Listener: Receives Counter Increments scenario.
    /// </summary>
    [Fact(DisplayName = "Meter Listener: Receives Counter Increments")]
    public void MeterListener_ReceivesCounterIncrements()
    {
        long hitTotal = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Rowles.LeanLucene" &&
                instrument.Name == "leanlucene.cache.hits")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => hitTotal += value);
        listener.Start();

        _collector.RecordCacheHit();
        _collector.RecordCacheHit();
        _collector.RecordCacheHit();

        Assert.Equal(3, hitTotal);
    }

    /// <summary>
    /// Verifies the Dispose: Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Dispose: Does Not Throw")]
    public void Dispose_DoesNotThrow()
    {
        var c = new MeterMetricsCollector();
        var ex = Record.Exception(() => c.Dispose());
        Assert.Null(ex);
    }

    /// <summary>
    /// Verifies the Dispose: Is Idempotent scenario.
    /// </summary>
    [Fact(DisplayName = "Dispose: Is Idempotent")]
    public void Dispose_IsIdempotent()
    {
        var c = new MeterMetricsCollector();
        c.Dispose();
        var ex = Record.Exception(() => c.Dispose());
        Assert.Null(ex);
    }
}
