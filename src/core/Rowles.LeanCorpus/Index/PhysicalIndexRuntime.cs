using System.Diagnostics;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index;

/// <summary>Composes physical ownership without host, acknowledgement or tenant policy.</summary>
internal sealed class PhysicalIndexRuntime : IDisposable, IAsyncDisposable, ILifetimeLeaseOwner
{
    private readonly object _gate = new();
    private readonly MMapDirectory _directory;
    private readonly IndexWriter? _writer;
    private readonly SearcherManager _searchers;
    private readonly TaskCompletionSource _operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _searchersDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PhysicalIndexRuntimeState _state = PhysicalIndexRuntimeState.Opening;
    private int _activeOperations;
    private int _activeSearcherLeases;
    private bool _directoryOwned = true;
    private Exception? _leaseReleaseFailure;
    private Exception? _closeFailure;
    private DateTimeOffset? _closeStarted;
    private TimeSpan? _closeDuration;
    private PhysicalIndexRuntimeFailure? _lastFailure;
    private bool _commitDegraded;

    internal PhysicalIndexRuntime(string path, IndexWriterConfig? writerConfig, SearcherManagerConfig? searcherConfig = null)
    {
        _directory = new MMapDirectory(path);
        try
        {
            if (writerConfig is not null)
                _writer = new IndexWriter(_directory, writerConfig);
            _searchers = new SearcherManager(_directory, searcherConfig);
            _searchers.RefreshFailed += (_, failure) => RecordFailure("refresh", failure.Error);
            _state = PhysicalIndexRuntimeState.Open;
        }
        catch (Exception openFailure)
        {
            var failures = new List<Exception> { openFailure };
            CaptureCleanupFailure(failures, () => _writer?.Dispose());
            CaptureCleanupFailure(failures, _directory.Dispose);
            if (failures.Count > 1)
                throw new AggregateException("Physical index open and cleanup failed.", failures);
            throw;
        }
    }

    internal Guid Id { get; } = Guid.NewGuid();
    internal string Path => _directory.DirectoryPath;

    // Internal consumers must retain a whole-operation lease around engine access.
    // Ownership of these resources never transfers to a host.
    internal IndexWriter Writer => _writer ?? throw new InvalidOperationException("The physical index is read-only.");

    internal OperationLease EnterOperation(bool allowFaultedWriter = false)
    {
        lock (_gate)
        {
            EnsureOpen();
            if (!allowFaultedWriter) _writer?.ThrowIfIndexingFailed();
            _activeOperations++;
            return new OperationLease(this, new LifetimeLease(this, this));
        }
    }

    internal void CommitUnderOperation(OperationLease operation, bool refresh)
    {
        ValidateOperation(operation);
        Writer.ThrowIfIndexingFailed();
        try
        {
            Writer.Commit();
            lock (_gate) _commitDegraded = false;
        }
        catch (Exception failure)
        {
            lock (_gate) _commitDegraded = true;
            RecordFailure("commit", failure);
            throw;
        }
        if (refresh)
            _ = RefreshUnderOperation(operation);
    }

    internal bool RefreshUnderOperation(OperationLease operation)
    {
        ValidateOperation(operation);
        return _searchers.MaybeRefresh();
    }

    internal PhysicalSearcherLease AcquireSearcherUnderOperation(OperationLease operation)
    {
        ValidateOperation(operation);
        lock (_gate)
        {
            if (_state is not (PhysicalIndexRuntimeState.Open or PhysicalIndexRuntimeState.Quiescing))
                throw new ObjectDisposedException(nameof(PhysicalIndexRuntime));
            var lease = _searchers.AcquireLease();
            _activeSearcherLeases++;
            return new PhysicalSearcherLease(this, lease);
        }
    }

    internal PhysicalSearcherLease AcquireSearcher()
    {
        lock (_gate)
        {
            EnsureOpen();
            var lease = _searchers.AcquireLease();
            _activeSearcherLeases++;
            return new PhysicalSearcherLease(this, lease);
        }
    }

