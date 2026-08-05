namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Owns an immutable, reference-counted reader and swaps in replacements produced by a caller-supplied refresh function.
/// </summary>
/// <typeparam name="TReader">The disposable reader type managed by this instance.</typeparam>
public sealed class ReaderManager<TReader> : IDisposable
    where TReader : class, IDisposable
{
    private readonly Func<TReader, TReader?> _refreshFactory;
    private readonly TimeSpan _refreshInterval;
    private readonly Lock _lock = new();
    private readonly Lock _refreshLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _refreshLoopExited = new(false);
    private readonly List<ReaderRef> _readers = [];
    private readonly Task _refreshTask;
    private volatile ReaderRef _current;
    private int _disposed;
    private long _refreshes;
    private long _refreshFailures;
    private long _disposedReaders;
    private int _backgroundRefreshes;
    private long _consecutiveFailures;
    private long _lastRefreshErrorAtTicks;
    private volatile Exception? _lastRefreshError;

    /// <summary>
    /// Initialises a reader manager and starts its background refresh loop.
    /// </summary>
    /// <param name="openFactory">Creates the initial reader.</param>
    /// <param name="refreshFactory">Returns a replacement reader, or <c>null</c> when the current reader is still current.</param>
    /// <param name="refreshInterval">The background refresh interval.</param>
    public ReaderManager(Func<TReader> openFactory, Func<TReader, TReader?> refreshFactory, TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(openFactory);
        ArgumentNullException.ThrowIfNull(refreshFactory);
        _refreshFactory = refreshFactory;
        _refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(1);
        if (_refreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));

        var reader = openFactory() ?? throw new InvalidOperationException("The reader factory returned null.");
        _current = new ReaderRef(reader, this);
        _readers.Add(_current);
        _refreshTask = Task.Run(() => RefreshLoop(_cts.Token));
    }

    /// <summary>Gets the most recent refresh exception, or <c>null</c> when none has failed.</summary>
    public Exception? LastRefreshError => _lastRefreshError;

    /// <summary>Gets the UTC time at which the most recent refresh exception was recorded.</summary>
    public DateTime? LastRefreshErrorAt
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastRefreshErrorAtTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Gets the number of consecutive failed refreshes.</summary>
    public long ConsecutiveRefreshFailures => Interlocked.Read(ref _consecutiveFailures);

    /// <summary>Raised when the refresh factory fails.</summary>
    public event EventHandler<ReaderRefreshFailedEventArgs>? RefreshFailed;

    /// <summary>Acquires the current reader until the returned lease is disposed.</summary>
    public ReaderLease<TReader> AcquireLease()
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var current = _current;
            if (current.TryAcquire())
                return new ReaderLease<TReader>(current.Reader, () => current.ReleaseLease());
            Thread.Yield();
        }
    }

    /// <summary>Acquires the current reader and requires the caller to release it explicitly.</summary>
    public TReader Acquire()
    {
        while (true)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var current = _current;
            if (current.TryAcquire())
                return current.Reader;
            Thread.Yield();
        }
    }

    /// <summary>Releases a reader acquired through <see cref="Acquire"/>.</summary>
    public void Release(TReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (_lock)
        {
            foreach (var candidate in _readers)
            {
                if (ReferenceEquals(candidate.Reader, reader))
                {
                    candidate.ReleaseLease();
                    return;
                }
            }
        }
    }

    /// <summary>Acquires a reader matching the supplied predicate, or returns <c>false</c> when none is retained.</summary>
    public bool TryAcquire(Func<TReader, bool> predicate, out ReaderLease<TReader> lease)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_lock)
        {
            for (int i = _readers.Count - 1; i >= 0; i--)
            {
                var candidate = _readers[i];
                if (candidate.IsRetired || !predicate(candidate.Reader) || !candidate.TryAcquire())
                    continue;
                lease = new ReaderLease<TReader>(candidate.Reader, () => candidate.ReleaseLease());
                return true;
            }
        }

        lease = default;
        return false;
    }

    /// <summary>Attempts one synchronous refresh and returns whether a replacement was published.</summary>
    public bool MaybeRefresh()
        => TryRefresh(background: false);

    internal bool ConsumeBackgroundRefreshes()
        => Interlocked.Exchange(ref _backgroundRefreshes, 0) != 0;

    private bool TryRefresh(bool background)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_refreshLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;

            try
            {
                var current = _current;
                var replacement = _refreshFactory(current.Reader);
                if (replacement is null || ReferenceEquals(replacement, current.Reader))
                {
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    return false;
                }

                var next = new ReaderRef(replacement, this);
                lock (_lock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        next.Retire();
                        return false;
                    }

                    _readers.Add(next);
                    _current = next;
                    current.Retire();
                }

                Interlocked.Increment(ref _refreshes);
                if (background)
                    Interlocked.Exchange(ref _backgroundRefreshes, 1);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                RecordRefreshFailure(ex);
                return false;
            }
        }
    }

    /// <summary>Checks for a replacement asynchronously.</summary>
    public Task<bool> MaybeRefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MaybeRefresh());
    }

    /// <summary>Gets current reader and refresh counters.</summary>
    public ReaderManagerDiagnostics GetDiagnostics()
    {
        lock (_lock)
        {
            int activeReaders = _readers.Count(static reader => !reader.IsRetired);
            int activeLeases = _readers.Sum(static reader => reader.LeaseCount);
            return new ReaderManagerDiagnostics(activeReaders, activeLeases,
                Interlocked.Read(ref _refreshes), Interlocked.Read(ref _refreshFailures),
                Interlocked.Read(ref _disposedReaders));
        }
    }

    /// <summary>Stops refreshes and retires all readers.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _cts.Cancel();
        _refreshLoopExited.Wait(TimeSpan.FromSeconds(30));
        _cts.Dispose();
        lock (_lock)
        {
            foreach (var reader in _readers.ToArray())
                reader.Retire();
        }
        _refreshLoopExited.Dispose();
    }

    private async Task RefreshLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_refreshInterval, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                    break;
                if (!TryRefresh(background: true))
                    continue;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
        finally
        {
            _refreshLoopExited.Set();
        }
    }

    private void RecordRefreshFailure(Exception exception)
    {
        _lastRefreshError = exception;
        Interlocked.Exchange(ref _lastRefreshErrorAtTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _refreshFailures);
        long consecutive = Interlocked.Increment(ref _consecutiveFailures);
        try { RefreshFailed?.Invoke(this, new ReaderRefreshFailedEventArgs(exception, consecutive)); }
        catch (Exception subscriberException)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(subscriberException, "reader-refresh-failed event subscriber");
        }
    }

    private sealed class ReaderRef
    {
        private readonly ReaderManager<TReader> _owner;
        private int _refCount = 1;
        private int _retired;

        internal ReaderRef(TReader reader, ReaderManager<TReader> owner)
        {
            Reader = reader;
            _owner = owner;
        }

        internal TReader Reader { get; }
        internal bool IsRetired => Volatile.Read(ref _retired) != 0;
        internal int LeaseCount => Math.Max(0, Volatile.Read(ref _refCount) - 1);

        internal bool TryAcquire()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _refCount);
                if (current <= 0 || IsRetired)
                    return false;
            }
            while (Interlocked.CompareExchange(ref _refCount, current + 1, current) != current);
            return true;
        }

        internal void ReleaseLease()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
                DisposeReader();
        }

        internal void Retire()
        {
            if (Interlocked.Exchange(ref _retired, 1) == 0)
                ReleaseLease();
        }

        private void DisposeReader()
        {
            Reader.Dispose();
            Interlocked.Increment(ref _owner._disposedReaders);
            lock (_owner._lock)
                _owner._readers.Remove(this);
        }
    }
}
