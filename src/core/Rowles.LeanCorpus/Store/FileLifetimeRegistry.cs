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
            lock (_lock)
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Index file is missing: '{filePath}'.", filePath);

                GetOrAdd(filePath).LeaseCount++;
                return new FileLease(this, filePath);
            }
        }

        internal FileSnapshotLease AcquireSnapshot(IReadOnlyCollection<string> fileNames)
        {
            if (fileNames.Count == 0)
                return new FileSnapshotLease(this, []);

            var paths = new string[fileNames.Count];
            int index = 0;
            lock (_lock)
            {
                foreach (var fileName in fileNames)
                {
                    var filePath = GetPath(fileName);
                    if (!File.Exists(filePath))
                        throw new FileNotFoundException($"Index file is missing: '{filePath}'.", filePath);
                    paths[index++] = filePath;
                }

                foreach (var filePath in paths)
                    GetOrAdd(filePath).LeaseCount++;
            }
            return new FileSnapshotLease(this, paths);
        }

        internal FileSnapshotLease AcquireSnapshot(
            Func<string, bool> includeFile,
            out string[] inventory)
        {
            lock (_lock)
            {
                inventory = Directory.GetFiles(_directoryPath)
                    .Select(Path.GetFileName)
                    .Where(static name => name is not null)
                    .Select(static name => name!)
                    .ToArray();
                var selected = inventory.Where(includeFile).ToArray();
                foreach (var fileName in selected)
                    GetOrAdd(GetPath(fileName)).LeaseCount++;
                return new FileSnapshotLease(this, selected.Select(GetPath).ToArray());
            }
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

/// <summary>A value-type lease over one concrete index file.</summary>
internal struct FileLease : IDisposable
{
    private FileLifetimeRegistry.DirectoryState? _owner;
    private readonly string? _filePath;

    internal FileLease(FileLifetimeRegistry.DirectoryState owner, string filePath)
    {
        _owner = owner;
        _filePath = filePath;
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null && _filePath is not null)
            owner.Release(_filePath);
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
