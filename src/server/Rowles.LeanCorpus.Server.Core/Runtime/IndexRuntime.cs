using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns the engine resources for one explicitly registered local index.</summary>
internal sealed class IndexRuntime : IDisposable, IAsyncDisposable
{
    private readonly PhysicalIndexRuntime _physical;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _writeLock = new();
    private long _pendingOperations;
    private long _nextServerSequence;
    private int _flags;
    private int _disposed;
    private Exception? _lastRefreshFailure;

    internal Action? BeforeRefreshForTesting { get; set; }

    internal IndexRuntime(string path, CompiledIndexSchema schema, TimeSpan commitInterval, TimeSpan refreshInterval, LocalIndexOpenMode mode = LocalIndexOpenMode.ReadWrite, Func<LocalCommitReceipt, ValueTask>? onCommitted = null)
    {
        Schema = schema;
        Mode = mode;
        var writerConfig = mode == LocalIndexOpenMode.ReadWrite ? new IndexWriterConfig
        {
            Schema = schema.EngineSchema,
            TrackSequenceNumbers = true,
            FieldAnalysers = schema.Fields
                .Where(pair => pair.Value.Analyser is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Analyser!, StringComparer.Ordinal)
        } : null;
        _physical = new PhysicalIndexRuntime(path, writerConfig, new() { RefreshInterval = refreshInterval });
        Searchers = new LocalSearcherAccess(_physical);
        try { Commits = new LocalCommitCoordinator(this, commitInterval, onCommitted); }
        catch (Exception openFailure)
        {
            try { _physical.Dispose(); }
            catch (Exception cleanupFailure) { throw new AggregateException(openFailure, cleanupFailure); }
            throw;
        }
    }

    internal IndexWriter Writer => _physical.Writer;
    internal PhysicalIndexRuntimeDiagnostics GetPhysicalDiagnostics() => _physical.GetDiagnostics();

    internal PhysicalIndexRuntime.OperationLease EnterPhysicalOperation() => _physical.EnterOperation(allowFaultedWriter: true);

    internal LocalIndexOpenMode Mode { get; private set; }

    internal LocalCommitCoordinator Commits { get; }

    internal LocalSearcherAccess Searchers { get; }

    internal CompiledIndexSchema Schema { get; }

    internal string Path => _physical.Path;

    internal object WriteLock => _writeLock;

    internal long MarkWrite()
    {
        Interlocked.Increment(ref _pendingOperations);
        return Interlocked.Increment(ref _nextServerSequence);
    }

    internal long PendingOperations => Volatile.Read(ref _pendingOperations);

    internal long CurrentServerSequence => Volatile.Read(ref _nextServerSequence);

    internal bool IsDegraded => Volatile.Read(ref _lastRefreshFailure) is not null
        || (Volatile.Read(ref _flags) & (int)LocalRuntimeFlags.Degraded) != 0
        || _physical.GetDiagnostics().Health != PhysicalIndexRuntimeHealth.Healthy;

    internal bool RequiresReopen => _physical.GetDiagnostics().ReopenRequired;
    internal bool SearchesAllowed => _physical.GetDiagnostics().SearchesAllowed;
    internal Exception? PhysicalFailure => _physical.GetDiagnostics().LastFailure?.Exception;
    internal Exception? LastRefreshFailure => Volatile.Read(ref _lastRefreshFailure);

    internal void MarkDegraded() => Interlocked.Or(ref _flags, (int)LocalRuntimeFlags.Degraded);

    internal void ClearDegraded() => Interlocked.And(ref _flags, ~(int)LocalRuntimeFlags.Degraded);

    internal bool Commit(bool refresh) => Commits.Commit(refresh) is CommitPublished;

    internal LocalCommitReceipt? CommitCore(bool refresh, PhysicalIndexRuntime.OperationLease? admittedOperation = null)
    {
        using PhysicalIndexRuntime.OperationLease? ownedOperation = admittedOperation is null ? _physical.EnterOperation() : null;
        PhysicalIndexRuntime.OperationLease operation = admittedOperation ?? ownedOperation!;
        lock (_writeLock)
        {
            if (Mode != LocalIndexOpenMode.ReadWrite)
                throw new InvalidOperationException("The local index is read-only.");
            if (Volatile.Read(ref _pendingOperations) == 0)
            {
                if (refresh)
                    _physical.RefreshUnderOperation(operation);
                return null;
            }
            long last = Volatile.Read(ref _nextServerSequence);
            long first = last - Volatile.Read(ref _pendingOperations) + 1;
            _physical.CommitUnderOperation(operation, refresh: false);
            Interlocked.Exchange(ref _pendingOperations, 0);
            long generation = Writer.CurrentCommitGeneration;
            long contentToken = Writer.CurrentContentToken;
            bool visible = false;
            if (refresh)
            {
                try
                {
                    BeforeRefreshForTesting?.Invoke();
                    _physical.RefreshUnderOperation(operation);
                    using var lease = _physical.AcquireSearcherUnderOperation(operation);
                    visible = _physical.ConsecutiveRefreshFailures == 0 && lease.ContentToken == contentToken;
                    if (_physical.ConsecutiveRefreshFailures == 0)
                        Volatile.Write(ref _lastRefreshFailure, null);
                }
                catch (Exception failure) when (!IsFatalRefreshFailure(failure))
                {
                    visible = false;
                    Volatile.Write(ref _lastRefreshFailure, failure);
                    LeanCorpusActivitySource.TraceSwallowed(failure, "server post-commit refresh");
                }
            }
            return new LocalCommitReceipt(first, last, generation, contentToken, true, visible);
        }
    }

    internal void Refresh()
    {
        Searchers.Refresh();
        if (Searchers.ConsecutiveRefreshFailures > 0 && Searchers.LastRefreshError is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Volatile.Write(ref _lastRefreshFailure, null);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _ = CloseCoreAsync();
        return new ValueTask(_closed.Task);
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        Interlocked.Or(ref _flags, (int)LocalRuntimeFlags.Draining);
        if (Mode == LocalIndexOpenMode.ReadWrite && PendingOperations > 0 && await Commits.CommitAsync(refresh: false).ConfigureAwait(false) is CommitFailed failed)
            failures.Add(failed.Exception ?? new IOException(failed.Message));
        try { await Commits.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        try { await _physical.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 0) _closed.TrySetResult();
        else _closed.TrySetException(new AggregateException("Server index shutdown failed.", failures));
    }

    private static bool IsFatalRefreshFailure(Exception failure) =>
        failure is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
