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
        CancellationToken cancellationToken = default)
        => AcquireAsync(repositoryRoot, ExecutionLeaseKind.Shared, waitTimeout, cancellationToken);

    public static Task<ExecutionLease> AcquireExclusiveAsync(
        string repositoryRoot,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
        => AcquireAsync(repositoryRoot, ExecutionLeaseKind.Exclusive, waitTimeout, cancellationToken);

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
                        await DelayOrTimeoutAsync(deadline, waitTimeout, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (kind == ExecutionLeaseKind.Exclusive && ownsPromotionGate && !HasLiveReaders(leaseDirectory))
                {
                    return new ExecutionLease(promotionPath, kind);
                }

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

    private static async Task DelayOrTimeoutAsync(Stopwatch elapsed, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var remaining = timeout - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateMarkerAsync(string path, CancellationToken cancellationToken)
    {
        var (processId, processStartedUtc) = GetCurrentProcessIdentity();
        var marker = JsonSerializer.SerializeToUtf8Bytes(new LeaseMarker(processId, processStartedUtc));
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(marker, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
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
            using var stream = File.OpenRead(path);
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
