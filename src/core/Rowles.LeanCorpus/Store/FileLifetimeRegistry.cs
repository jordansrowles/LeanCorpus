namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Coordinates mapped inputs, search snapshots, and deferred deletion across every
/// <see cref="MMapDirectory"/> instance that points at the same directory.
/// </summary>
internal static class FileLifetimeRegistry
{
    private static readonly Dictionary<string, DirectoryState> s_directories = new(PathComparer);
    private static readonly Lock s_lock = new();

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static DirectoryState ForDirectory(string directoryPath)
    {
        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        lock (s_lock)
        {
            if (!s_directories.TryGetValue(canonicalPath, out var state))
            {
                state = new DirectoryState(canonicalPath, PathComparer);
                s_directories.Add(canonicalPath, state);
            }
            return state;
        }
    }

    internal static DirectoryState RegisterDirectory(string directoryPath)
    {
        string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        lock (s_lock)
        {
            if (!s_directories.TryGetValue(canonicalPath, out DirectoryState? state))
            {
                state = new DirectoryState(canonicalPath, PathComparer);
                s_directories.Add(canonicalPath, state);
            }
            state.AddOwner();
            return state;
        }
    }

    internal static void ReleaseDirectory(DirectoryState state)
    {
        lock (s_lock)
        {
            state.ReleaseOwner();
            if (s_directories.TryGetValue(state.DirectoryPath, out DirectoryState? current)
                && ReferenceEquals(current, state)
                && state.IsEvictable())
                s_directories.Remove(state.DirectoryPath);
        }
    }

    internal static int RegisteredDirectoryCount
    {
        get { lock (s_lock) return s_directories.Count; }
    }

    internal static bool IsRegistered(string directoryPath)
    {
        string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        lock (s_lock) return s_directories.ContainsKey(canonicalPath);
    }

    internal static FileLifetimeRegistryDiagnostics GetDiagnostics(string directoryPath)
    {
        string canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        lock (s_lock)
        {
            return s_directories.TryGetValue(canonicalPath, out DirectoryState? state)
                ? state.GetDiagnostics(RegisteredDirectoryCountUnderLock)
                : new(RegisteredDirectoryCountUnderLock, 0, 0, 0, 0, 0);
        }
    }

    private static int RegisteredDirectoryCountUnderLock => s_directories.Count;

    private static void TryEvict(DirectoryState state)
    {
        lock (s_lock)
        {
            if (s_directories.TryGetValue(state.DirectoryPath, out DirectoryState? current)
                && ReferenceEquals(current, state)
                && state.IsEvictable())
                s_directories.Remove(state.DirectoryPath);
        }
    }

    internal sealed class DirectoryState
    {
        private readonly string _directoryPath;
        private readonly Dictionary<string, FileState> _files;
        private readonly Lock _lock = new();
        private int _owners;
        private int _pendingFinaliserCallbacks;
        internal Action? BeforeEvictionForTesting { get; set; }
        internal Action? BeforeFinaliserDeleteCallbackForTesting { get; set; }
        internal Action? AfterFinaliserDeleteCallbackForTesting { get; set; }
        internal string DirectoryPath => _directoryPath;

        internal DirectoryState(string directoryPath, StringComparer comparer)
        {
            _directoryPath = directoryPath;
            _files = new Dictionary<string, FileState>(comparer);
        }

        internal void AddOwner()
        {
            lock (_lock) checked { _owners++; }
        }

        internal void ReleaseOwner()
        {
            lock (_lock)
            {
                if (_owners <= 0)
                    throw new InvalidOperationException("The file-lifetime directory owner count is already zero.");
                _owners--;
            }
        }

        internal bool IsEvictable()
        {
            lock (_lock)
            {
                if (_owners != 0 || _files.Count != 0 || _pendingFinaliserCallbacks != 0)
                    return false;
                BeforeEvictionForTesting?.Invoke();
                return true;
            }
        }

        internal FileLifetimeRegistryDiagnostics GetDiagnostics(int registeredStates)
        {
            lock (_lock)
                return new(registeredStates, _owners, _files.Values.Sum(static state => state.LeaseCount),
                    _files.Values.Count(static state => state.DeletePending), _pendingFinaliserCallbacks, _files.Count);
        }

        internal void PrepareForWrite(string fileName)
        {
            string filePath = GetPath(fileName);
            lock (_lock)
            {
                if (_files.TryGetValue(filePath, out FileState? state))
                {
                    if (state.DeletePending && state.LeaseCount > 0)
                        throw new IOException($"Cannot replace index file '{Path.GetFileName(filePath)}' while retained readers still lease it.");
                    state.DeletePending = false;
                    if (state.LeaseCount == 0 && state.FinaliserCallbacks == 0)
                        _files.Remove(filePath);
                }
            }
            TryEvict(this);
        }