    internal SearcherLease AcquireLease()
    {
        var lease = AcquireSearcher();
        return new SearcherLease(lease.Searcher, lease.CommitGeneration, lease.ContentToken, lease.Dispose);
    }

    internal ReaderManagerDiagnostics GetReaderDiagnostics() => _searchers.GetDiagnostics();
    internal long ConsecutiveRefreshFailures => _searchers.ConsecutiveRefreshFailures;
    internal Exception? LastRefreshError => _searchers.LastRefreshError;

    internal void Commit(bool refresh)
    {
        using var operation = EnterOperation();
        CommitUnderOperation(operation, refresh);
    }

    internal bool Refresh()
    {
        using var operation = EnterOperation();
        return RefreshUnderOperation(operation);
    }

    internal PhysicalIndexRuntimeDiagnostics GetDiagnostics()
    {
        // Never hold admission while taking lower-level resource locks.
        var generations = _searchers.GetLifecycleSnapshot();
        var inputs = _directory.GetOwnedResourceSnapshot();
        var files = _directory.GetFileResourceSnapshot();
        int merges = _writer?.ActiveMergeCount ?? 0;
        lock (_gate)
        {
            bool writerFaulted = _writer?.IsFaulted == true;
            if (writerFaulted && _closeFailure is null && _writer?.IndexingFailure is { } writerFailure
                && !ReferenceEquals(_lastFailure?.Exception, writerFailure))
                _lastFailure = new("writer", writerFailure, DateTimeOffset.UtcNow);
            bool faulted = writerFaulted || _state == PhysicalIndexRuntimeState.Faulted;
            var health = faulted ? PhysicalIndexRuntimeHealth.Faulted
                : _commitDegraded || _searchers.ConsecutiveRefreshFailures > 0 ? PhysicalIndexRuntimeHealth.Degraded
                : PhysicalIndexRuntimeHealth.Healthy;
            return new(Id, _state, _activeOperations, _activeSearcherLeases,
                _writer is not null && !_writer.DisposalCompleted, _directoryOwned,
                _closeFailure ?? _leaseReleaseFailure, _closeStarted, _closeDuration,
                health, _state == PhysicalIndexRuntimeState.Open && _writer is not null && !faulted,
                _state == PhysicalIndexRuntimeState.Open, faulted, _lastFailure,
                generations.Generation, generations.Retired, _writer?.WriteLockOwned == true,
                _state is PhysicalIndexRuntimeState.Quiescing or PhysicalIndexRuntimeState.Closing,
                files.PendingDeletes, files.FileLeases, inputs.Inputs, inputs.MappedViews, merges,
                _writer is null ? PhysicalIndexWriterState.ReadOnly
                    : _writer.DisposalCompleted ? PhysicalIndexWriterState.Closed
                    : _writer.IsClosing ? PhysicalIndexWriterState.Closing : PhysicalIndexWriterState.Open);
        }
    }

    private void RecordFailure(string component, Exception failure)
    {
        lock (_gate) _lastFailure = new(component, failure, DateTimeOffset.UtcNow);
    }

