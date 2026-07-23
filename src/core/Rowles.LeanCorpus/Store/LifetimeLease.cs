namespace Rowles.LeanCorpus.Store;

/// <summary>Owns one release operation without exposing the leased component's layer.</summary>
internal struct LifetimeLease : IDisposable
{
    private ILifetimeLeaseOwner? _owner;
    private readonly object? _token;

    internal LifetimeLease(ILifetimeLeaseOwner owner, object token)
    {
        _owner = owner;
        _token = token;
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null && _token is not null)
            owner.ReleaseLease(_token);
    }
}

internal interface ILifetimeLeaseOwner
{
    void ReleaseLease(object token);
}
