using System.Collections.Concurrent;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Microsoft.Extensions.Logging;

namespace Rowles.LeanLucene.Example.JsonApi;

/// <summary>
/// Manages a set of named collections, each backed by its own
/// <see cref="MMapDirectory"/> on disk and a dedicated <see cref="IndexWriter"/>
/// + <see cref="IndexSearcher"/> pair.
/// </summary>
public sealed class CollectionManager : IDisposable
{
    /// <summary>
    /// Per-collection runtime handles. Searchers are never exposed directly; callers
    /// always go through <see cref="SearcherManager.AcquireLease"/>.
    /// </summary>
    private sealed record CollectionEntry(IndexWriter Writer, MMapDirectory Dir, SearcherManager SearcherManager) : IDisposable
    {
        public void Dispose()
        {
            SearcherManager.Dispose();
            Writer.Dispose();
            Dir.Dispose();
        }
    }

    private sealed class CreationLock
    {
        private int _refCount;
        private int _removed;
        private int _disposed;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool IsRemoved => Volatile.Read(ref _removed) != 0;

        public bool TryAddRef()
        {
            if (IsRemoved) return false;

            Interlocked.Increment(ref _refCount);
            if (!IsRemoved) return true;

            ReleaseRef();
            return false;
        }

        public void MarkRemoved()
        {
            Volatile.Write(ref _removed, 1);
            DisposeWhenUnused();
        }

        public void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
                DisposeWhenUnused();
        }

