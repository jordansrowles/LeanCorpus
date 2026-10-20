using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index;
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

    internal IDisposable EnterPhysicalOperation() => _physical.EnterOperation(allowFaultedWriter: true);

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

    internal bool IsDegraded => (Volatile.Read(ref _flags) & (int)LocalRuntimeFlags.Degraded) != 0
        || _physical.GetDiagnostics().Health != PhysicalIndexRuntimeHealth.Healthy;

    internal bool RequiresReopen => _physical.GetDiagnostics().ReopenRequired;
    internal bool SearchesAllowed => _physical.GetDiagnostics().SearchesAllowed;
    internal Exception? PhysicalFailure => _physical.GetDiagnostics().LastFailure?.Exception;

    internal void MarkDegraded() => Interlocked.Or(ref _flags, (int)LocalRuntimeFlags.Degraded);

    internal void ClearDegraded() => Interlocked.And(ref _flags, ~(int)LocalRuntimeFlags.Degraded);

    internal bool Commit(bool refresh) => Commits.Commit(refresh) is CommitPublished;

    internal LocalCommitReceipt? CommitCore(bool refresh)
    {
        using var operation = _physical.EnterOperation();
        lock (_writeLock)
        {
            if (Mode != LocalIndexOpenMode.ReadWrite)
                throw new InvalidOperationException("The local index is read-only.");
            if (Volatile.Read(ref _pendingOperations) == 0)
            {
                if (refresh)
                    Searchers.Refresh();
                return null;
            }
            long last = Volatile.Read(ref _nextServerSequence);
            long first = last - Volatile.Read(ref _pendingOperations) + 1;
            _physical.Commit(refresh: false);
            Interlocked.Exchange(ref _pendingOperations, 0);
            long generation = Writer.CurrentCommitGeneration;
            long contentToken = Writer.CurrentContentToken;
            bool visible = false;
            if (refresh)
            {
                Searchers.Refresh();
                using var lease = Searchers.AcquireLease();
                visible = Searchers.ConsecutiveRefreshFailures == 0 && lease.ContentToken == contentToken;
            }
            return new LocalCommitReceipt(first, last, generation, contentToken, true, visible);
        }
    }

    internal void Refresh()
    {
        Searchers.Refresh();
        if (Searchers.ConsecutiveRefreshFailures > 0 && Searchers.LastRefreshError is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
        if (Mode == LocalIndexOpenMode.ReadWrite && PendingOperations > 0 && Commits.Commit(refresh: false) is CommitFailed failed)
            failures.Add(failed.Exception ?? new IOException(failed.Message));
        try { await Commits.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        try { await _physical.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 0) _closed.TrySetResult();
        else _closed.TrySetException(new AggregateException("Server index shutdown failed.", failures));
    }
}