        internal FileLease Acquire(string fileName)
        {
            var filePath = GetPath(fileName);
            FileLease lease;
            lock (_lock)
            {
                GetOrAdd(filePath).LeaseCount++;
                lease = new FileLease(this, filePath);
            }

            if (File.Exists(filePath))
                return lease;

            lease.Dispose();
            throw new FileNotFoundException($"Index file is missing: '{filePath}'.", filePath);
        }

        internal FileSnapshotLease AcquireSnapshot(IReadOnlyCollection<string> fileNames)
        {
            if (fileNames.Count == 0)
                return new FileSnapshotLease(this, []);

            var paths = new string[fileNames.Count];
            int index = 0;
            foreach (var fileName in fileNames)
                paths[index++] = GetPath(fileName);

            lock (_lock)
            {
                foreach (var filePath in paths)
                    GetOrAdd(filePath).LeaseCount++;
            }

            var lease = new FileSnapshotLease(this, paths);
            foreach (var filePath in paths)
            {
                if (File.Exists(filePath))
                    continue;
                lease.Dispose();
                throw new FileNotFoundException($"Index file is missing: '{filePath}'.", filePath);
            }
            return lease;
        }

        internal FileSnapshotLease AcquireSnapshot(
            Func<string, bool> includeFile,
            out string[] inventory)
        {
            inventory = Directory.GetFiles(_directoryPath)
                .Select(Path.GetFileName)
                .Where(static name => name is not null)
                .Select(static name => name!)
                .ToArray();
            var selectedPaths = inventory.Where(includeFile).Select(GetPath).ToArray();

            lock (_lock)
            {
                foreach (var filePath in selectedPaths)
                    GetOrAdd(filePath).LeaseCount++;
            }

            var lease = new FileSnapshotLease(this, selectedPaths);
            foreach (var filePath in selectedPaths)
            {
                if (File.Exists(filePath))
                    continue;
                lease.Dispose();
                throw new FileNotFoundException($"Index file is missing: '{filePath}'.", filePath);
            }
            return lease;
        }

        internal void Delete(string fileName)
        {
            var filePath = GetPath(fileName);
            bool evict;
            lock (_lock)
            {
                if (_files.TryGetValue(filePath, out var state) && (state.LeaseCount > 0 || state.FinaliserCallbacks > 0))
                {
                    Diagnostics.FileSystemMeasurement.Record("delete.deferred");
                    state.DeletePending = true;
                    return;
                }

                GetOrAdd(filePath).DeletePending = true;
                if (TryDelete(filePath, "index file delete"))
                    _files.Remove(filePath);
                evict = _owners == 0 && _files.Count == 0 && _pendingFinaliserCallbacks == 0;
            }
            if (evict) TryEvict(this);
        }

        internal void Release(string filePath)
        {
            bool evict;
            lock (_lock)
            {
                if (!_files.TryGetValue(filePath, out var state))
                    return;

                if (state.LeaseCount > 0)
                    state.LeaseCount--;

                if (state.LeaseCount == 0 && state.FinaliserCallbacks == 0
                    && (!state.DeletePending || TryDelete(filePath, "deferred index file delete")))
                    _files.Remove(filePath);
                evict = _owners == 0 && _files.Count == 0 && _pendingFinaliserCallbacks == 0;
            }
            if (evict) TryEvict(this);
        }

        internal void ReleaseFromFinaliser(string filePath, System.Diagnostics.ActivityContext measurementContext = default)
        {
            FileState? callbackState = null;
            lock (_lock)
            {
                if (!_files.TryGetValue(filePath, out var state))
                    return;

                if (state.LeaseCount > 0)
                    state.LeaseCount--;

                if (state.LeaseCount != 0)
                    return;

                if (!state.DeletePending)
                {
                    _files.Remove(filePath);
                }
                else
                {
                    state.FinaliserCallbacks++;
                    _pendingFinaliserCallbacks++;
                    callbackState = state;
                }
            }

            if (callbackState is not null)
            {
                ThreadPool.UnsafeQueueUserWorkItem(
                    static state => state.Owner.DeleteDeferredFromFinaliser(state.FilePath, state.FileState, state.MeasurementContext),
                    (Owner: this, FilePath: filePath, FileState: callbackState, MeasurementContext: measurementContext),
                    preferLocal: false);
            }
            else TryEvict(this);
        }

        private void DeleteDeferredFromFinaliser(string filePath, FileState expectedState, System.Diagnostics.ActivityContext measurementContext)
        {
            using var measurement = Diagnostics.FileSystemMeasurement.Start("delete.finaliser", measurementContext);
            try
            {
                BeforeFinaliserDeleteCallbackForTesting?.Invoke();
                lock (_lock)
                {
                    if (_files.TryGetValue(filePath, out var state) && ReferenceEquals(state, expectedState)
                        && state.LeaseCount == 0 && state.DeletePending
                        && TryDelete(filePath, "finalised deferred index file delete"))
                        _files.Remove(filePath);
                }
            }
            finally
            {
                lock (_lock)
                {
                    FinishFinaliserCallback(expectedState);
                    if (_files.TryGetValue(filePath, out FileState? current)
                        && ReferenceEquals(current, expectedState)
                        && current.FinaliserCallbacks == 0 && current.LeaseCount == 0 && !current.DeletePending)
                        _files.Remove(filePath);
                }
                TryEvict(this);
                AfterFinaliserDeleteCallbackForTesting?.Invoke();
            }
        }

