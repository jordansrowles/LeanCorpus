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

    /// <summary>Returns a point-in-time snapshot of filesystem counters.</summary>
    public static FileSystemDiagnosticsSnapshot GetSnapshot() => new(
        Interlocked.Read(ref s_filesCreated),
        Interlocked.Read(ref s_retryCount),
        Interlocked.Read(ref s_retryDelayMilliseconds),
        Interlocked.Read(ref s_syncOperationCount),
        Interlocked.Read(ref s_syncStopwatchTicks) * 1000d / Stopwatch.Frequency);

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
    double SyncElapsedMilliseconds);
