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

    internal sealed class DirectoryState
    {
        private readonly string _directoryPath;
        private readonly Dictionary<string, FileState> _files;
        private readonly Lock _lock = new();

        internal DirectoryState(string directoryPath, StringComparer comparer)
        {
            _directoryPath = directoryPath;
            _files = new Dictionary<string, FileState>(comparer);
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
            lock (_lock)
            {
                if (_files.TryGetValue(filePath, out var state) && state.LeaseCount > 0)
                {
                    state.DeletePending = true;
                    return;
                }

                TryDelete(filePath, "index file delete");
                _files.Remove(filePath);
            }
        }

        internal void Release(string filePath)
        {
            lock (_lock)
            {
                if (!_files.TryGetValue(filePath, out var state))
                    return;

                if (state.LeaseCount > 0)
                    state.LeaseCount--;

                if (state.LeaseCount != 0)
                    return;

                if (state.DeletePending)
                    TryDelete(filePath, "deferred index file delete");
                _files.Remove(filePath);
            }
        }

        internal void ReleaseFromFinaliser(string filePath)
        {
            bool queueDeferredDelete = false;
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
                    return;
                }

                queueDeferredDelete = true;
            }

            if (queueDeferredDelete)
            {
                ThreadPool.UnsafeQueueUserWorkItem(
                    static state => state.Owner.DeleteDeferredFromFinaliser(state.FilePath),
                    (Owner: this, FilePath: filePath),
                    preferLocal: false);
            }
        }

        private void DeleteDeferredFromFinaliser(string filePath)
        {
            lock (_lock)
            {
                if (!_files.TryGetValue(filePath, out var state) ||
                    state.LeaseCount != 0 ||
                    !state.DeletePending)
                    return;

                TryDelete(filePath, "finalised deferred index file delete");
                _files.Remove(filePath);
            }
        }

        internal string[] GetPendingDeletionFiles(IReadOnlyList<string> paths)
        {
            lock (_lock)
            {
                return paths.Where(path => _files.TryGetValue(path, out var state) && state.DeletePending)
                    .Select(Path.GetFileName).Where(static name => name is not null).Select(static name => name!).ToArray();
            }
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

        private static void TryDelete(string filePath, string operation)
        {
            try { FileOpenRetry.Delete(filePath); }
            catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, operation); }
        }
    }

    private sealed class FileState
    {
        internal int LeaseCount;
        internal bool DeletePending;
    }
}

/// <summary>A lease over one concrete index file.</summary>
internal sealed class FileLease : IDisposable
{
    private readonly ReleaseToken _token;

    internal FileLease(FileLifetimeRegistry.DirectoryState owner, string filePath)
    {
        _token = new ReleaseToken(owner, filePath);
    }

    public void Dispose() => _token.Dispose();

    internal void ReleaseFromFinaliser() => _token.ReleaseFromFinaliser();

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

        internal void ReleaseFromFinaliser()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.ReleaseFromFinaliser(_filePath);
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