        private void FinishFinaliserCallback(FileState state)
        {
            if (state.FinaliserCallbacks > 0) state.FinaliserCallbacks--;
            if (_pendingFinaliserCallbacks > 0) _pendingFinaliserCallbacks--;
        }

        internal string[] GetPendingDeletionFiles(IReadOnlyList<string> paths)
        {
            lock (_lock)
            {
                return paths.Where(path => _files.TryGetValue(path, out var state) && state.DeletePending)
                    .Select(Path.GetFileName).Where(static name => name is not null).Select(static name => name!).ToArray();
            }
        }

        internal (int FileLeases, int PendingDeletes) GetResourceSnapshot()
        {
            lock (_lock)
                return (_files.Values.Sum(static state => state.LeaseCount),
                    _files.Values.Count(static state => state.DeletePending));
        }

        internal void CompletePendingDeletes()
        {
            var failures = new List<Exception>();
            lock (_lock)
            {
                foreach (var entry in _files.Where(static entry => entry.Value.DeletePending && entry.Value.LeaseCount == 0 && entry.Value.FinaliserCallbacks == 0).ToArray())
                {
                    try { FileOpenRetry.Delete(entry.Key); _files.Remove(entry.Key); }
                    catch (Exception failure) { failures.Add(failure); }
                }
            }
            if (failures.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("Deferred index cleanup failed.", failures);
            TryEvict(this);
        }

        private FileState GetOrAdd(string filePath)
        {
            if (!_files.TryGetValue(filePath, out var state))
            {
                state = new FileState();
                _files.Add(filePath, state);
            }
            return state;
        }

        private string GetPath(string fileName) => Path.Combine(_directoryPath, fileName);

        private static bool TryDelete(string filePath, string operation)
        {
            try { FileOpenRetry.Delete(filePath); return true; }
            catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, operation); return false; }
        }
    }

    private sealed class FileState
    {
        internal int LeaseCount;
        internal int FinaliserCallbacks;
        internal bool DeletePending;
    }
}

internal sealed record FileLifetimeRegistryDiagnostics(
    int RegisteredDirectoryStates,
    int DirectoryOwners,
    int FileLeases,
    int PendingDeletes,
    int PendingFinaliserCallbacks,
    int TrackedFiles);

/// <summary>A lease over one concrete index file.</summary>
internal sealed class FileLease : IDisposable
{
    private readonly ReleaseToken _token;

    internal FileLease(FileLifetimeRegistry.DirectoryState owner, string filePath)
    {
        _token = new ReleaseToken(owner, filePath);
    }

    public void Dispose() => _token.Dispose();

    internal void ReleaseFromFinaliser(System.Diagnostics.ActivityContext measurementContext = default) =>
        _token.ReleaseFromFinaliser(measurementContext);

    private sealed class ReleaseToken
    {
        private readonly FileLifetimeRegistry.DirectoryState _owner;
        private readonly string _filePath;
        private int _disposed;

        internal ReleaseToken(FileLifetimeRegistry.DirectoryState owner, string filePath)
        {
            _owner = owner;
            _filePath = filePath;
        }

        internal void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.Release(_filePath);
        }

        internal void ReleaseFromFinaliser(System.Diagnostics.ActivityContext measurementContext)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.ReleaseFromFinaliser(_filePath, measurementContext);
        }
    }
}

/// <summary>A lease over every file belonging to a committed searcher snapshot.</summary>
internal sealed class FileSnapshotLease : IDisposable
{
    private FileLifetimeRegistry.DirectoryState? _owner;
    private string[]? _filePaths;

    internal FileSnapshotLease(FileLifetimeRegistry.DirectoryState owner, string[] filePaths)
    {
        _owner = owner;
        _filePaths = filePaths;
    }

    internal long RetainedBytes => _filePaths?.Sum(static path => File.Exists(path) ? new FileInfo(path).Length : 0L) ?? 0;
    internal string[] RetainedFiles => _filePaths?.Select(Path.GetFileName).Where(static name => name is not null).Select(static name => name!).ToArray() ?? [];
    internal string[] PendingDeletionFiles => _owner is not null && _filePaths is not null ? _owner.GetPendingDeletionFiles(_filePaths) : [];

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        var paths = Interlocked.Exchange(ref _filePaths, null);
        if (owner is null || paths is null)
            return;

        foreach (var path in paths)
            owner.Release(path);
    }
}