    /// <summary>Closes admission once; cancellation cancels only this caller's wait.</summary>
    internal ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        bool startClose = false;
        lock (_gate)
        {
            if (_state == PhysicalIndexRuntimeState.Open)
            {
                _state = PhysicalIndexRuntimeState.Quiescing;
                _closeStarted = DateTimeOffset.UtcNow;
                if (_activeOperations == 0) _operationsDrained.TrySetResult();
                if (_activeSearcherLeases == 0) _searchersDrained.TrySetResult();
                startClose = true;
            }
        }
        if (startClose)
            // Low-level close includes synchronous drains and filesystem barriers.
            // One worker performs them; callers asynchronously join the shared task.
            _ = Task.Run(CompleteCloseAsync, CancellationToken.None);
        return new ValueTask(cancellationToken.CanBeCanceled
            ? _closed.Task.WaitAsync(cancellationToken)
            : _closed.Task);
    }

    public ValueTask DisposeAsync() => CloseAsync(CancellationToken.None);
    public void Dispose() => CloseAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    void ILifetimeLeaseOwner.ReleaseLease(object token)
    {
        lock (_gate)
        {
            _activeOperations--;
            if (_activeOperations == 0 && _state != PhysicalIndexRuntimeState.Open)
                _operationsDrained.TrySetResult();
        }
    }

    private void ValidateOperation(OperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.EnsureOwner(this);
    }

    private void ReleaseSearcher(SearcherLease lease)
    {
        try { lease.Dispose(); }
        catch (Exception failure)
        {
            lock (_gate) _leaseReleaseFailure ??= failure;
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _activeSearcherLeases--;
                if (_activeSearcherLeases == 0 && _state != PhysicalIndexRuntimeState.Open)
                    _searchersDrained.TrySetResult();
            }
        }
    }

    private async Task CompleteCloseAsync()
    {
        long started = Stopwatch.GetTimestamp();
        using var activity = LeanCorpusActivitySource.Source.StartActivity("leancorpus.index.runtime.close");
        activity?.SetTag("runtime.id", Id);
        try
        {
            await _operationsDrained.Task.ConfigureAwait(false);
            var failures = new List<Exception>();
            // Manager disposal stops executing/background refreshes and retires
            // generations. Previously issued searcher leases remain valid.
            CaptureCleanupFailure(failures, _searchers.Dispose);
            await _searchersDrained.Task.ConfigureAwait(false);
            lock (_gate)
            {
                _state = PhysicalIndexRuntimeState.Closing;
                if (_leaseReleaseFailure is not null) failures.Add(_leaseReleaseFailure);
            }
            CaptureCleanupFailure(failures, () => _writer?.Dispose());
            // A writer that timed out still owns resources and may be executing.
            // Never dispose its directory underneath that unfinished shutdown.
            if (_writer is null || _writer.DisposalCompleted)
            {
                try
                {
                    _directory.Dispose();
                    lock (_gate) _directoryOwned = false;
                    _directory.CompletePendingDeletes();
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("Physical index close failed.", failures);
            lock (_gate)
            {
                _state = PhysicalIndexRuntimeState.Closed;
                _closeDuration = Stopwatch.GetElapsedTime(started);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            _closed.TrySetResult();
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                _state = PhysicalIndexRuntimeState.Faulted;
                _closeFailure = failure;
                _lastFailure = new("close", failure, DateTimeOffset.UtcNow);
                _closeDuration = Stopwatch.GetElapsedTime(started);
            }
            activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
            _closed.TrySetException(failure);
        }
        finally
        {
            activity?.SetTag("close.duration_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_state != PhysicalIndexRuntimeState.Open, this);

    private static void CaptureCleanupFailure(List<Exception> failures, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception failure) { failures.Add(failure); }
    }

    internal sealed class PhysicalSearcherLease(PhysicalIndexRuntime owner, SearcherLease lease) : IDisposable
    {
        private PhysicalIndexRuntime? _owner = owner;
        internal IndexSearcher Searcher => lease.Searcher;
        internal int CommitGeneration => lease.CommitGeneration;
        internal long ContentToken => lease.ContentToken;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseSearcher(lease);
    }

    internal sealed class OperationLease : IDisposable
    {
        private readonly PhysicalIndexRuntime _owner;
        private readonly LifetimeLease _lifetime;
        private int _disposed;

        internal OperationLease(PhysicalIndexRuntime owner, LifetimeLease lifetime)
        {
            _owner = owner;
            _lifetime = lifetime;
        }

        internal void EnsureOwner(PhysicalIndexRuntime owner)
        {
            if (!ReferenceEquals(_owner, owner))
                throw new InvalidOperationException("The physical operation lease belongs to a different runtime.");
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _lifetime.Dispose();
        }
    }
}
