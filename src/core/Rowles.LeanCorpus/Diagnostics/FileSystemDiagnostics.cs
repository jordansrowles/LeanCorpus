using System.Diagnostics;

namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>Process-wide counters for low-level filesystem operations.</summary>
public static class FileSystemDiagnostics
{
    private static long s_filesCreated;
    private static long s_retryCount;
    private static long s_retryDelayMilliseconds;
    private static long s_syncOperationCount;
    private static long s_syncStopwatchTicks;
    private static long s_dirtyRegistrations;
    private static long s_dirtySnapshotCount;
    private static long s_dirtySnapshotEntriesScanned;
    private static long s_dirtySnapshotEntriesReturned;
    private static long s_fileSyncCount;
    private static long s_fileSyncStopwatchTicks;
    private static long s_directorySyncAttemptCount;
    private static long s_directorySyncSuccessCount;
    private static long s_directorySyncUnsupportedCount;
    private static long s_directorySyncSkippedCount;
    private static long s_directorySyncStopwatchTicks;
    private static long s_immediateDurableAtomicWriteCount;
    private static int s_detailedMeasurementScopes;

    /// <summary>Returns a point-in-time snapshot of filesystem counters.</summary>
    public static FileSystemDiagnosticsSnapshot GetSnapshot() => new(
        Interlocked.Read(ref s_filesCreated),
        Interlocked.Read(ref s_retryCount),
        Interlocked.Read(ref s_retryDelayMilliseconds),
        Interlocked.Read(ref s_syncOperationCount),
        Interlocked.Read(ref s_syncStopwatchTicks) * 1000d / Stopwatch.Frequency)
    {
        DirtyRegistrations = Interlocked.Read(ref s_dirtyRegistrations),
        DirtySnapshotCount = Interlocked.Read(ref s_dirtySnapshotCount),
        DirtySnapshotEntriesScanned = Interlocked.Read(ref s_dirtySnapshotEntriesScanned),
        DirtySnapshotEntriesReturned = Interlocked.Read(ref s_dirtySnapshotEntriesReturned),
        FileSyncCount = Interlocked.Read(ref s_fileSyncCount),
        FileSyncElapsedMilliseconds = ToMilliseconds(Interlocked.Read(ref s_fileSyncStopwatchTicks)),
        DirectorySyncAttemptCount = Interlocked.Read(ref s_directorySyncAttemptCount),
        DirectorySyncSuccessCount = Interlocked.Read(ref s_directorySyncSuccessCount),
        DirectorySyncUnsupportedCount = Interlocked.Read(ref s_directorySyncUnsupportedCount),
        DirectorySyncSkippedCount = Interlocked.Read(ref s_directorySyncSkippedCount),
        DirectorySyncElapsedMilliseconds = ToMilliseconds(Interlocked.Read(ref s_directorySyncStopwatchTicks)),
        ImmediateDurableAtomicWriteCount = Interlocked.Read(ref s_immediateDurableAtomicWriteCount)
    };

    /// <summary>
    /// Enables detailed dirty-tracker and atomic-writer counters until the returned scope is disposed.
    /// The counters are disabled by default to keep indexing writes free of extra global atomics.
    /// </summary>
    public static IDisposable BeginDetailedMeasurement()
    {
        Interlocked.Increment(ref s_detailedMeasurementScopes);
        return new DetailedMeasurementScope();
    }

    internal static void RecordFileCreated() => Interlocked.Increment(ref s_filesCreated);

    internal static void RecordRetry(int delayMilliseconds)
    {
        Interlocked.Increment(ref s_retryCount);
        Interlocked.Add(ref s_retryDelayMilliseconds, delayMilliseconds);
    }

    internal static long StartSync() => Stopwatch.GetTimestamp();

    internal static void RecordSync(long startedAt)
    {
        Interlocked.Increment(ref s_syncOperationCount);
        Interlocked.Add(ref s_syncStopwatchTicks, Stopwatch.GetTimestamp() - startedAt);
    }

    internal static void RecordDirtyRegistration()
    {
        if (Volatile.Read(ref s_detailedMeasurementScopes) != 0)
            Interlocked.Increment(ref s_dirtyRegistrations);
    }

