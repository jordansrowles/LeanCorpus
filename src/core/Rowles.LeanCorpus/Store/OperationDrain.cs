using System.Runtime.ExceptionServices;

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
    private DisposalCompletionState _disposalCompletion;
    private ExceptionDispatchInfo? _disposalFailure;
    internal int ActiveCount => Volatile.Read(ref _state) & int.MaxValue;

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
    internal bool BeginDisposeAndWaitForCompletion()
    {
        RequestDispose();
        bool waited = false;

        lock (_waitLock)
        {
            while (Volatile.Read(ref _state) != DisposeRequested)
            {
                waited = true;
                Monitor.Wait(_waitLock);
            }

            while (_disposalCompletion == DisposalCompletionState.Running)
            {
                waited = true;
                Monitor.Wait(_waitLock);
            }

            if (_disposalCompletion == DisposalCompletionState.Succeeded)
                return false;

            if (_disposalCompletion == DisposalCompletionState.Faulted && waited)
            {
                _disposalFailure!.Throw();
                return false;
            }

            _disposalFailure = null;
            _disposalCompletion = DisposalCompletionState.Running;
            return true;
        }
    }

    internal void CompleteDisposalSuccessfully()
    {
        lock (_waitLock)
        {
            _disposalFailure = null;
            _disposalCompletion = DisposalCompletionState.Succeeded;
            Monitor.PulseAll(_waitLock);
        }
    }

    internal void CompleteDisposalWithFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ExceptionDispatchInfo failure = ExceptionDispatchInfo.Capture(exception);
        lock (_waitLock)
        {
            _disposalFailure = failure;
            _disposalCompletion = DisposalCompletionState.Faulted;
            Monitor.PulseAll(_waitLock);
        }
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

    private enum DisposalCompletionState : byte
    {
        NotStarted,
        Running,
        Succeeded,
        Faulted
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
