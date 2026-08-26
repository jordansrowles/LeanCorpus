namespace Rowles.LeanCorpus.Search.Searcher;

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

    /// <summary>The committed generation captured when the lease was acquired.</summary>
    public int CommitGeneration { get; }

    internal SearcherLease(IndexSearcher searcher, int commitGeneration, long contentToken, Action release)
    {
        Searcher = searcher;
        CommitGeneration = commitGeneration;
        ContentToken = contentToken;
        _release = release;
    }

    /// <summary>The commit content token captured when the lease was acquired.</summary>
    public long ContentToken { get; }

    /// <summary>Releases the underlying reference.</summary>
    public void Dispose() => _release?.Invoke();
}
