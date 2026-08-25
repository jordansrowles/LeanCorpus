using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Owns the engine resources for one explicitly registered local index.</summary>
internal sealed class IndexRuntime : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly object _writeLock = new();
    private readonly Timer _commitTimer;
    private long _pendingOperations;
    private int _disposed;

    internal IndexRuntime(string path, CompiledIndexSchema schema, TimeSpan commitInterval, TimeSpan refreshInterval)
    {
        Schema = schema;
        _directory = new MMapDirectory(path);
        Writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            Schema = schema.EngineSchema,
            FieldAnalysers = schema.Fields
                .Where(pair => pair.Value.Analyser is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Analyser!, StringComparer.Ordinal)
        });
        Searchers = new SearcherManager(_directory, new SearcherManagerConfig { RefreshInterval = refreshInterval });
        _commitTimer = new Timer(static state => ((IndexRuntime)state!).CommitPending(), this, commitInterval, commitInterval);
    }

    internal IndexWriter Writer { get; }

    internal SearcherManager Searchers { get; }

    internal CompiledIndexSchema Schema { get; }

    internal string Path => _directory.DirectoryPath;

    internal object WriteLock => _writeLock;

    internal long MarkWrite() => Interlocked.Increment(ref _pendingOperations);

    internal long PendingOperations => Volatile.Read(ref _pendingOperations);

    internal bool Commit(bool refresh)
    {
        lock (_writeLock)
        {
            if (Volatile.Read(ref _pendingOperations) == 0)
            {
                if (refresh)
                    Searchers.MaybeRefresh();
                return false;
            }
            Writer.Commit();
            Interlocked.Exchange(ref _pendingOperations, 0);
            if (refresh)
                Searchers.MaybeRefresh();
            return true;
        }
    }

    internal void Refresh() => Searchers.MaybeRefresh();

    private void CommitPending()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        try { Commit(refresh: true); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"LeanCorpus Server background commit failed: {exception}"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _commitTimer.Dispose();
        Commit(refresh: false);
        Searchers.Dispose();
        Writer.Dispose();
        _directory.Dispose();
    }
}
