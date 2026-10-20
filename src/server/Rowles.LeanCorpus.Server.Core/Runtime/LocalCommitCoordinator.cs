using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Coordinates periodic, threshold and explicit local commit publication.</summary>
public sealed class LocalCommitCoordinator : ILocalCommitCoordinator, IDisposable, IAsyncDisposable
{
    private readonly IndexRuntime _runtime;
    private readonly Timer _timer;
    private readonly Func<LocalCommitReceipt, ValueTask>? _onCommitted;
    private readonly object _sync = new();
    private readonly List<(long Sequence, TaskCompletionSource<LocalCommitReceipt> Completion)> _waiters = [];
    private int _disposed;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LocalCommitReceipt? _lastReceipt;
    private Exception? _lastFailure;
    private Exception? _lastObserverFailure;
    private int _consecutiveFailures;
    private int _activeCommitAttempt;
    private long _lastSuccessfulCommitUtcTicks;

    // A deterministic interleaving seam for the commit publication regression.
    internal Action<LocalCommitReceipt>? BeforeReceiptPublicationForTesting { get; set; }

    internal LocalCommitCoordinator(IndexRuntime runtime, TimeSpan interval, Func<LocalCommitReceipt, ValueTask>? onCommitted = null)
    {
        _runtime = runtime;
        _onCommitted = onCommitted;
        _timer = new Timer(static state => ((LocalCommitCoordinator)state!).CommitPending(), this, interval, interval);
    }

    /// <inheritdoc />
    public LocalCommitState State => new(_runtime.PendingOperations, LastReceipt, LastFailure?.Message, ConsecutiveFailures);

