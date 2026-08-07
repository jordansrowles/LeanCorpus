namespace Rowles.LeanCorpus.Store;

/// <summary>Owns one release operation without exposing the leased component's layer.</summary>
internal readonly struct LifetimeLease : IDisposable
{
    private readonly ReleaseToken? _token;

    internal LifetimeLease(ILifetimeLeaseOwner owner, object token)
    {
        _token = new ReleaseToken(owner, token);
    }

    public readonly void Dispose() => _token?.Dispose();

    private sealed class ReleaseToken
    {
        private readonly ILifetimeLeaseOwner _owner;
        private readonly object _leaseToken;
        private int _disposed;

        internal ReleaseToken(ILifetimeLeaseOwner owner, object leaseToken)
        {
            _owner = owner;
            _leaseToken = leaseToken;
        }

        internal void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner.ReleaseLease(_leaseToken);
        }
    }
}

internal interface ILifetimeLeaseOwner
{
    void ReleaseLease(object token);
}
