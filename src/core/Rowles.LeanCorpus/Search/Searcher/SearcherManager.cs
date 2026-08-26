using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Manages the lifecycle of <see cref="IndexSearcher"/> instances through the generic reader manager.
/// </summary>
public sealed class SearcherManager : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly SearcherManagerConfig _config;
    private readonly QueryCache? _queryCache;
    private readonly ConditionalWeakTable<IndexSearcher, SearcherMetadata> _metadata = new();
    private readonly ReaderManager<IndexSearcher> _readerManager;

    /// <summary>The exception thrown by the most recent refresh attempt, or <c>null</c> when none has failed.</summary>
    public Exception? LastRefreshError => _readerManager.LastRefreshError;

    /// <summary>The UTC timestamp at which the most recent refresh exception was recorded.</summary>
    public DateTime? LastRefreshErrorAt => _readerManager.LastRefreshErrorAt;

    /// <summary>The number of consecutive failed refreshes since the last successful refresh.</summary>
    public long ConsecutiveRefreshFailures => _readerManager.ConsecutiveRefreshFailures;

    /// <summary>Raised when a refresh fails.</summary>
    public event EventHandler<RefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// Initialises a searcher manager for the specified directory.
    /// </summary>
    /// <param name="directory">The index directory to manage.</param>
    /// <param name="config">Optional refresh and searcher configuration.</param>
    public SearcherManager(MMapDirectory directory, SearcherManagerConfig? config = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _config = config ?? new SearcherManagerConfig();

        if (_config.SearcherConfig.EnableQueryCache)
        {
            _queryCache = new QueryCache(_config.SearcherConfig.QueryCacheMaxEntries);
            _config.SearcherConfig.SharedCache = _queryCache;
        }

        _readerManager = new ReaderManager<IndexSearcher>(
            OpenInitialSearcher,
            RefreshSearcher,
            _config.RefreshInterval);
        _readerManager.RefreshFailed += OnReaderRefreshFailed;
    }

    /// <summary>Acquires a scoped reference to the current searcher.</summary>
    public SearcherLease AcquireLease()
    {
        var lease = _readerManager.AcquireLease();
        SearcherMetadata metadata = GetMetadata(lease.Reader);
        return new SearcherLease(lease.Reader, metadata.Generation, metadata.ContentToken, lease.Dispose);
    }

    internal bool TryAcquireLease(int generation, out SearcherLease lease)
    {
        if (_readerManager.TryAcquire(reader => GetMetadata(reader).Generation == generation, out var readerLease))
        {
            SearcherMetadata metadata = GetMetadata(readerLease.Reader);
            lease = new SearcherLease(readerLease.Reader, metadata.Generation, metadata.ContentToken, readerLease.Dispose);
            return true;
        }

        lease = default;
        return false;
    }

    internal string DirectoryPath => _directory.DirectoryPath;

    /// <summary>Acquires the current searcher. Call <see cref="Release"/> when finished.</summary>
    public IndexSearcher Acquire() => _readerManager.Acquire();

    /// <summary>Releases a searcher acquired through <see cref="Acquire"/>.</summary>
    public void Release(IndexSearcher searcher) => _readerManager.Release(searcher);

    /// <summary>Runs an action with a leased searcher and releases it afterwards.</summary>
    public T UsingSearcher<T>(Func<IndexSearcher, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var lease = AcquireLease();
        return action(lease.Searcher);
    }

    /// <summary>Synchronously checks for a new commit and publishes a replacement when required.</summary>
    public bool MaybeRefresh()
        => _readerManager.MaybeRefresh() || _readerManager.ConsumeBackgroundRefreshes();

    /// <summary>Async variant of <see cref="MaybeRefresh"/>.</summary>
    public async Task<bool> MaybeRefreshAsync(CancellationToken ct = default)
        => await _readerManager.MaybeRefreshAsync(ct).ConfigureAwait(false)
            || _readerManager.ConsumeBackgroundRefreshes();

    /// <summary>Gets generic lifecycle diagnostics for the managed searchers.</summary>
    public ReaderManagerDiagnostics GetDiagnostics() => _readerManager.GetDiagnostics();

    /// <summary>Stops refreshes and disposes retained searchers after their leases end.</summary>
    public void Dispose() => _readerManager.Dispose();

    private IndexSearcher OpenInitialSearcher()
    {
        IndexOpenGuard.EnsureNoBlockingMigration(_directory, _config.CompatibilityMode);
        var latestCommit = Index.IndexRecovery.RecoverLatestCommit(
            _directory.DirectoryPath,
            cleanupOrphans: false,
            catalog: _config.SearcherConfig.CodecCatalog);
        var searcher = new IndexSearcher(_directory, _config.SearcherConfig);
        _metadata.Add(searcher, new SearcherMetadata(latestCommit?.Generation ?? 0, latestCommit?.ContentToken ?? 0));
        return searcher;
    }

    private IndexSearcher? RefreshSearcher(IndexSearcher current)
    {
        IndexOpenGuard.EnsureNoBlockingMigration(_directory, _config.CompatibilityMode);
        var latestCommit = Index.IndexRecovery.RecoverLatestCommit(
            _directory.DirectoryPath,
            cleanupOrphans: false,
            catalog: _config.SearcherConfig.CodecCatalog);
        if (latestCommit is null)
            return null;

        var currentMetadata = GetMetadata(current);
        if (latestCommit.Generation <= currentMetadata.Generation)
            return null;

        IndexOpenGuard.EnsureCanOpenSegments(
            _directory,
            latestCommit.SegmentIds,
            _config.CompatibilityMode,
            forWriting: false,
            _config.SearcherConfig.CodecCatalog);
        if (latestCommit.ContentToken == currentMetadata.ContentToken)
        {
            currentMetadata.Generation = latestCommit.Generation;
            return null;
        }

        var replacement = new IndexSearcher(_directory, _config.SearcherConfig);
        _metadata.Add(replacement, new SearcherMetadata(latestCommit.Generation, latestCommit.ContentToken));
        _queryCache?.Invalidate();
        return replacement;
    }

    private SearcherMetadata GetMetadata(IndexSearcher searcher)
        => _metadata.TryGetValue(searcher, out var metadata)
            ? metadata
            : throw new InvalidOperationException("The searcher is not owned by this manager.");

    private void OnReaderRefreshFailed(object? sender, ReaderRefreshFailedEventArgs args)
    {
        try { RefreshFailed?.Invoke(this, new RefreshFailedEventArgs(args.Exception, args.ConsecutiveFailures)); }
        catch (Exception subscriberException)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(subscriberException, "refresh-failed event subscriber");
        }
    }

    private sealed class SearcherMetadata(int generation, long contentToken)
    {
        internal int Generation { get; set; } = generation;
        internal long ContentToken { get; } = contentToken;
    }
}