        private void DisposeWhenUnused()
        {
            if (!IsRemoved || Volatile.Read(ref _refCount) != 0)
                return;

            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Semaphore.Dispose();
        }
    }

    private readonly string _rootPath;
    private readonly ILogger<CollectionManager>? _logger;
    private readonly Dictionary<string, CollectionEntry> _collections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> _corruptCollections = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    // Per-collection semaphores ensure that slow IndexWriter construction for a NEW collection
    // does not block reads and writes to existing, already-open collections.
    private readonly ConcurrentDictionary<string, CreationLock> _creationLocks = new(StringComparer.Ordinal);

    public CollectionManager(string rootPath, ILogger<CollectionManager>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _logger = logger;
        Directory.CreateDirectory(rootPath);

        // Re-open any previously persisted collections
        foreach (var dir in Directory.EnumerateDirectories(rootPath))
        {
            string name = Path.GetFileName(dir);
            if (!IsValidCollectionName(name))
            {
                var ex = new InvalidDataException($"Collection directory '{dir}' has an invalid name.");
                _corruptCollections[name] = ex;
                _logger?.LogWarning(ex, "Skipping invalid collection directory {CollectionPath}", dir);
                continue;
            }

            try { EnsureOpen(name); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _corruptCollections[name] = ex;
                _logger?.LogWarning(ex, "Skipping corrupt collection {CollectionName} at {CollectionPath}", name, dir);
            }
        }
    }

    public IReadOnlyDictionary<string, Exception> CorruptCollections
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, Exception>(_corruptCollections, StringComparer.Ordinal);
        }
    }

    public IEnumerable<(string Name, int DocCount)> ListCollections()
    {
        List<(string, int)> result;
        lock (_lock)
        {
            result = _collections
                .Select(kv =>
                {
                    using var lease = kv.Value.SearcherManager.AcquireLease();
                    return (kv.Key, lease.Searcher.Stats.LiveDocCount);
                })
                .ToList();
        }
        return result;
    }

    public IndexWriter GetWriter(string name)
    {
        lock (_lock)
        {
            if (_collections.TryGetValue(name, out var existing)) return existing.Writer;
        }
        return EnsureOpen(name).Writer;
    }

    public SearcherLease AcquireSearcher(string name)
    {
        lock (_lock)
        {
            if (_collections.TryGetValue(name, out var existing))
                return existing.SearcherManager.AcquireLease();
        }
        return EnsureOpen(name).SearcherManager.AcquireLease();
    }

    public bool DropCollection(string name)
    {
        var creationLock = AcquireCreationLock(name);
        creationLock.Semaphore.Wait();
        try
        {
            CollectionEntry entry;
            lock (_lock)
            {
                if (!_collections.Remove(name, out entry!))
                    return false;
            }

            entry.Dispose();
            string colPath = CollectionPath(name);
            if (Directory.Exists(colPath))
                DeleteDirectorySafely(colPath);
            return true;
        }
        finally
        {
            if (_creationLocks.TryRemove(new KeyValuePair<string, CreationLock>(name, creationLock)))
                creationLock.MarkRemoved();
            creationLock.Semaphore.Release();
            creationLock.ReleaseRef();
        }
    }

    public bool Exists(string name)
    {
        lock (_lock) { return _collections.ContainsKey(name); }
    }

    public void CommitAndRefresh(string name)
    {
        CollectionEntry entry;
        lock (_lock)
        {
            if (!_collections.TryGetValue(name, out entry!))
                entry = EnsureOpen(name);
        }
        entry.Writer.Commit();
        entry.SearcherManager.MaybeRefresh();
    }

    /// <summary>
    /// Returns an existing <see cref="CollectionEntry"/>, or creates one if it does not yet exist.
    /// Construction happens outside the global lock to prevent blocking other collections; a
    /// per-collection semaphore serialises concurrent creation of the same new collection.
    /// </summary>
    private CollectionEntry EnsureOpen(string name)
    {
        ValidateCollectionName(name);

        // Fast path: collection already exists.
        lock (_lock)
        {
            if (_collections.TryGetValue(name, out var existing)) return existing;
        }

        while (true)
        {
            // Slow path: creation may involve segment recovery. Use a per-collection semaphore
            // so parallel creates for the SAME name are serialised without blocking other names.
            var creationLock = AcquireCreationLock(name);
            creationLock.Semaphore.Wait();
            try
            {
                if (creationLock.IsRemoved)
                    continue;

                // Re-check after acquiring the semaphore; another thread may have created it.
                lock (_lock)
                {
                    if (_collections.TryGetValue(name, out var existing)) return existing;
                }

                string path = CollectionPath(name);
                Directory.CreateDirectory(path);
                var dir = new MMapDirectory(path);
                var writer = new IndexWriter(dir, new IndexWriterConfig());
                var searcherManager = new SearcherManager(dir);
                var entry = new CollectionEntry(writer, dir, searcherManager);

                lock (_lock)
                {
                    _collections[name] = entry;
                }
                return entry;
            }
            finally
            {
                creationLock.Semaphore.Release();
                creationLock.ReleaseRef();
            }
        }
    }

    private CreationLock AcquireCreationLock(string name)
    {
        while (true)
        {
            var creationLock = _creationLocks.GetOrAdd(name, static _ => new CreationLock());
            if (creationLock.TryAddRef())
                return creationLock;

            _creationLocks.TryRemove(new KeyValuePair<string, CreationLock>(name, creationLock));
        }
    }

    private string CollectionPath(string name)
    {
        ValidateCollectionName(name);
        string path = Path.GetFullPath(Path.Combine(_rootPath, name));
        string root = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException("Collection name escapes the configured data directory.", nameof(name));
        return path;
    }

    public static bool IsValidCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name is "." or "..") return false;
        foreach (char c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                continue;
            return false;
        }
        return true;
    }

    private static void ValidateCollectionName(string name)
    {
        if (!IsValidCollectionName(name))
            throw new ArgumentException("Collection names may contain only ASCII letters, digits, underscores, and hyphens.", nameof(name));
    }

    private static void DeleteDirectorySafely(string path)
    {
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to delete reparse point '{path}'.");

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var entryAttrs = File.GetAttributes(entry);
            if ((entryAttrs & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to delete reparse point '{entry}'.");

            if ((entryAttrs & FileAttributes.Directory) != 0)
            {
                DeleteDirectorySafely(entry);
            }
            else
            {
                if ((entryAttrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry, entryAttrs & ~FileAttributes.ReadOnly);
                File.Delete(entry);
            }
        }

        File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        Directory.Delete(path);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _collections.Values)
                entry.Dispose();
            _collections.Clear();
        }

        foreach (var pair in _creationLocks.ToArray())
        {
            if (_creationLocks.TryRemove(pair))
                pair.Value.MarkRemoved();
        }
    }
}
