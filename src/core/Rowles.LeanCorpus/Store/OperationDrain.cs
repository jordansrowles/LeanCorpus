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
        while (true)
        {
            int state = Volatile.Read(ref _state);
            if (state < 0)
                break;
            if (Interlocked.CompareExchange(ref _state, state | DisposeRequested, state) == state)
                break;
        }

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
