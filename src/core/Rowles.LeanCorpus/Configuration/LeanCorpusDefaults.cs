namespace Rowles.LeanCorpus;

/// <summary>
/// Configures process-wide defaults used when new LeanCorpus configuration objects are created.
/// Changes affect only configurations created after publication; existing configurations and
/// active writers retain their captured values.
/// </summary>
public static class LeanCorpusDefaults
{
    private static readonly object s_updateLock = new();
    [ThreadStatic] private static bool t_updateInProgress;
    private static LeanCorpusDefaultSnapshot s_current = LeanCorpusDefaultSnapshot.BuiltIn;

    /// <summary>
    /// Publishes a complete, immutable snapshot of the supplied defaults. The callback runs
    /// while a process-wide update is serialised, but configured factories are not invoked here.
    /// </summary>
    /// <param name="configure">Configures a private options builder.</param>
    public static void Configure(Action<LeanCorpusDefaultOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        lock (s_updateLock)
        {
            ThrowIfReentrant();
            t_updateInProgress = true;
            try
            {
                LeanCorpusDefaultSnapshot current = Volatile.Read(ref s_current);
                var options = new LeanCorpusDefaultOptions(current);
                configure(options);
                LeanCorpusDefaultSnapshot candidate = options.ToSnapshot();
                ValidateCandidate(candidate);
                Interlocked.Exchange(ref s_current, candidate);
            }
            finally
            {
                t_updateInProgress = false;
            }
        }
    }

    /// <summary>Restores the built-in defaults for subsequently created configurations.</summary>
    public static void Reset()
    {
        lock (s_updateLock)
        {
            ThrowIfReentrant();
            t_updateInProgress = true;
            try
            {
                Interlocked.Exchange(ref s_current, LeanCorpusDefaultSnapshot.BuiltIn);
            }
            finally
            {
                t_updateInProgress = false;
            }
        }
    }

    internal static LeanCorpusDefaultSnapshot GetSnapshot() => Volatile.Read(ref s_current);

    internal static LeanCorpusDefaultSnapshot CaptureSnapshotForTests() => GetSnapshot();

    internal static void RestoreSnapshotForTests(LeanCorpusDefaultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (s_updateLock)
        {
            ThrowIfReentrant();
            t_updateInProgress = true;
            try
            {
                Interlocked.Exchange(ref s_current, snapshot);
            }
            finally
            {
                t_updateInProgress = false;
            }
        }
    }

    private static void ThrowIfReentrant()
    {
        if (t_updateInProgress)
            throw new InvalidOperationException("LeanCorpusDefaults cannot be configured recursively on the same thread.");
    }

    private static void ValidateCandidate(LeanCorpusDefaultSnapshot snapshot)
    {
        var writer = new Index.Indexer.IndexWriterConfig(snapshot, applyFactories: false);
        writer.Validate();

        var searcher = new Search.Searcher.IndexSearcherConfig(snapshot, applyFactories: false);
        searcher.Validate();

        var manager = new Search.Searcher.SearcherManagerConfig(snapshot, applyFactories: false);
        manager.Validate();

        var mapping = new Document.Json.JsonMappingOptions(snapshot);
        mapping.Validate();
        _ = new Search.SearchOptions(snapshot);
        _ = new Search.HnswSearchOptions(snapshot);
    }
}
