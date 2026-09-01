using System.Threading.Channels;
using System.Runtime.ExceptionServices;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Accepts documents, analyses text fields, buffers in memory,
/// and flushes immutable segments to disk.
/// Coordinates commit, backpressure, merge, snapshot, deletion, and DWPT subsystems
/// via dedicated manager classes.
/// </summary>
public sealed partial class IndexWriter : IDisposable
{
    private static readonly TimeSpan DefaultDisposeTimeout = TimeSpan.FromSeconds(30);

    private readonly MMapDirectory _directory;
    private readonly IndexWriterConfig _config;
    private readonly TimeSpan _disposeTimeout;
    private readonly IAnalyser _defaultAnalyser;

    private DocumentBufferState _buffer = new();

    private readonly Dictionary<string, IAnalyser> _analyserCache = new(StringComparer.Ordinal);
    private readonly SpanCountingTokenSink _spanCountingSink = new();
    private readonly SpanPostingTokenSink _spanPostingSink;

    // --- Commit state ---
    private long _nextSequenceNumber;
    private long _flushSeqNoStart;
    private int _nextSegmentOrdinal;
    private int _commitGeneration;
    private long _contentToken;
    private bool _contentChangedSinceCommit;
    private int _preparedGeneration = -1;
    private List<SegmentInfo>? _preparedSegments;
    private long _preparedContentToken;
    private long _preparedRollbackContentToken;
    private readonly List<SegmentInfo> _committedSegments = [];
    private readonly PendingDeleteQueue _deleteQueue = new();
    private readonly Dictionary<string, int> _fieldOrdinals = new(StringComparer.Ordinal);

    // --- Backpressure state ---
    private SemaphoreSlim? _backpressureSemaphore;
    private int _flushElection;
    private int _semaphoreSlotsHeld;

    // --- Merge state ---
    private Task? _mergeTask;
    private readonly List<Task> _mergeTasks = [];
    private readonly HashSet<string> _reservedMergeSegments = new(StringComparer.Ordinal);
    private readonly HashSet<string> _obsoleteMergeSegments = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _mergeCts = new();
    private readonly Lock _mergeLock = new();
    private readonly Lock _mergeIoLock = new();

    // --- Snapshot state ---
    private readonly List<IndexSnapshot> _heldSnapshots = [];

    // --- DWPT state ---
    private DocumentsWriterPerThread[]? _dwptPool;
    private int _dwptCounter;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _dwptThreadSlots = new();

    // --- Detached flush state ---
    private readonly List<FlushPendingState> _flushPending = [];
    private int _activeFlushCount;
    private SemaphoreSlim? _flushSemaphore;

    // --- Async write channel ---
    private readonly Channel<AsyncWriteCommand> _asyncWriteChannel;
    private readonly Task _asyncWriteConsumer;

    private readonly Lock _writeLock = new();
    private int _disposed;      // 0 = alive, 1 = disposed (atomically set via Interlocked)
    private int _closing;       // 0 = open, 1 = Dispose has started draining (prevents TOCTOU)
    private int _inFlightAdds;  // count of indexing callers that passed the disposed-check gate
    private int _indexingFailed;
    private Exception? _indexingFailure;
    private readonly Stream _writeLockFile;

    /// <summary>
    /// Initialises a new <see cref="IndexWriter"/> for the given directory with the specified configuration.
    /// Acquires an exclusive write lock on the directory. Only one writer may be open per directory at a time.
    /// </summary>
    /// <param name="directory">The directory where index files will be written.</param>
    /// <param name="config">Writer configuration including analyser, flush thresholds, and deletion policy.</param>
    /// <exception cref="WriteLockException">Thrown if another <see cref="IndexWriter"/> already holds the write lock for this directory.</exception>
    public IndexWriter(MMapDirectory directory, IndexWriterConfig config)
        : this(directory, config, DefaultDisposeTimeout)
    {
    }

    internal IndexWriter(MMapDirectory directory, IndexWriterConfig config, TimeSpan disposeTimeout)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(disposeTimeout, TimeSpan.Zero);
        config.Validate();

