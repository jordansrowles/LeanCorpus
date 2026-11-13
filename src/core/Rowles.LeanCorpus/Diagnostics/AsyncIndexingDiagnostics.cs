using System.Diagnostics;

namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Process-wide counters for asynchronous indexing measurements. They are disabled by
/// default, so release indexing paths pay one volatile read and no global atomics unless a
/// benchmark or test opens a measurement scope.
/// </summary>
internal static class AsyncIndexingDiagnostics
{
    private static int s_measurementScopes;
    private static long s_queueAdmissions;
    private static long s_admittedDocuments;
    private static long s_maximumCommandDocuments;
    private static long s_producerWaits;
    private static long s_waitingProducers;
    private static long s_maximumWaitingProducers;
    private static long s_completedCommands;
    private static long s_dequeueLagTicks;
    private static long s_completionLagTicks;
    private static long s_maximumCompletionLagTicks;
    private static long s_flushSnapshots;
    private static long s_flushSnapshotDocuments;
    private static long s_publishedSegments;
    private static long s_commits;
    private static long s_lastCommittedSegmentCount;

    /// <summary>Gets whether at least one measurement scope is open.</summary>
    internal static bool IsEnabled => Volatile.Read(ref s_measurementScopes) != 0;

    /// <summary>Enables counters until the returned scope is disposed.</summary>
    internal static IDisposable BeginMeasurement()
    {
        Interlocked.Increment(ref s_measurementScopes);
        return new MeasurementScope();
    }

    /// <summary>Clears all counters. Call between measured operations, not while one runs.</summary>
    internal static void Reset()
    {
        Interlocked.Exchange(ref s_queueAdmissions, 0);
        Interlocked.Exchange(ref s_admittedDocuments, 0);
        Interlocked.Exchange(ref s_maximumCommandDocuments, 0);
        Interlocked.Exchange(ref s_producerWaits, 0);
        Interlocked.Exchange(ref s_waitingProducers, 0);
        Interlocked.Exchange(ref s_maximumWaitingProducers, 0);
        Interlocked.Exchange(ref s_completedCommands, 0);
        Interlocked.Exchange(ref s_dequeueLagTicks, 0);
        Interlocked.Exchange(ref s_completionLagTicks, 0);
        Interlocked.Exchange(ref s_maximumCompletionLagTicks, 0);
        Interlocked.Exchange(ref s_flushSnapshots, 0);
        Interlocked.Exchange(ref s_flushSnapshotDocuments, 0);
        Interlocked.Exchange(ref s_publishedSegments, 0);
        Interlocked.Exchange(ref s_commits, 0);
        Interlocked.Exchange(ref s_lastCommittedSegmentCount, 0);
    }

    /// <summary>Returns a point-in-time copy of the counters.</summary>
    internal static AsyncIndexingDiagnosticsSnapshot GetSnapshot() => new(
        QueueAdmissions: Interlocked.Read(ref s_queueAdmissions),
        AdmittedDocuments: Interlocked.Read(ref s_admittedDocuments),
        MaximumCommandDocuments: Interlocked.Read(ref s_maximumCommandDocuments),
        ProducerWaits: Interlocked.Read(ref s_producerWaits),
        MaximumWaitingProducers: Interlocked.Read(ref s_maximumWaitingProducers),
        CompletedCommands: Interlocked.Read(ref s_completedCommands),
        TotalDequeueLagMilliseconds: ToMilliseconds(Interlocked.Read(ref s_dequeueLagTicks)),
        TotalCompletionLagMilliseconds: ToMilliseconds(Interlocked.Read(ref s_completionLagTicks)),
        MaximumCompletionLagMilliseconds: ToMilliseconds(Interlocked.Read(ref s_maximumCompletionLagTicks)),
        FlushSnapshots: Interlocked.Read(ref s_flushSnapshots),
        FlushSnapshotDocuments: Interlocked.Read(ref s_flushSnapshotDocuments),
        PublishedSegments: Interlocked.Read(ref s_publishedSegments),
        Commits: Interlocked.Read(ref s_commits),
        LastCommittedSegmentCount: Interlocked.Read(ref s_lastCommittedSegmentCount));

