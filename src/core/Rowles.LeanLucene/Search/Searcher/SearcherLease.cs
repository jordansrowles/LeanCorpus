namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// A scoped reference to a <see cref="IndexSearcher"/> obtained from a
/// <see cref="SearcherManager"/>. Disposing the lease releases the underlying
/// reference. Prefer this over the legacy <see cref="SearcherManager.Acquire"/>
/// + <see cref="SearcherManager.Release"/> pair: the lease bypasses the
/// <c>ConditionalWeakTable</c> lookup performed by <c>Release</c>.
/// </summary>
public readonly struct SearcherLease : IDisposable
{
    private readonly Action? _release;

    /// <summary>The leased searcher.</summary>
    public IndexSearcher Searcher { get; }

    internal SearcherLease(IndexSearcher searcher, Action release)
    {
        Searcher = searcher;
        _release = release;
    }

    /// <summary>Releases the underlying reference.</summary>
    public void Dispose() => _release?.Invoke();
}