    internal LocalCommitReceipt? LastReceipt => Volatile.Read(ref _lastReceipt);
    internal Exception? LastFailure => Volatile.Read(ref _lastFailure);
    internal Exception? LastObserverFailure => Volatile.Read(ref _lastObserverFailure);
    internal int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    internal LocalCommitCoordinatorDiagnostics GetDiagnostics()
    {
        LocalCommitReceipt? receipt = LastReceipt;
        int waiters;
        lock (_sync) waiters = _waiters.Count;
        return new(receipt?.LastSequenceNumber, receipt?.CommitGeneration, receipt?.ContentToken, receipt?.IsVisible,
            waiters, Volatile.Read(ref _activeCommitAttempt) != 0, Volatile.Read(ref _disposed) != 0,
            LastFailure, ConsecutiveFailures, LastObserverFailure);
    }
    internal DateTimeOffset? LastSuccessfulCommitUtc
    {
        get
        {
            long ticks = Volatile.Read(ref _lastSuccessfulCommitUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public CommitResult Commit(bool refresh = false) => CommitCore(refresh, admittedOperation: null);

    internal CommitResult CommitUnderOperation(bool refresh, PhysicalIndexRuntime.OperationLease operation) => CommitCore(refresh, operation);

    private CommitResult CommitCore(bool refresh, PhysicalIndexRuntime.OperationLease? admittedOperation)
    {
        LocalCommitReceipt? notifyReceipt = null;
        CommitResult result;
        // Linearisation order is WriteLock -> waiter _sync. No physical runtime
        // gate is held while waiting for WriteLock or _sync: request paths create
        // their lease first, while timer paths acquire admission from under
        // WriteLock. Waiter registration holds only _sync. Host callbacks run
        // after WriteLock has been released.
        lock (_runtime.WriteLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return new CommitFailed("The local commit coordinator is closed.", new ObjectDisposedException(nameof(LocalCommitCoordinator)));

            Interlocked.Exchange(ref _activeCommitAttempt, 1);
            try
            {
                LocalCommitReceipt? receipt = _runtime.CommitCore(refresh, admittedOperation);
                if (receipt is null)
                    return new NothingToCommit();

                BeforeReceiptPublicationForTesting?.Invoke(receipt);
                notifyReceipt = PublishReceipt(receipt);
                result = new CommitPublished(notifyReceipt);
            }
            catch (Exception exception)
            {
                RecordCommitFailure(exception);
                result = new CommitFailed("The local commit failed.", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _activeCommitAttempt, 0);
            }
        }

        if (notifyReceipt is not null)
            NotifyObserver(notifyReceipt);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<CommitResult> CommitAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Commit(refresh));
    }

    /// <inheritdoc />
    public async ValueTask<LocalCommitReceipt> WaitUntilCommittedAsync(long sequenceNumber, CancellationToken cancellationToken = default)
    {
        LocalCommitReceipt? current = LastReceipt;
        if (current is not null && current.LastSequenceNumber >= sequenceNumber)
            return current;

        TaskCompletionSource<LocalCommitReceipt> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            current = LastReceipt;
            if (current is not null && current.LastSequenceNumber >= sequenceNumber)
                return current;
            if (_runtime.RequiresReopen)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                    _runtime.PhysicalFailure ?? LastFailure ?? new IOException("The physical runtime requires reopening.")).Throw();
            _waiters.Add((sequenceNumber, completion));
        }
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            var waiter = ((LocalCommitCoordinator Coordinator, TaskCompletionSource<LocalCommitReceipt> Completion))state!;
            waiter.Coordinator.CancelWaiter(waiter.Completion);
        }, (this, completion));
        return await completion.Task.ConfigureAwait(false);
    }

    private void CommitPending()
    {
        if (Volatile.Read(ref _disposed) != 0 || _runtime.Mode != LocalIndexOpenMode.ReadWrite || _runtime.PendingOperations == 0)
            return;
        _ = Commit(refresh: true);
    }

    private LocalCommitReceipt PublishReceipt(LocalCommitReceipt receipt)
    {
        List<TaskCompletionSource<LocalCommitReceipt>> completed = [];
        lock (_sync)
        {
            LocalCommitReceipt? current = LastReceipt;
            if (current is not null && receipt.LastSequenceNumber < current.LastSequenceNumber)
                throw new InvalidOperationException("A local commit receipt cannot regress its published sequence.");
            if (current is not null && receipt.LastSequenceNumber == current.LastSequenceNumber)
            {
                if (receipt.CommitGeneration != current.CommitGeneration || receipt.ContentToken != current.ContentToken)
                    throw new InvalidOperationException("Equal-sequence local commit receipts must identify the same committed content.");
                receipt = current with { IsVisible = current.IsVisible || receipt.IsVisible };
            }

            Volatile.Write(ref _lastReceipt, receipt);
            Volatile.Write(ref _lastFailure, null);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _lastSuccessfulCommitUtcTicks, DateTimeOffset.UtcNow.Ticks);
            _runtime.ClearDegraded();

            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Sequence > receipt.LastSequenceNumber)
                    continue;
                completed.Add(_waiters[i].Completion);
                _waiters.RemoveAt(i);
            }
        }
        foreach (TaskCompletionSource<LocalCommitReceipt> waiter in completed)
            waiter.TrySetResult(receipt);
        return receipt;
    }

    private void RecordCommitFailure(Exception failure)
    {
        List<TaskCompletionSource<LocalCommitReceipt>> failed = [];
        lock (_sync)
        {
            Volatile.Write(ref _lastFailure, failure);
            Interlocked.Increment(ref _consecutiveFailures);
            _runtime.MarkDegraded();
            if (_runtime.RequiresReopen)
            {
                failed.AddRange(_waiters.Select(static waiter => waiter.Completion));
                _waiters.Clear();
            }
        }
        if (_runtime.RequiresReopen)
            foreach (TaskCompletionSource<LocalCommitReceipt> waiter in failed)
                waiter.TrySetException(_runtime.PhysicalFailure ?? failure);
    }

    private void CancelWaiter(TaskCompletionSource<LocalCommitReceipt> completion)
    {
        lock (_sync)
        {
            _waiters.RemoveAll(waiter => ReferenceEquals(waiter.Completion, completion));
        }
        completion.TrySetCanceled();
    }

    private async Task ObserveAsync(ValueTask notification)
    {
        try
        {
            await notification.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordObserverFailure(exception);
        }
    }

    private void RecordObserverFailure(Exception exception)
    {
        Volatile.Write(ref _lastObserverFailure, exception);
        _runtime.MarkDegraded();
    }

    private void NotifyObserver(LocalCommitReceipt receipt)
    {
        if (_onCommitted is null)
            return;
        try
        {
            ValueTask notification = _onCommitted(receipt);
            if (!notification.IsCompletedSuccessfully)
                _ = ObserveAsync(notification);
        }
        catch (Exception exception)
        {
            RecordObserverFailure(exception);
        }
    }

    /// <summary>Stops periodic commit scheduling and releases sequence waiters.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Stops scheduling, drains executing timer callbacks and releases remaining waiters.</summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _ = CloseCoreAsync();
        return new ValueTask(_closed.Task);
    }

    private async Task CloseCoreAsync()
    {
        try
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
            await Task.Run(() =>
            {
                List<TaskCompletionSource<LocalCommitReceipt>> remaining;
                lock (_runtime.WriteLock)
                {
                    lock (_sync)
                    {
                        remaining = _waiters.Select(static waiter => waiter.Completion).ToList();
                        _waiters.Clear();
                    }
                }
                foreach (TaskCompletionSource<LocalCommitReceipt> completion in remaining)
                    completion.TrySetException(new ObjectDisposedException(nameof(LocalCommitCoordinator)));
            }).ConfigureAwait(false);
            _closed.TrySetResult();
        }
        catch (Exception failure) { _closed.TrySetException(failure); }
    }
}
