using System.Runtime.CompilerServices;
using Rowles.LeanLucene.Store;
namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Manages the lifecycle of <see cref="IndexSearcher"/> instances, automatically
/// refreshing when new commits are detected. Thread-safe acquire/release pattern
/// with reference counting ensures old searchers are disposed only after all
/// in-flight searches complete.
/// </summary>
public sealed class SearcherManager : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly SearcherManagerConfig _config;
    private readonly Lock _swapLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _refreshTask;
    private readonly ConditionalWeakTable<IndexSearcher, SearcherRef> _searchers = new();

    private volatile SearcherRef _current;
    private int _disposed;
    private volatile Exception? _lastRefreshError;
    private long _lastRefreshErrorAtTicks;
    private long _consecutiveRefreshFailures;

    /// <summary>
    /// The exception thrown by the most recent refresh attempt, or null if the most recent
    /// refresh succeeded (or none has run yet).
    /// </summary>
    public Exception? LastRefreshError => _lastRefreshError;

    /// <summary>
    /// The UTC timestamp at which <see cref="LastRefreshError"/> was recorded, or null if
    /// no refresh has failed yet.
    /// </summary>
    public DateTime? LastRefreshErrorAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastRefreshErrorAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Number of consecutive failed refreshes since the last successful one. Reset to zero
    /// on each successful refresh.
    /// </summary>
    public long ConsecutiveRefreshFailures => Interlocked.Read(ref _consecutiveRefreshFailures);

    /// <summary>
    /// Raised when a refresh fails. The exception is also stored on
    /// <see cref="LastRefreshError"/> for callers that prefer polling.
    /// </summary>
    public event EventHandler<RefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// Initialises a new <see cref="SearcherManager"/> for the specified directory, opening an initial
    /// <see cref="IndexSearcher"/> and starting the background refresh loop.
    /// </summary>
    /// <param name="directory">The index directory to manage.</param>
    /// <param name="config">Optional configuration controlling the refresh interval and searcher settings.</param>
    public SearcherManager(MMapDirectory directory, SearcherManagerConfig? config = null)
    {
        _directory = directory;
        _config = config ?? new SearcherManagerConfig();

        // Determine the current commit generation so we don't falsely refresh
        var latestCommit = Index.IndexRecovery.RecoverLatestCommit(directory.DirectoryPath, cleanupOrphans: false);
        int initialGen = latestCommit?.Generation ?? 0;
        long initialContentToken = latestCommit?.ContentToken ?? 0;

        var initialSearcher = new IndexSearcher(directory, _config.SearcherConfig);
        _current = new SearcherRef(initialSearcher, initialGen, initialContentToken);
        _searchers.Add(initialSearcher, _current);
        _refreshTask = Task.Run(() => RefreshLoop(_cts.Token));
    }

    /// <summary>
    /// Acquires a scoped reference to the current searcher. Disposing the returned
    /// <see cref="SearcherLease"/> releases the reference. This is the preferred
    /// alternative to <see cref="Acquire"/> + <see cref="Release"/>: the lease
    /// bypasses the <c>ConditionalWeakTable</c> lookup performed by <c>Release</c>.
    /// </summary>
    public SearcherLease AcquireLease()
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var sr = _current;
            if (sr.TryIncrementRef())
                return new SearcherLease(sr.Searcher, sr.DecrementRef);
            Thread.SpinWait(1);
        }
    }

    /// <summary>
    /// Acquires a reference to the current searcher. The caller must call
    /// <see cref="Release"/> when done. The searcher remains valid until released.
    /// </summary>
    public IndexSearcher Acquire()
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var sr = _current;
            if (sr.TryIncrementRef())
                return sr.Searcher;
            // The ref was retired between reading _current and incrementing;
            // _current has already been swapped to a live ref — spin and retry.
            Thread.SpinWait(1);
        }
    }

    /// <summary>
    /// Releases a previously acquired searcher. If this was the last reference,
    /// it will be disposed.
    /// </summary>
    public void Release(IndexSearcher searcher)
    {
        if (_searchers.TryGetValue(searcher, out var sr))
            sr.DecrementRef();
    }

    /// <summary>
    /// Convenience method: acquires a searcher, runs the action, and releases it.
    /// </summary>
    public T UsingSearcher<T>(Func<IndexSearcher, T> action)
    {
        var searcher = Acquire();
        try { return action(searcher); }
        finally { Release(searcher); }
    }

    /// <summary>
    /// Synchronously checks for a new commit and swaps in a fresh searcher if one is found.
    /// Returns true if the searcher was refreshed.
    /// </summary>
    public bool MaybeRefresh()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return TryRefresh();
    }

    /// <summary>Async variant of <see cref="MaybeRefresh"/>.</summary>
    public Task<bool> MaybeRefreshAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(MaybeRefresh, ct);
    }

    /// <summary>Stops the background refresh loop and disposes the current searcher.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _cts.Cancel();
        try { _refreshTask.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { /* Expected: task cancelled during shutdown */ }
        catch (ObjectDisposedException) { /* CTS already disposed */ }
        _cts.Dispose();
        lock (_swapLock)
        {
            _current.Retire();
        }
    }

    private async Task RefreshLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.RefreshInterval, ct);
                TryRefresh();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                RecordRefreshFailure(ex);
            }
        }
    }

    private void RecordRefreshFailure(Exception ex)
    {
        _lastRefreshError = ex;
        Interlocked.Exchange(ref _lastRefreshErrorAtTicks, DateTime.UtcNow.Ticks);
        var failures = Interlocked.Increment(ref _consecutiveRefreshFailures);
        try { RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(ex, failures)); }
        catch { /* never let a subscriber's exception break the refresh loop */ }
    }

    private bool TryRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        try
        {
            var refreshed = TryRefreshCore();
            // Successful path resets failure counter.
            Interlocked.Exchange(ref _consecutiveRefreshFailures, 0);
            return refreshed;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            RecordRefreshFailure(ex);
            return false;
        }
    }

    private bool TryRefreshCore()
    {
        // Check if the commit generation on disk is newer than what we have
        var latestCommit = Index.IndexRecovery.RecoverLatestCommit(_directory.DirectoryPath, cleanupOrphans: false);
        if (latestCommit is null) return false;

        if (latestCommit.Generation <= _current.Generation)
            return false;

        lock (_swapLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;

            // Double-check under lock
            if (latestCommit.Generation <= _current.Generation)
                return false;

            if (latestCommit.ContentToken == _current.ContentToken)
            {
                _current.Generation = latestCommit.Generation;
                return false;
            }

            var newSearcher = new IndexSearcher(_directory, _config.SearcherConfig);
            var newRef = new SearcherRef(newSearcher, latestCommit.Generation, latestCommit.ContentToken);
            _searchers.Add(newSearcher, newRef);

            var oldRef = _current;
            _current = newRef;
            oldRef.Retire();
            return true;
        }
    }

    /// <summary>Reference-counted wrapper around an IndexSearcher.</summary>
    private sealed class SearcherRef
    {
        public IndexSearcher Searcher { get; }
        public int Generation { get; set; }
        public long ContentToken { get; }
        private int _refCount = 1; // 1 = the owner/publish reference held by _current

        public SearcherRef(IndexSearcher searcher, int generation = 0, long contentToken = 0)
        {
            Searcher = searcher;
            Generation = generation;
            ContentToken = contentToken;
        }

        /// <summary>
        /// Attempts to increment the ref count atomically. Returns false if the count
        /// is already zero (the ref has been retired), allowing <see cref="SearcherManager.Acquire"/>
        /// to retry with a fresh <see cref="SearcherRef"/>.
        /// </summary>
        public bool TryIncrementRef()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _refCount);
                if (current <= 0) return false;
            } while (Interlocked.CompareExchange(ref _refCount, current + 1, current) != current);
            return true;
        }

        public void DecrementRef()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
                Searcher.Dispose();
        }

        /// <summary>
        /// Releases the owner/publish reference. Called by <see cref="SearcherManager"/> when
        /// this ref is swapped out or when the manager is disposed.
        /// </summary>
        public void Retire() => DecrementRef();
    }
}
