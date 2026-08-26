using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Coordinates periodic, threshold and explicit local commit publication.</summary>
public sealed class LocalCommitCoordinator : ILocalCommitCoordinator, IDisposable
{
    private readonly IndexRuntime _runtime;
    private readonly Timer _timer;
    private readonly Func<LocalCommitReceipt, ValueTask>? _onCommitted;
    private readonly object _sync = new();
    private readonly List<(long Sequence, TaskCompletionSource<LocalCommitReceipt> Completion)> _waiters = [];
    private int _disposed;
    private LocalCommitReceipt? _lastReceipt;
    private Exception? _lastFailure;
    private int _consecutiveFailures;

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
    internal int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <inheritdoc />
    public CommitResult Commit(bool refresh = false)
    {
        try
        {
            LocalCommitReceipt? receipt = _runtime.CommitCore(refresh);
            if (receipt is null)
                return new NothingToCommit();

            Volatile.Write(ref _lastReceipt, receipt);
            Volatile.Write(ref _lastFailure, null);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _runtime.ClearDegraded();
            CompleteWaiters(receipt);
            if (_onCommitted is not null)
            {
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
            return new CommitPublished(receipt);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastFailure, exception);
            Interlocked.Increment(ref _consecutiveFailures);
            _runtime.MarkDegraded();
            return new CommitFailed("The local commit failed.", exception);
        }
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
            current = LastReceipt;
            if (current is not null && current.LastSequenceNumber >= sequenceNumber)
                return current;
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

    private void CompleteWaiters(LocalCommitReceipt receipt)
    {
        List<TaskCompletionSource<LocalCommitReceipt>> completed = [];
        lock (_sync)
        {
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
        Volatile.Write(ref _lastFailure, exception);
        Interlocked.Increment(ref _consecutiveFailures);
        _runtime.MarkDegraded();
    }

    /// <summary>Stops periodic commit scheduling and releases sequence waiters.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _timer.Dispose();
        lock (_sync)
        {
            foreach ((_, TaskCompletionSource<LocalCommitReceipt> completion) in _waiters)
                completion.TrySetException(new ObjectDisposedException(nameof(LocalCommitCoordinator)));
            _waiters.Clear();
        }
    }
}