        _directory = directory;
        _config = config;
        _disposeTimeout = disposeTimeout;
        _spanPostingSink = new SpanPostingTokenSink(_buffer, _config);
        _buffer.StoreTermVectors = config.StoreTermVectors;

        // If using default StandardAnalyser and config has custom stop words or cache size, rebuild it
        if (config.DefaultAnalyser is StandardAnalyser &&
            (config.StopWords is not null || config.AnalyserInternCacheSize != 4096))
        {
            _defaultAnalyser = new StandardAnalyser(config.AnalyserInternCacheSize, config.StopWords);
        }
        else
        {
            _defaultAnalyser = config.DefaultAnalyser;
        }

        // Acquire exclusive write lock for this directory
        var lockPath = Path.Combine(directory.DirectoryPath, "write.lock");
        try
        {
            _writeLockFile = FileOpenRetry.Open(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        catch (IOException)
        {
            throw new WriteLockException(directory.DirectoryPath);
        }

        // Initialize backpressure semaphore if MaxQueuedDocs > 0
        if (config.MaxQueuedDocs > 0)
            _backpressureSemaphore = new SemaphoreSlim(config.MaxQueuedDocs, config.MaxQueuedDocs);
        if (config.MaxConcurrentFlushes > 1)
            _flushSemaphore = new SemaphoreSlim(config.MaxConcurrentFlushes, config.MaxConcurrentFlushes);

        // Load existing commit state if present
        CommitManager.LoadLatestCommit(this);
        DwptManager.InitialiseDwptPool(this);

        // Start async write consumer
        var channelCapacity = config.MaxQueuedDocs > 0 ? config.MaxQueuedDocs : 4096;
        _asyncWriteChannel = Channel.CreateBounded<AsyncWriteCommand>(new BoundedChannelOptions(channelCapacity)
        {
            SingleWriter = false, SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        _asyncWriteConsumer = Task.Run(RunAsyncWriteLoop);
    }

    public long NextSequenceNumber => Volatile.Read(ref _nextSequenceNumber);

    public bool HasPreparedCommit => _preparedGeneration >= 0;

    /// <summary>Adds one document to the index.</summary>
    /// <remarks>
    /// When <see cref="IndexWriterConfig.TokenBudgetPolicy"/> is
    /// <see cref="Analysis.TokenBudgetPolicy.Reject"/>, a token-budget failure rejects
    /// only this document and does not make the writer unusable.
    /// </remarks>
    public void AddDocument(LeanDocument doc)
        => DwptManager.AddDocument(this, doc);

    /// <summary>Adds documents sequentially in list order.</summary>
    /// <remarks>
    /// This operation is not atomic. If a document is rejected, documents accepted
    /// earlier in the list remain buffered and may be committed.
    /// </remarks>
    public void AddDocuments(IReadOnlyList<LeanDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        for (int i = 0; i < documents.Count; i++)
            DwptManager.AddDocument(this, documents[i]);
    }

    /// <summary>Adds an adjacent child-parent document block.</summary>
    /// <remarks>
    /// Token-budget validation is atomic for the complete block. Document blocks are
    /// not supported when <see cref="IndexWriterConfig.IndexSort"/> is configured,
    /// because physical sorting would break the adjacency required by block joins.
    /// </remarks>
    public void AddDocumentBlock(IReadOnlyList<LeanDocument> block)
        => DwptManager.AddDocumentBlock(this, block);

    public void UpdateDocument(string field, string term, LeanDocument replacement)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(replacement);
            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    DwptManager.WaitForPendingFlushes(this);
                    DwptManager.FlushDwptPool(this);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    QueueDelete(field, term, isSoftDelete: false);
                    // Flushes can materialise documents targeted by earlier deletes.
                    // Apply the complete queue to every committed segment so those
                    // deletes are not cleared before the newly materialised segment
                    // is examined.
                    DeletionApplier.ApplyPendingDeletions(
                        _deleteQueue, _committedSegments,
                        _directory, _commitGeneration, _config.DurableCommits, _config.Metrics);
                    enteredCore = true;
                    DwptManager.AddDocument(this, replacement);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public void SoftDeleteDocuments(TermQuery query)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfIndexingFailed();
        if (!_config.SoftDeletesEnabled)
            throw new InvalidOperationException(
                "SoftDeletesEnabled must be true in IndexWriterConfig to use SoftDeleteDocuments.");

        lock (_writeLock)
        {
            QueueDelete(query.Field, query.Term, isSoftDelete: true);
        }
    }

    public void UpdateDocuments(Query query, LeanDocument replacement)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(replacement);
            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    int preFlushSegmentCount = _committedSegments.Count;

                    DwptManager.WaitForPendingFlushes(this);
                    DwptManager.FlushDwptPool(this);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    var terms = ResolveQueryToTerms(query, _committedSegments.GetRange(0, preFlushSegmentCount));
                    foreach (var (f, t) in terms)
                        QueueDelete(f, t, isSoftDelete: false);

                    // Flushing can materialise documents targeted by earlier deletes.
                    // Apply the complete queue to every committed segment before it is
                    // cleared, including segments materialised by this update.
                    DeletionApplier.ApplyPendingDeletions(
                        _deleteQueue, _committedSegments,
                        _directory, _commitGeneration, _config.DurableCommits, _config.Metrics);
                    enteredCore = true;
                    DwptManager.AddDocument(this, replacement);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    private List<(string field, string term)> ResolveQueryToTerms(Query query, List<SegmentInfo> segments)
    {
        var terms = new List<(string, string)>();
        ResolveQueryToTermsInternal(query, segments, terms);
        return terms;
    }

    private void ResolveQueryToTermsInternal(Query query, List<SegmentInfo> segments, List<(string, string)> terms)
    {
        switch (query)
        {
            case TermQuery tq:
                terms.Add((tq.Field, tq.Term));
                break;

            case BooleanQuery bq:
                foreach (var clause in bq.Clauses)
                {
                    if (clause.Occur == Occur.MustNot)
                        continue;
                    ResolveQueryToTermsInternal(clause.Query, segments, terms);
                }
                break;

            case MatchAllDocsQuery:
                foreach (var seg in segments)
                {
                    var basePath = Path.Combine(_directory.DirectoryPath, seg.SegmentId);
                    var dicPath = basePath + ".dic";
                    if (!FileOpenRetry.FileExists(dicPath)) continue;

                    using var dicReader = Codecs.TermDictionary.TermDictionaryReader.Open(dicPath);
                    foreach (var (qualifiedTerm, _) in dicReader.EnumerateAllTerms())
                    {
                        int sep = qualifiedTerm.IndexOf('\x00');
                        if (sep > 0)
                        {
                            var field = qualifiedTerm[..sep];
                            var term = qualifiedTerm[(sep + 1)..];
                            if (!terms.Contains((field, term)))
                                terms.Add((field, term));
                        }
                    }
                }
                break;

            default:
                throw new NotSupportedException(
                    $"Query type '{query.GetType().Name}' is not supported for update-by-query. Use a TermQuery, BooleanQuery of TermQuery clauses, or MatchAllDocsQuery.");
        }
    }

    public void AddIndexes(MMapDirectory sourceDirectory)
    {
        EnterIndexingOperation();
        try
        {
            var recovery = IndexRecovery.RecoverLatestCommit(
                sourceDirectory.DirectoryPath,
                cleanupOrphans: false,
                catalog: _config.CodecCatalog);
            if (recovery is null)
                throw new InvalidDataException(
                    $"No valid commit found in source directory '{sourceDirectory.DirectoryPath}'.");

            IndexOpenGuard.EnsureCanOpenSegments(
                sourceDirectory,
                recovery.SegmentIds,
                _config.CompatibilityMode,
                forWriting: false,
                _config.CodecCatalog);

            var sourceSegments = new List<SegmentInfo>();
            foreach (var segId in recovery.SegmentIds)
            {
                var segPath = Path.Combine(sourceDirectory.DirectoryPath, segId + ".seg");
                if (!FileOpenRetry.FileExists(segPath))
                    throw new InvalidDataException($"Segment file not found: {segPath}");

                var seg = SegmentInfo.ReadFrom(segPath);
                var delPath = seg.DelGeneration.HasValue
                    ? Path.Combine(sourceDirectory.DirectoryPath, $"{segId}_gen_{seg.DelGeneration.Value}.del")
                    : Path.Combine(sourceDirectory.DirectoryPath, segId + ".del");
                if (FileOpenRetry.FileExists(delPath))
                {
                    var liveDocs = LiveDocs.Deserialise(delPath, seg.DocCount);
                    seg.LiveDocCount = liveDocs.LiveCount;
                    seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
                }
                else
                {
                    seg.LiveDocCount = seg.DocCount;
                }

                sourceSegments.Add(seg);
            }

            lock (_writeLock)
            {
                DwptManager.FlushDwptPool(this);
                if (_buffer.DocCount > 0)
                    FlushSegment();

                var merger = new SegmentMerger(_directory, _config.MergePolicy, _config.PostingsSkipInterval,
                    _config.SoftDeleteRetentionSeconds, _config.HnswBuildConfig,
                    useCompoundFile: _config.UseCompoundFile);
                int localOrdinal = _nextSegmentOrdinal;
                _nextSegmentOrdinal += sourceSegments.Count + 8;

                var merged = merger.MergeSegmentsFromDirectory(
                    sourceDirectory, sourceSegments, ref localOrdinal, _config, _commitGeneration);
                if (merged is not null)
                {
                    _committedSegments.Add(merged);
                    _contentChangedSinceCommit = true;
                    _nextSegmentOrdinal = Math.Max(_nextSegmentOrdinal, localOrdinal);
                }
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public void Commit()
    {
        EnterIndexingOperation();
        try
        {
            CommitManager.CommitWithLocks(this);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public void PrepareCommit()
    {
        EnterIndexingOperation();
        try
        {
            CommitManager.PrepareCommit(this);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public void Rollback()
    {
        EnterIndexingOperation();
        try
        {
            lock (_mergeIoLock)
            lock (_writeLock)
            {
                CommitManager.RollbackPrepared(this);
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public int Compact()
    {
        EnterIndexingOperation();
        try
        {
            return CommitManager.CompactWithLocks(this);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public int ForceMerge(int maxSegments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        EnterIndexingOperation();
        try
        {
            return CommitManager.ForceMerge(this, maxSegments);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public IReadOnlyList<SegmentInfo> GetNrtSegments()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.GetNrtSegments(this);
    }

    public IndexSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.CreateSnapshot(this);
    }

    public void ReleaseSnapshot(IndexSnapshot snapshot)
    {
        SnapshotManager.ReleaseSnapshot(this, snapshot);
    }

    public IndexBackupManifest CreateBackupManifest(IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.CreateBackupManifest(snapshot, _directory.DirectoryPath);
    }

    public IndexBackupResult BackupSnapshot(
        IndexSnapshot snapshot,
        string backupDirectoryPath,
        IndexBackupOptions? options = null)
        => BackupSnapshot(snapshot, backupDirectoryPath, options, CancellationToken.None);

    public IndexBackupResult BackupSnapshot(
        IndexSnapshot snapshot,
        string backupDirectoryPath,
        CancellationToken cancellationToken)
        => BackupSnapshot(snapshot, backupDirectoryPath, null, cancellationToken);

    public IndexBackupResult BackupSnapshot(
        IndexSnapshot snapshot,
        string backupDirectoryPath,
        IndexBackupOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.BackupSnapshot(snapshot, backupDirectoryPath, _directory.DirectoryPath, options, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // Prevent new callers from entering while we drain in-flight operations.
        Volatile.Write(ref _closing, 1);

        Exception? failure = null;
        try
        {
            DrainIndexingOperations(_disposeTimeout);

            // Drain any pending detached flushes and publish their segments.
            lock (_writeLock)
                DwptManager.WaitForPendingFlushes(this);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            DisposeResources(ref failure);
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void DrainIndexingOperations(TimeSpan timeout)
    {
        var spinWait = new SpinWait();
        long drainTimeoutMs = checked((long)timeout.TotalMilliseconds);
        long started = Environment.TickCount64;
        while (Volatile.Read(ref _inFlightAdds) != 0)
        {
            spinWait.SpinOnce();
            if (!spinWait.NextSpinWillYield)
                continue;

            if (Environment.TickCount64 - started >= drainTimeoutMs)
            {
                Diagnostics.LeanCorpusActivitySource.TraceSwallowed(
                    new TimeoutException(
                        $"IndexWriter.Dispose timed out after {timeout.TotalSeconds:0.###} seconds waiting for " +
                        $"{Volatile.Read(ref _inFlightAdds)} in-flight indexing operation(s) to complete."),
                    "dispose-drain-timeout");
                MarkIndexingFailed();
                return;
            }
            Thread.Sleep(1);
        }
    }

    private void DisposeResources(ref Exception? failure)
    {
        CaptureDisposeFailure(ref failure, _mergeCts.Cancel, "dispose-cancel-merges");
        try { _mergeTask?.Wait(); }
        catch (AggregateException) { /* Expected: merge task cancelled during shutdown */ }
        catch (ObjectDisposedException) { /* CTS already disposed */ }
        catch (TaskSchedulerException) { /* Task was rejected by scheduler during shutdown */ }
        CaptureDisposeFailure(ref failure, _mergeCts.Dispose, "dispose-merge-cancellation");

        CaptureDisposeFailure(ref failure, () => { _asyncWriteChannel.Writer.Complete(); }, "dispose-async-writes");
        CaptureDisposeFailure(ref failure, WaitForAsyncWriteConsumer, "dispose-async-write-consumer");

        CaptureDisposeFailure(ref failure, () => _backpressureSemaphore?.Dispose(), "dispose-backpressure");
        CaptureDisposeFailure(ref failure, () => _flushSemaphore?.Dispose(), "dispose-flush-semaphore");
        CaptureDisposeFailure(ref failure, _writeLockFile.Dispose, "dispose-write-lock-handle");

        var lockPath = Path.Combine(_directory.DirectoryPath, "write.lock");
        try { FileOpenRetry.Delete(lockPath); }
        catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "write-lock file delete"); }
    }

    private void WaitForAsyncWriteConsumer()
    {
        if (!_asyncWriteConsumer.Wait(_disposeTimeout))
        {
            throw new TimeoutException(
                $"IndexWriter.Dispose timed out after {_disposeTimeout.TotalSeconds:0.###} seconds " +
                "waiting for the asynchronous write consumer to stop.");
        }
    }

    private static void CaptureDisposeFailure(ref Exception? failure, Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (failure is null)
                failure = ex;
            else
                Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, operation);
        }
    }

    internal void EnterIndexingOperation()
    {
        Interlocked.Increment(ref _inFlightAdds);
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _closing) != 0 || Volatile.Read(ref _indexingFailed) != 0)
        {
            Interlocked.Decrement(ref _inFlightAdds);
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(IndexWriter));
            if (Volatile.Read(ref _closing) != 0)
                throw new ObjectDisposedException(nameof(IndexWriter),
                    "The writer is shutting down. No new indexing operations are accepted.");
            throw CreateIndexingFailureException();
        }
    }

    internal void ExitIndexingOperation()
    {
        Interlocked.Decrement(ref _inFlightAdds);
    }

    internal void MarkIndexingFailed(Exception? failure = null)
    {
        if (failure is not null)
            Interlocked.CompareExchange(ref _indexingFailure, failure, null);
        Volatile.Write(ref _indexingFailed, 1);
    }

    internal void ThrowIfIndexingFailed()
    {
        if (Volatile.Read(ref _indexingFailed) != 0)
            throw CreateIndexingFailureException();
    }

    private InvalidOperationException CreateIndexingFailureException()
        => new(
            "The writer is unusable after an unrecoverable failure. Dispose the writer and reopen from the last commit.",
            Volatile.Read(ref _indexingFailure));

    internal void ValidateDocument(LeanDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _config.Schema?.Validate(doc);
    }

    internal void ValidateDocuments(IReadOnlyList<LeanDocument> documents)
    {
        if (_config.Schema is not { } schema)
        {
            for (int i = 0; i < documents.Count; i++)
                ArgumentNullException.ThrowIfNull(documents[i]);
            return;
        }

        for (int i = 0; i < documents.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(documents[i]);
            schema.Validate(documents[i]);
        }
    }

    private bool ShouldFlush()
    {
        if (_buffer.DocCount >= _config.MaxBufferedDocs)
            return true;
        long ram = ComputeEstimatedRamBytes();
        return ram >= (long)(_config.RamBufferSizeMB * 1024 * 1024);
    }

    internal bool ShouldThrottleForMerge()
    {
        if (_config.MergeThrottleSegments > 0 &&
            _committedSegments.Count >= _config.MergeThrottleSegments)
            return true;

        lock (_mergeLock)
        {
            if (_config.MaxPendingMergeBytes <= 0 || _reservedMergeSegments.Count == 0)
                return false;

            long pendingBytes = 0;
            foreach (var segment in _committedSegments)
            {
                if (_reservedMergeSegments.Contains(segment.SegmentId))
                    pendingBytes += segment.TotalBytes;
            }
            return pendingBytes >= _config.MaxPendingMergeBytes;
        }
    }

    internal void ThrottleMerge()
    {
        MergeScheduler.ScheduleBackgroundMerge(this);
        var task = _mergeTask;
        if (task is not null)
        {
            try { task.Wait(TimeSpan.FromMinutes(2)); }
            catch (AggregateException) { MarkIndexingFailed(); }
        }
    }

    private long ComputeEstimatedRamBytes()
    {
        long bytes = _buffer.AccountedRamBytes;
        _config.Metrics.RecordWriterMemory(bytes, 0, _deleteQueue.Count * 96L);
        return bytes;
    }

    // --- Internal static helpers called by extracted manager classes ---

    internal static void FlushSegmentStatic(IndexWriter writer)
    {
        if (writer._buffer.DocCount == 0) return;

        int docCountToFlush = writer._buffer.DocCount;

        var segInfo = SegmentFlusher.Flush(
            writer._buffer, writer._config, writer._directory.DirectoryPath,
            ref writer._nextSegmentOrdinal, writer._commitGeneration,
            writer._flushSeqNoStart, writer._nextSequenceNumber);

        writer._committedSegments.Add(segInfo);
        ResetBufferStatic(writer);

        if (writer._backpressureSemaphore is not null && docCountToFlush > 0)
        {
            int toRelease = Math.Min(docCountToFlush, writer._semaphoreSlotsHeld);
            if (toRelease > 0)
            {
                BackpressureController.ReleaseSemaphoreSlots(writer, toRelease);
                writer._semaphoreSlotsHeld -= toRelease;
            }
        }
    }

    internal static void ResetBufferStatic(IndexWriter writer)
    {
        writer._buffer.Reset();
        writer._flushSeqNoStart = writer._nextSequenceNumber;
    }

    private void FlushSegment()
    {
        FlushSegmentStatic(this);
    }


    // --- Async write channel types and consumer ---
    private enum AsyncWriteKind { Single, Batch, Block }
    private readonly record struct AsyncWriteCommand(
        object Payload, AsyncWriteKind Kind, TaskCompletionSource Tcs);

    private async Task RunAsyncWriteLoop()
    {
        var reader = _asyncWriteChannel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var cmd))
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    cmd.Tcs.TrySetException(new ObjectDisposedException(nameof(IndexWriter)));
                    continue;
                }
                try
                {
                    ProcessAsyncWriteCommand(cmd);
                    cmd.Tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    if (ex is not ObjectDisposedException and not Analysis.TokenBudgetExceededException)
                        MarkIndexingFailed();
                    cmd.Tcs.TrySetException(ex);
                }
            }
        }
    }

    private void ProcessAsyncWriteCommand(AsyncWriteCommand cmd)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(IndexWriter));
        switch (cmd.Kind)
        {
            case AsyncWriteKind.Single:
                AddDocument((LeanDocument)cmd.Payload);
                break;
            case AsyncWriteKind.Batch:
                AddDocuments((IReadOnlyList<LeanDocument>)cmd.Payload);
                break;
            case AsyncWriteKind.Block:
                AddDocumentBlock((IReadOnlyList<LeanDocument>)cmd.Payload);
                break;
        }
    }
    /// <summary>Gets the latest locally published commit generation.</summary>
    public int CurrentCommitGeneration => Volatile.Read(ref _commitGeneration);

    /// <summary>Gets the content token for the latest locally published commit.</summary>
    public long CurrentContentToken => Volatile.Read(ref _contentToken);

    internal DocumentBufferState Buffer => _buffer;
    internal MMapDirectory Directory => _directory;
    internal IndexWriterConfig Config => _config;
    internal IAnalyser DefaultAnalyser => _defaultAnalyser;
    internal Lock WriteLock => _writeLock;
    internal CancellationTokenSource MergeCts => _mergeCts;
    internal Lock MergeLock => _mergeLock;
    internal Lock MergeIoLock => _mergeIoLock;

    // --- Internal accessors for mutable scalars (managers need ref access) ---
    internal ref long NextSequenceNumberMut => ref _nextSequenceNumber;
    internal ref long FlushSeqNoStart => ref _flushSeqNoStart;
    internal ref int NextSegmentOrdinal => ref _nextSegmentOrdinal;
    internal ref int CommitGeneration => ref _commitGeneration;
    internal ref long ContentToken => ref _contentToken;
    internal ref bool ContentChangedSinceCommit => ref _contentChangedSinceCommit;
    internal ref int PreparedGeneration => ref _preparedGeneration;
    internal ref long PreparedContentToken => ref _preparedContentToken;
    internal ref long PreparedRollbackContentToken => ref _preparedRollbackContentToken;
    internal ref List<SegmentInfo>? PreparedSegments => ref _preparedSegments;
    internal ref int FlushElection => ref _flushElection;
    internal ref int SemaphoreSlotsHeld => ref _semaphoreSlotsHeld;
    internal ref Task? MergeTask => ref _mergeTask;
    internal List<Task> MergeTasks => _mergeTasks;
    internal HashSet<string> ReservedMergeSegments => _reservedMergeSegments;
    internal HashSet<string> ObsoleteMergeSegments => _obsoleteMergeSegments;
    internal ref int DwptCounter => ref _dwptCounter;
    internal System.Collections.Concurrent.ConcurrentDictionary<int, int> DwptThreadSlots => _dwptThreadSlots;

    internal List<SegmentInfo> CommittedSegments => _committedSegments;

    internal List<DeleteTerm> PendingDeletes => _deleteQueue.GetOrderedList();
    internal PendingDeleteQueue DeleteQueue => _deleteQueue;
    /// <summary>Returns or creates a compact field ordinal for the given field name.</summary>
    internal int GetFieldOrdinal(string fieldName)
    {
        if (!_fieldOrdinals.TryGetValue(fieldName, out int ordinal))
        {
            ordinal = _fieldOrdinals.Count;
            _fieldOrdinals[fieldName] = ordinal;
        }
        return ordinal;
    }
    internal Dictionary<string, int> FieldOrdinals => _fieldOrdinals;
    internal List<IndexSnapshot> HeldSnapshots => _heldSnapshots;
    internal DocumentsWriterPerThread[]? DwptPool { get => _dwptPool; set => _dwptPool = value; }
    internal SemaphoreSlim? BackpressureSemaphore => _backpressureSemaphore;
    internal SemaphoreSlim? BackpressureSemaphoreForTests => _backpressureSemaphore;
    internal List<FlushPendingState> FlushPending => _flushPending;
    internal ref int ActiveFlushCount => ref _activeFlushCount;
    internal SemaphoreSlim? FlushSemaphore => _flushSemaphore;
}