    internal static void RecordDirtySnapshot(int entriesScanned, int entriesReturned)
    {
        if (Volatile.Read(ref s_detailedMeasurementScopes) == 0)
            return;
        Interlocked.Increment(ref s_dirtySnapshotCount);
        Interlocked.Add(ref s_dirtySnapshotEntriesScanned, entriesScanned);
        Interlocked.Add(ref s_dirtySnapshotEntriesReturned, entriesReturned);
    }

    internal static long StartFileSync() => Stopwatch.GetTimestamp();

    internal static void RecordFileSync(long startedAt)
    {
        Interlocked.Increment(ref s_fileSyncCount);
        Interlocked.Add(ref s_fileSyncStopwatchTicks, Stopwatch.GetTimestamp() - startedAt);
    }

    internal static long StartDirectorySync() => Stopwatch.GetTimestamp();

    internal static void RecordDirectorySync(long startedAt, Store.DirectorySyncResult result)
    {
        if (result != Store.DirectorySyncResult.SkippedUnsupported)
            Interlocked.Increment(ref s_directorySyncAttemptCount);
        if (result == Store.DirectorySyncResult.Succeeded)
            Interlocked.Increment(ref s_directorySyncSuccessCount);
        else if (result == Store.DirectorySyncResult.Unsupported)
            Interlocked.Increment(ref s_directorySyncUnsupportedCount);
        else if (result == Store.DirectorySyncResult.SkippedUnsupported)
            Interlocked.Increment(ref s_directorySyncSkippedCount);
        Interlocked.Add(ref s_directorySyncStopwatchTicks, Stopwatch.GetTimestamp() - startedAt);
    }

    internal static void RecordImmediateDurableAtomicWrite()
    {
        if (Volatile.Read(ref s_detailedMeasurementScopes) != 0)
            Interlocked.Increment(ref s_immediateDurableAtomicWriteCount);
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private sealed class DetailedMeasurementScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref s_detailedMeasurementScopes);
        }
    }
}

/// <summary>Point-in-time process-wide filesystem counters.</summary>
/// <param name="FilesCreated">Successful create or create-new file opens.</param>
/// <param name="RetryCount">Filesystem operations retried after classified transient failures.</param>
/// <param name="RetryDelayMilliseconds">Total requested retry delay.</param>
/// <param name="SyncOperationCount">File and directory synchronisation calls.</param>
/// <param name="SyncElapsedMilliseconds">Cumulative elapsed synchronisation time.</param>
public readonly record struct FileSystemDiagnosticsSnapshot(
    long FilesCreated,
    long RetryCount,
    long RetryDelayMilliseconds,
    long SyncOperationCount,
    double SyncElapsedMilliseconds)
{
    /// <summary>Gets the number of file versions registered as dirty.</summary>
    public long DirtyRegistrations { get; init; }

    /// <summary>Gets the number of dirty-state snapshots requested by commits.</summary>
    public long DirtySnapshotCount { get; init; }

    /// <summary>Gets the number of entries inspected by dirty-state snapshots.</summary>
    public long DirtySnapshotEntriesScanned { get; init; }

    /// <summary>Gets the number of entries returned by dirty-state snapshots.</summary>
    public long DirtySnapshotEntriesReturned { get; init; }

    /// <summary>Gets the number of file synchronisation operations.</summary>
    public long FileSyncCount { get; init; }

    /// <summary>Gets cumulative elapsed time spent synchronising files.</summary>
    public double FileSyncElapsedMilliseconds { get; init; }

    /// <summary>Gets the number of native directory synchronisation attempts.</summary>
    public long DirectorySyncAttemptCount { get; init; }

    /// <summary>Gets the number of successful native directory synchronisations.</summary>
    public long DirectorySyncSuccessCount { get; init; }

    /// <summary>Gets the number of native directory synchronisations found to be unsupported.</summary>
    public long DirectorySyncUnsupportedCount { get; init; }

    /// <summary>Gets the number of directory synchronisations skipped after capability detection.</summary>
    public long DirectorySyncSkippedCount { get; init; }

    /// <summary>Gets cumulative elapsed time spent in directory synchronisation calls.</summary>
    public double DirectorySyncElapsedMilliseconds { get; init; }

    /// <summary>Gets the number of atomic writers requesting immediate independent durability.</summary>
    public long ImmediateDurableAtomicWriteCount { get; init; }
}
