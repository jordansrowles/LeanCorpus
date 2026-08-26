using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns the engine resources for one explicitly registered local index.</summary>
internal sealed class IndexRuntime : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly object _writeLock = new();
    private readonly IndexWriter? _writer;
    private long _pendingOperations;
    private long _nextServerSequence;
    private LocalRuntimeFlags _flags;
    private int _disposed;

    internal IndexRuntime(string path, CompiledIndexSchema schema, TimeSpan commitInterval, TimeSpan refreshInterval, LocalIndexOpenMode mode = LocalIndexOpenMode.ReadWrite, Func<LocalCommitReceipt, ValueTask>? onCommitted = null)
    {
        Schema = schema;
        Mode = mode;
        _directory = new MMapDirectory(path);
        if (mode == LocalIndexOpenMode.ReadWrite)
        {
            _writer = new IndexWriter(_directory, new IndexWriterConfig
            {
                Schema = schema.EngineSchema,
                TrackSequenceNumbers = true,
                FieldAnalysers = schema.Fields
                    .Where(pair => pair.Value.Analyser is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Analyser!, StringComparer.Ordinal)
            });
        }
        Searchers = new SearcherManager(_directory, new SearcherManagerConfig { RefreshInterval = refreshInterval });
        Commits = new LocalCommitCoordinator(this, commitInterval, onCommitted);
    }

    internal IndexWriter Writer => _writer ?? throw new InvalidOperationException("The local index is read-only.");

    internal LocalIndexOpenMode Mode { get; private set; }

    internal LocalCommitCoordinator Commits { get; }

    internal SearcherManager Searchers { get; }

    internal CompiledIndexSchema Schema { get; }

    internal string Path => _directory.DirectoryPath;

    internal object WriteLock => _writeLock;

    internal long MarkWrite()
    {
        Interlocked.Increment(ref _pendingOperations);
        return Interlocked.Increment(ref _nextServerSequence);
    }

    internal long PendingOperations => Volatile.Read(ref _pendingOperations);

    internal long CurrentServerSequence => Volatile.Read(ref _nextServerSequence);

    internal bool IsDegraded => (Interlocked.CompareExchange(ref _flags, LocalRuntimeFlags.None, LocalRuntimeFlags.None) & LocalRuntimeFlags.Degraded) != 0;

    internal void MarkDegraded() => Interlocked.Or(ref _flags, LocalRuntimeFlags.Degraded);

    internal void ClearDegraded() => Interlocked.And(ref _flags, ~LocalRuntimeFlags.Degraded);

    internal bool Commit(bool refresh) => Commits.Commit(refresh) is CommitPublished;

    internal LocalCommitReceipt? CommitCore(bool refresh)
    {
        lock (_writeLock)
        {
            if (Mode != LocalIndexOpenMode.ReadWrite)
                throw new InvalidOperationException("The local index is read-only.");
            if (Volatile.Read(ref _pendingOperations) == 0)
            {
                if (refresh)
                    Searchers.MaybeRefresh();
                return null;
            }
            long last = Volatile.Read(ref _nextServerSequence);
            long first = last - Volatile.Read(ref _pendingOperations) + 1;
            Writer.Commit();
            Interlocked.Exchange(ref _pendingOperations, 0);
            long generation = Writer.CurrentCommitGeneration;
            long contentToken = Writer.CurrentContentToken;
            if (refresh)
                Searchers.MaybeRefresh();
            return new LocalCommitReceipt(first, last, generation, contentToken, true, refresh);
        }
    }

    internal void Refresh() => Searchers.MaybeRefresh();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Interlocked.Or(ref _flags, LocalRuntimeFlags.Draining);
        if (Mode == LocalIndexOpenMode.ReadWrite)
            _ = Commit(refresh: false);
        Commits.Dispose();
        Searchers.Dispose();
        _writer?.Dispose();
        _directory.Dispose();
    }
}
