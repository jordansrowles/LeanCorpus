namespace Rowles.LeanCorpus.Store;

/// <summary>Platform-specific durability and transient-error behaviour.</summary>
internal interface IPlatformFileSystem
{
    void SyncFile(string path);

    DirectorySyncResult SyncDirectory(string path);

    bool IsTransient(Exception exception);
}

internal enum DirectorySyncResult
{
    Failed,
    Succeeded,
    Unsupported,
    SkippedUnsupported
}

/// <summary>Selects the filesystem implementation once for the current process.</summary>
internal static class PlatformFileSystem
{
    private const int SyncMaxRetries = 5;
    private const int SyncRetryDelayMilliseconds = 200;
    private static readonly IPlatformFileSystem s_default = OperatingSystem.IsBrowser()
        ? new BrowserFileSystem()
        : OperatingSystem.IsWindows() ? WindowsFileSystem.Instance : PosixFileSystem.Instance;
    private static readonly AsyncLocal<IPlatformFileSystem?> s_override = new();

    internal static IPlatformFileSystem Current => s_override.Value ?? s_default;

    /// <summary>
    /// Rejects an operation whose contract depends on durable barriers before it writes anything.
    /// Browser storage is volatile, so these operations would otherwise fail part-way and leave output.
    /// </summary>
    internal static void ThrowIfBrowser(string operation)
    {
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException($"{operation} requires durable filesystem barriers, which browser storage cannot provide.");
    }

    private sealed class BrowserFileSystem : IPlatformFileSystem
    {
        public void SyncFile(string path) => throw UnsupportedDurability();
        public DirectorySyncResult SyncDirectory(string path) => throw UnsupportedDurability();
        public bool IsTransient(Exception exception) => false;
        private static PlatformNotSupportedException UnsupportedDurability() =>
            new("Browser storage is volatile and cannot provide native file or directory durability barriers.");
    }

    internal static void SyncFile(string path) =>
        SyncFile(Current, path, static delay => Thread.Sleep(delay));

    internal static DirectorySyncResult SyncDirectory(string path) => Current.SyncDirectory(path);

    internal static void SyncFile(
        IPlatformFileSystem fileSystem,
        string path,
        Action<int> delay)
    {
        int retries = SyncMaxRetries;
        while (true)
        {
            try
            {
                fileSystem.SyncFile(path);
                return;
            }
            catch (Exception ex) when (retries > 0 && fileSystem.IsTransient(ex))
            {
                retries--;
                using var measurement = Diagnostics.FileSystemMeasurement.Start("retry");
                measurement?.SetTag("delay_ms", SyncRetryDelayMilliseconds);
                Diagnostics.FileSystemDiagnostics.RecordRetry(SyncRetryDelayMilliseconds);
                delay(SyncRetryDelayMilliseconds);
            }
        }
    }

    internal static IDisposable OverrideForTesting(IPlatformFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        IPlatformFileSystem? previous = s_override.Value;
        s_override.Value = fileSystem;
        return new OverrideScope(previous);
    }

    private sealed class OverrideScope(IPlatformFileSystem? previous) : IDisposable
    {
        private IPlatformFileSystem? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            s_override.Value = _previous;
            _previous = null;
        }
    }
}
