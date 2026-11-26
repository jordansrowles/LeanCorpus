using System.Diagnostics;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal enum ExecutionLeaseKind
{
    Shared,
    Exclusive
}

internal sealed class ExecutionLease : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);
    private readonly string _markerPath;
    private int _disposed;

    private ExecutionLease(string markerPath, ExecutionLeaseKind kind)
    {
        _markerPath = markerPath;
        Kind = kind;
    }

    public ExecutionLeaseKind Kind { get; }

    public static Task<ExecutionLease> AcquireSharedAsync(
        string repositoryRoot,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default,
        Action<string>? onWaiting = null)
        => AcquireAsync(repositoryRoot, ExecutionLeaseKind.Shared, waitTimeout, onWaiting, cancellationToken);

    /// <summary>Acquires the exclusive lease, waiting up to <paramref name="waitTimeout"/>.</summary>
    /// <param name="onWaiting">
    /// Called once, with a message naming the holder, the first time the lease is found to be held,
    /// so a caller can say why it is waiting instead of appearing to hang.
    /// </param>
    public static Task<ExecutionLease> AcquireExclusiveAsync(
        string repositoryRoot,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default,
        Action<string>? onWaiting = null)
        => AcquireAsync(repositoryRoot, ExecutionLeaseKind.Exclusive, waitTimeout, onWaiting, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            File.Delete(_markerPath);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<ExecutionLease> AcquireAsync(
        string repositoryRoot,
        ExecutionLeaseKind kind,
        TimeSpan waitTimeout,
        Action<string>? onWaiting,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (waitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTimeout), "Lease wait timeout must be greater than zero.");
        }

        var leaseDirectory = Path.Combine(Path.GetFullPath(repositoryRoot), "artifacts", "devops", "leases");
        Directory.CreateDirectory(leaseDirectory);
        var promotionPath = Path.Combine(leaseDirectory, "promotion.lock");
        var deadline = Stopwatch.StartNew();
        string? acquiredPath = null;
        var ownsPromotionGate = false;
        var waitReported = false;
        void ReportWait()
        {
            if (waitReported || onWaiting is null)
                return;
            waitReported = true;
            onWaiting(DescribeWait(kind, waitTimeout, leaseDirectory, promotionPath, ownsPromotionGate));
        }

        try
        {
            while (deadline.Elapsed < waitTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveStaleMarkers(leaseDirectory);

                if (kind == ExecutionLeaseKind.Shared)
                {
                    if (File.Exists(promotionPath))
                    {
                        ReportWait();
                        await DelayOrTimeoutAsync(deadline, waitTimeout, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    acquiredPath = Path.Combine(leaseDirectory, $"reader-{Guid.NewGuid():N}.json");
                    await CreateMarkerAsync(acquiredPath, cancellationToken).ConfigureAwait(false);
                    if (!File.Exists(promotionPath))
                    {
                        return new ExecutionLease(acquiredPath, kind);
                    }

                    File.Delete(acquiredPath);
                    acquiredPath = null;
                }
                else if (!ownsPromotionGate)
                {
                    try
                    {
                        await CreateMarkerAsync(promotionPath, cancellationToken).ConfigureAwait(false);
                        ownsPromotionGate = true;
                    }
                    catch (IOException) when (File.Exists(promotionPath))
                    {
                        ReportWait();
                        await DelayOrTimeoutAsync(deadline, waitTimeout, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (kind == ExecutionLeaseKind.Exclusive && ownsPromotionGate && !HasLiveReaders(leaseDirectory))
                {
                    return new ExecutionLease(promotionPath, kind);
                }

                ReportWait();
                await DelayOrTimeoutAsync(deadline, waitTimeout, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"Could not acquire the {kind.ToString().ToLowerInvariant()} DevOps execution lease within {waitTimeout}.");
        }
        catch
        {
            if (acquiredPath is not null)
            {
                File.Delete(acquiredPath);
            }

            if (ownsPromotionGate)
            {
                File.Delete(promotionPath);
            }

            throw;
        }
    }

    /// <summary>Describes who holds the lease a caller is about to wait for.</summary>
    internal static string DescribeWait(
        ExecutionLeaseKind kind, TimeSpan waitTimeout, string leaseDirectory, string promotionPath, bool ownsPromotionGate)
    {
        string holder;
        if (!ownsPromotionGate && TryReadMarker(promotionPath, out var marker))
        {
            holder = $"held exclusively by process {marker.ProcessId} (started {marker.ProcessStartedUtc:u})";
        }
        else
        {
            int readers = Directory.EnumerateFiles(leaseDirectory, "reader-*.json").Count();
            holder = readers == 1 ? "held by 1 shared reader" : $"held by {readers} shared readers";
        }

        return $"Waiting up to {waitTimeout:g} for the {kind.ToString().ToLowerInvariant()} DevOps execution lease, {holder} ({promotionPath}).";
    }

    private static bool TryReadMarker(string path, out LeaseMarker marker)
    {
        marker = null!;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var read = JsonSerializer.Deserialize<LeaseMarker>(stream);
            if (read is null)
                return false;
            marker = read;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static async Task DelayOrTimeoutAsync(Stopwatch elapsed, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var remaining = timeout - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a marker file atomically from the caller's point of view: if writing it fails or is
    /// cancelled after the file was created, the file is removed, so a caller that sees an
    /// exception never leaves behind a marker it does not know it owns.
    /// </summary>
    internal static async Task CreateMarkerAsync(string path, CancellationToken cancellationToken)
    {
        var (processId, processStartedUtc) = GetCurrentProcessIdentity();
        var marker = JsonSerializer.SerializeToUtf8Bytes(new LeaseMarker(processId, processStartedUtc));
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        try
        {
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(marker, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static bool HasLiveReaders(string leaseDirectory)
    {
        foreach (var path in Directory.EnumerateFiles(leaseDirectory, "reader-*.json"))
        {
            if (ReadLiveMarker(path))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveStaleMarkers(string leaseDirectory)
    {
        var promotionPath = Path.Combine(leaseDirectory, "promotion.lock");
        if (File.Exists(promotionPath) && !ReadLiveMarker(promotionPath))
        {
            TryDelete(promotionPath);
        }

        foreach (var path in Directory.EnumerateFiles(leaseDirectory, "reader-*.json"))
        {
            if (!ReadLiveMarker(path))
            {
                TryDelete(path);
            }
        }
    }

    private static bool ReadLiveMarker(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var marker = JsonSerializer.Deserialize<LeaseMarker>(stream);
            if (marker is null)
            {
                return true;
            }

            using var process = Process.GetProcessById(marker.ProcessId);
            return Math.Abs((process.StartTime.ToUniversalTime() - marker.ProcessStartedUtc.UtcDateTime).TotalSeconds) < 2;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static (int ProcessId, DateTimeOffset StartedUtc) GetCurrentProcessIdentity()
    {
        using var process = Process.GetCurrentProcess();
        return (process.Id, process.StartTime.ToUniversalTime());
    }

    private sealed record LeaseMarker(int ProcessId, DateTimeOffset ProcessStartedUtc);
}