    /// <summary>Returns a timestamp when enabled, otherwise zero.</summary>
    internal static long TimestampIfEnabled() => IsEnabled ? Stopwatch.GetTimestamp() : 0;

    internal static void RecordAdmission(int documents)
    {
        Interlocked.Increment(ref s_queueAdmissions);
        Interlocked.Add(ref s_admittedDocuments, documents);
        UpdateMaximum(ref s_maximumCommandDocuments, documents);
    }

    internal static void RecordProducerWaitStarted()
    {
        Interlocked.Increment(ref s_producerWaits);
        UpdateMaximum(ref s_maximumWaitingProducers, Interlocked.Increment(ref s_waitingProducers));
    }

    internal static void RecordProducerWaitEnded() => Interlocked.Decrement(ref s_waitingProducers);

    internal static void RecordDequeued(long requestedTimestamp)
    {
        if (requestedTimestamp == 0)
            return;
        Interlocked.Add(ref s_dequeueLagTicks, Stopwatch.GetTimestamp() - requestedTimestamp);
    }

    internal static void RecordCompleted(long requestedTimestamp)
    {
        if (requestedTimestamp == 0)
            return;
        long lag = Stopwatch.GetTimestamp() - requestedTimestamp;
        Interlocked.Increment(ref s_completedCommands);
        Interlocked.Add(ref s_completionLagTicks, lag);
        UpdateMaximum(ref s_maximumCompletionLagTicks, lag);
    }

    internal static void RecordFlushSnapshot(int documents)
    {
        if (!IsEnabled)
            return;
        Interlocked.Increment(ref s_flushSnapshots);
        Interlocked.Add(ref s_flushSnapshotDocuments, documents);
    }

    internal static void RecordPublishedSegments(int segments)
    {
        if (!IsEnabled || segments <= 0)
            return;
        Interlocked.Add(ref s_publishedSegments, segments);
    }

    internal static void RecordCommit(int committedSegmentCount)
    {
        if (!IsEnabled)
            return;
        Interlocked.Increment(ref s_commits);
        Interlocked.Exchange(ref s_lastCommittedSegmentCount, committedSegmentCount);
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        long current = Volatile.Read(ref target);
        while (value > current)
        {
            long observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private sealed class MeasurementScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref s_measurementScopes);
        }
    }
}

/// <summary>Point-in-time asynchronous indexing counters.</summary>
internal readonly record struct AsyncIndexingDiagnosticsSnapshot(
    long QueueAdmissions,
    long AdmittedDocuments,
    long MaximumCommandDocuments,
    long ProducerWaits,
    long MaximumWaitingProducers,
    long CompletedCommands,
    double TotalDequeueLagMilliseconds,
    double TotalCompletionLagMilliseconds,
    double MaximumCompletionLagMilliseconds,
    long FlushSnapshots,
    long FlushSnapshotDocuments,
    long PublishedSegments,
    long Commits,
    long LastCommittedSegmentCount)
{
    /// <summary>Formats the counters as one invariant key-value line.</summary>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"admissions={QueueAdmissions}, admittedDocs={AdmittedDocuments}, maxCommandDocs={MaximumCommandDocuments}, " +
        $"producerWaits={ProducerWaits}, maxWaitingProducers={MaximumWaitingProducers}, completed={CompletedCommands}, " +
        $"dequeueLagMs={TotalDequeueLagMilliseconds:F3}, completionLagMs={TotalCompletionLagMilliseconds:F3}, " +
        $"maxCompletionLagMs={MaximumCompletionLagMilliseconds:F3}, flushSnapshots={FlushSnapshots}, " +
        $"flushSnapshotDocs={FlushSnapshotDocuments}, publishedSegments={PublishedSegments}, commits={Commits}, " +
        $"committedSegments={LastCommittedSegmentCount}");
}
