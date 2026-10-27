using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Prevents resource reclamation while operations are active. New operations are
/// rejected once disposal begins and the disposing thread waits for existing work.
/// </summary>
internal sealed class OperationDrain : ILifetimeLeaseOwner
{
    private const int DisposeRequested = int.MinValue;
    private static readonly object s_leaseToken = new();
    private static readonly object s_measuredLeaseToken = new();

    private readonly object _waitLock = new();
    private int _state;
    private DisposalAttempt? _currentDisposalAttempt;
    private bool _disposalSucceeded;
    internal int ActiveCount => Volatile.Read(ref _state) & int.MaxValue;

    // Lets lifecycle tests hold a joiner after it has captured its attempt. The
    // callback runs outside _waitLock and is null in normal operation.
    internal Action? BeforeDisposalAttemptWaitTestHook { get; set; }

    internal Scope Enter(object owner)
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state < 0, owner);
            if (state == int.MaxValue)
                throw new InvalidOperationException("Too many concurrent operations are active.");

            if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
                return new Scope(this);
        }
    }

    internal LifetimeLease Acquire(object owner)
    {
        EnterCore(owner);
        bool measured = Diagnostics.FileSystemDiagnostics.RecordLifetimeLeaseAcquired();
        return new LifetimeLease(this, measured ? s_measuredLeaseToken : s_leaseToken);
    }

    private void EnterCore(object owner)
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);
            ObjectDisposedException.ThrowIf(state < 0, owner);
            if (state == int.MaxValue)
                throw new InvalidOperationException("Too many concurrent operations are active.");

            if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
                return;
        }
    }

    internal void BeginDisposeAndWait()
    {
        _ = BeginDisposeAndWait(Timeout.InfiniteTimeSpan);
    }

    internal bool BeginDisposeAndWait(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, Timeout.InfiniteTimeSpan);
        RequestDispose();

        return WaitForActiveOperations(timeout);
    }

    /// <summary>
    /// Closes admission, drains active operations and grants exactly one caller the
    /// cleanup work. Calls that overlap a failed cleanup receive its same failure;
    /// a later call may retry the unfinished cleanup.
    /// </summary>
    internal DisposalAttempt? BeginDisposeAndWaitForCompletion()
    {
        RequestDispose();
        bool waited = false;
        DisposalAttempt? joinedAttempt;

        lock (_waitLock)
        {
            while (Volatile.Read(ref _state) != DisposeRequested)
            {
                waited = true;
                Monitor.Wait(_waitLock);
            }

            if (_disposalSucceeded)
                return null;

            if (_currentDisposalAttempt is { IsCompleted: false } currentAttempt)
            {
                joinedAttempt = currentAttempt;
            }
            else if (waited && _currentDisposalAttempt is { IsCompleted: true } completedAttempt)
            {
                joinedAttempt = completedAttempt;
            }
            else
            {
                var attempt = new DisposalAttempt();
                _currentDisposalAttempt = attempt;
                return attempt;
            }
        }

        BeforeDisposalAttemptWaitTestHook?.Invoke();
        joinedAttempt.WaitForCompletion();
        return null;
    }

    internal void CompleteDisposalSuccessfully(DisposalAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        lock (_waitLock)
        {
            EnsureCurrentAttempt(attempt);
            attempt.CompleteSuccessfully();
            _disposalSucceeded = true;
            Monitor.PulseAll(_waitLock);
        }
    }

    internal void CompleteDisposalWithFailure(DisposalAttempt attempt, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(exception);
        ExceptionDispatchInfo failure = ExceptionDispatchInfo.Capture(exception);
        lock (_waitLock)
        {
            EnsureCurrentAttempt(attempt);
            attempt.CompleteWithFailure(failure);
            Monitor.PulseAll(_waitLock);
        }
    }

    private void EnsureCurrentAttempt(DisposalAttempt attempt)
    {
        if (!ReferenceEquals(_currentDisposalAttempt, attempt) || attempt.IsCompleted)
            throw new InvalidOperationException("Only the owner of the active disposal attempt can complete it.");
    }

    private void RequestDispose()
    {
        while (true)
        {
            int state = Volatile.Read(ref _state);
            if (state < 0)
                break;
            if (Interlocked.CompareExchange(ref _state, state | DisposeRequested, state) == state)
                break;
        }
    }

    private bool WaitForActiveOperations(TimeSpan timeout)
    {
        if (Volatile.Read(ref _state) == DisposeRequested)
            return true;

        lock (_waitLock)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                while (Volatile.Read(ref _state) != DisposeRequested)
                    Monitor.Wait(_waitLock);
                return true;
            }

            long deadline = Environment.TickCount64 + checked((long)Math.Ceiling(timeout.TotalMilliseconds));
            while (Volatile.Read(ref _state) != DisposeRequested)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0 || !Monitor.Wait(_waitLock, TimeSpan.FromMilliseconds(remaining)))
                    return Volatile.Read(ref _state) == DisposeRequested;
            }
            return true;
        }
    }

    private void Exit()
    {
        int state = Interlocked.Decrement(ref _state);
        if (state != DisposeRequested)
            return;

        lock (_waitLock)
            Monitor.PulseAll(_waitLock);
    }

    internal void ExitOperation() => Exit();

    void ILifetimeLeaseOwner.ReleaseLease(object token)
    {
        if (ReferenceEquals(token, s_measuredLeaseToken))
            Diagnostics.FileSystemDiagnostics.RecordLifetimeLeaseReleased();
        Exit();
    }

    internal struct Scope : IDisposable
    {
        private OperationDrain? _owner;

        internal Scope(OperationDrain owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }

    internal sealed class DisposalAttempt
    {
        private readonly TaskCompletionSource<ExceptionDispatchInfo?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool IsCompleted => _completion.Task.IsCompleted;

        internal void WaitForCompletion()
        {
            ExceptionDispatchInfo? failure = _completion.Task.GetAwaiter().GetResult();
            failure?.Throw();
        }

        internal void CompleteSuccessfully()
        {
            if (!_completion.TrySetResult(null))
                throw new InvalidOperationException("The disposal attempt has already completed.");
        }

        internal void CompleteWithFailure(ExceptionDispatchInfo failure)
        {
            if (!_completion.TrySetResult(failure))
                throw new InvalidOperationException("The disposal attempt has already completed.");
        }
    }
}

/// <summary>Retains an input mapping while a longer-lived object can access it.</summary>
internal struct IndexInputLifetimeLease : IDisposable
{
    private IndexInput? _input;
    private LifetimeLease _inputLease;

    internal IndexInputLifetimeLease(IndexInput input, LifetimeLease inputLease)
    {
        _input = input;
        _inputLease = inputLease;
    }

    public void Dispose()
    {
        _inputLease.Dispose();
        GC.KeepAlive(_input);
        _input = null;
    }
}
