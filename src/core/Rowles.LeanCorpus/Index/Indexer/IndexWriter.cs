using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
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
    private readonly MMapDirectory _directory;
    private readonly IndexWriterConfig _config;
    private readonly IAnalyser _defaultAnalyser;

    private DocumentBufferState _buffer = new();

    private readonly Dictionary<string, IAnalyser> _analyserCache = new(StringComparer.Ordinal);
    private readonly SpanCountingTokenSink _spanCountingSink = new();
    private readonly SpanPostingTokenSink _spanPostingSink;

    private readonly CommitState _commitState = new();
    private readonly BackpressureState _backpressureState = new();
    private readonly MergeState _mergeState = new();
    private readonly SnapshotState _snapshotState = new();
    private readonly DwptState _dwptState = new();

    private readonly Lock _writeLock = new();
    private int _disposed;      // 0 = alive, 1 = disposed (atomically set via Interlocked)
    private int _inFlightAdds;  // count of indexing callers that passed the disposed-check gate
    private int _indexingFailed;
    private readonly FileStream _writeLockFile;

    /// <summary>
    /// Initialises a new <see cref="IndexWriter"/> for the given directory with the specified configuration.
    /// Acquires an exclusive write lock on the directory. Only one writer may be open per directory at a time.
    /// </summary>
    /// <param name="directory">The directory where index files will be written.</param>
    /// <param name="config">Writer configuration including analyser, flush thresholds, and deletion policy.</param>
    /// <exception cref="WriteLockException">Thrown if another <see cref="IndexWriter"/> already holds the write lock for this directory.</exception>
    public IndexWriter(MMapDirectory directory, IndexWriterConfig config)
    {
        _directory = directory;
        _config = config;
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
            _writeLockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        catch (IOException)
        {
            throw new WriteLockException(directory.DirectoryPath);
        }

        // Initialize backpressure semaphore if MaxQueuedDocs > 0
        if (config.MaxQueuedDocs > 0)
            _backpressureState.BackpressureSemaphore = new SemaphoreSlim(config.MaxQueuedDocs, config.MaxQueuedDocs);

        // Load existing commit state if present
        CommitManager.LoadLatestCommit(_commitState, _directory, _config);
    }

    /// <summary>
    /// Returns the sequence number that will be assigned to the next indexed document.
    /// Only meaningful when <see cref="IndexWriterConfig.TrackSequenceNumbers"/> is enabled.
    /// </summary>
    public long NextSequenceNumber => Volatile.Read(ref _commitState.NextSequenceNumber);

    /// <summary>
    /// Returns <c>true</c> if a commit has been prepared via <see cref="PrepareCommit"/>
    /// but not yet published via <see cref="Commit"/>.
    /// </summary>
    public bool HasPreparedCommit => _commitState.PreparedGeneration >= 0;

    /// <summary>
    /// Indexes a single document. Validates the document against the schema if one is configured.
    /// May block if <see cref="IndexWriterConfig.MaxQueuedDocs"/> backpressure is enabled.
    /// Automatically flushes a segment when the RAM or document count threshold is reached.
    /// </summary>
    public void AddDocument(LeanDocument doc)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            BackpressureController.AcquireBackpressureSlot(_backpressureState, _writeLock,
                _buffer, _config, _commitState, _directory.DirectoryPath);

            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    if (_backpressureState.BackpressureSemaphore is not null)
                        _backpressureState.SemaphoreSlotsHeld++;

                    if (ShouldThrottleForMerge() && _buffer.DocCount > 0)
                        FlushSegment();

                    enteredCore = true;
                    AddDocumentCore(doc);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();

                if (_backpressureState.BackpressureSemaphore is not null)
                {
                    _backpressureState.BackpressureSemaphore.Release();
                    lock (_writeLock)
                    {
                        _backpressureState.SemaphoreSlotsHeld--;
                    }
                }
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes a batch of documents with a single lock acquisition.
    /// </summary>
    public void AddDocuments(IReadOnlyList<LeanDocument> documents)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;
            ValidateDocuments(documents);
            if (_backpressureState.BackpressureSemaphore is not null && documents.Count > _config.MaxQueuedDocs)
            {
                foreach (var document in documents)
                    AddDocument(document);
                return;
            }

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (_backpressureState.BackpressureSemaphore is not null)
                {
                    for (int i = 0; i < documents.Count; i++)
                    {
                        BackpressureController.AcquireBackpressureSlot(_backpressureState, _writeLock,
                            _buffer, _config, _commitState, _directory.DirectoryPath);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (_backpressureState.BackpressureSemaphore is not null)
                    {
                        _backpressureState.SemaphoreSlotsHeld += acquired;
                        addedToHeldSlots = true;
                    }

                    for (int i = 0; i < documents.Count; i++)
                    {
                        enteredCore = true;
                        AddDocumentCore(documents[i]);
                    }
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                BackpressureController.ReleaseFailedBackpressureSlots(_backpressureState, _writeLock, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes a block of child documents followed by a parent document atomically.
    /// </summary>
    public void AddDocumentBlock(IReadOnlyList<LeanDocument> block)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            ValidateDocuments(block);
            if (_backpressureState.BackpressureSemaphore is not null && block.Count > _config.MaxQueuedDocs)
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({_config.MaxQueuedDocs}).");

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (_backpressureState.BackpressureSemaphore is not null)
                {
                    for (int i = 0; i < block.Count; i++)
                    {
                        BackpressureController.AcquireBackpressureSlot(_backpressureState, _writeLock,
                            _buffer, _config, _commitState, _directory.DirectoryPath);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (_backpressureState.BackpressureSemaphore is not null)
                    {
                        _backpressureState.SemaphoreSlotsHeld += acquired;
                        addedToHeldSlots = true;
                    }

                    for (int i = 0; i < block.Count; i++)
                    {
                        if (i == block.Count - 1)
                        {
                            _buffer.ParentDocIds ??= new HashSet<int>();
                            _buffer.ParentDocIds.Add(_buffer.DocCount);
                        }

                        enteredCore = true;
                        AddDocumentCore(block[i], suppressFlush: true);
                    }

                    if (ShouldFlush())
                        FlushSegment();
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                BackpressureController.ReleaseFailedBackpressureSlots(_backpressureState, _writeLock, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Atomically deletes documents matching the selector and adds the replacement.</summary>
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
                    int preFlushSegmentCount = _commitState.CommittedSegments.Count;

                    DwptManager.FlushDwptPool(_dwptState, _directory, _config, _commitState);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    _commitState.PendingDeletes.Add((field, term, isSoftDelete: false));
                    DeletionApplier.ApplyPendingDeletions(
                        _commitState.PendingDeletes, _commitState.CommittedSegments.GetRange(0, preFlushSegmentCount),
                        _directory, _commitState.CommitGeneration, _config.DurableCommits);
                    enteredCore = true;
                    AddDocumentCore(replacement);
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

    /// <summary>
    /// Soft-deletes documents matching the given term query.
    /// </summary>
    public void SoftDeleteDocuments(TermQuery query)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_config.SoftDeletesEnabled)
            throw new InvalidOperationException(
                "SoftDeletesEnabled must be true in IndexWriterConfig to use SoftDeleteDocuments.");

        lock (_writeLock)
        {
            _commitState.PendingDeletes.Add((query.Field, query.Term, isSoftDelete: true));
            _commitState.ContentChangedSinceCommit = true;
        }
    }

    /// <summary>
    /// Atomically deletes documents matching the given query and adds the replacement.
    /// </summary>
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
                    int preFlushSegmentCount = _commitState.CommittedSegments.Count;

                    DwptManager.FlushDwptPool(_dwptState, _directory, _config, _commitState);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    var terms = ResolveQueryToTerms(query, _commitState.CommittedSegments.GetRange(0, preFlushSegmentCount));
                    foreach (var (f, t) in terms)
                        _commitState.PendingDeletes.Add((f, t, isSoftDelete: false));

                    DeletionApplier.ApplyPendingDeletions(
                        _commitState.PendingDeletes, _commitState.CommittedSegments.GetRange(0, preFlushSegmentCount),
                        _directory, _commitState.CommitGeneration, _config.DurableCommits);
                    enteredCore = true;
                    AddDocumentCore(replacement);
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
                    if (!File.Exists(dicPath)) continue;

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

    /// <summary>
    /// Merges all segments from the given source directory into the current index.
    /// </summary>
    public void AddIndexes(MMapDirectory sourceDirectory)
    {
        EnterIndexingOperation();
        try
        {
            var recovery = IndexRecovery.RecoverLatestCommit(
                sourceDirectory.DirectoryPath,
                cleanupOrphans: false);
            if (recovery is null)
                throw new InvalidDataException(
                    $"No valid commit found in source directory '{sourceDirectory.DirectoryPath}'.");

            IndexOpenGuard.EnsureCanOpenSegments(
                sourceDirectory,
                recovery.SegmentIds,
                _config.CompatibilityMode,
                forWriting: false);

            var sourceSegments = new List<SegmentInfo>();
            foreach (var segId in recovery.SegmentIds)
            {
                var segPath = Path.Combine(sourceDirectory.DirectoryPath, segId + ".seg");
                if (!File.Exists(segPath))
                    throw new InvalidDataException($"Segment file not found: {segPath}");

                var seg = SegmentInfo.ReadFrom(segPath);
                var delPath = seg.DelGeneration.HasValue
                    ? Path.Combine(sourceDirectory.DirectoryPath, $"{segId}_gen_{seg.DelGeneration.Value}.del")
                    : Path.Combine(sourceDirectory.DirectoryPath, segId + ".del");
                if (File.Exists(delPath))
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
                DwptManager.FlushDwptPool(_dwptState, _directory, _config, _commitState);
                if (_buffer.DocCount > 0)
                    FlushSegment();

                var merger = new SegmentMerger(_directory, _config.MergePolicy, _config.PostingsSkipInterval,
                    _config.SoftDeleteRetentionSeconds);
                int localOrdinal = _commitState.NextSegmentOrdinal;
                _commitState.NextSegmentOrdinal += sourceSegments.Count + 8;

                var merged = merger.MergeSegmentsFromDirectory(
                    sourceDirectory, sourceSegments, ref localOrdinal, _config);
                if (merged is not null)
                {
                    _commitState.CommittedSegments.Add(merged);
                    _commitState.ContentChangedSinceCommit = true;
                    _commitState.NextSegmentOrdinal = Math.Max(_commitState.NextSegmentOrdinal, localOrdinal);
                }
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes all buffered documents and pending deletions to disk, writes a new
    /// <c>segments_N</c> commit file, and applies the configured deletion policy.
    /// </summary>
    public void Commit()
    {
        EnterIndexingOperation();
        try
        {
            CommitManager.CommitWithLocks(_commitState, _directory, _config, _buffer,
                _writeLock, _mergeState.MergeIoLock, _mergeState.MergeLock,
                _dwptState, _snapshotState, _mergeState,
                _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes all buffered documents and pending deletions to disk and writes a
    /// <c>segments_N.pending</c> commit file WITHOUT publishing it as the current commit point.
    /// </summary>
    public void PrepareCommit()
    {
        EnterIndexingOperation();
        try
        {
            CommitManager.PrepareCommit(_commitState, _directory, _config, _buffer,
                _writeLock, _mergeState.MergeIoLock, _dwptState,
                _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Discards a prepared commit created by <see cref="PrepareCommit"/>.
    /// </summary>
    public void Rollback()
    {
        EnterIndexingOperation();
        try
        {
            lock (_mergeState.MergeIoLock)
            lock (_writeLock)
            {
                CommitManager.RollbackPrepared(_commitState, _directory.DirectoryPath);
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Force-merges all committed segments into a single segment.
    /// </summary>
    public int Compact()
    {
        EnterIndexingOperation();
        try
        {
            return CommitManager.CompactWithLocks(_commitState, _directory, _config, _buffer,
                _writeLock, _mergeState.MergeIoLock, _dwptState, _snapshotState,
                _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Force-merges segments until the segment count reaches <paramref name="maxSegments"/> or fewer.
    /// </summary>
    public int ForceMerge(int maxSegments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        EnterIndexingOperation();
        try
        {
            return CommitManager.ForceMerge(_commitState, _directory, _config, _buffer,
                _writeLock, _mergeState.MergeIoLock, _snapshotState,
                _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld,
                maxSegments);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Returns all committed and flushed segments for near-real-time search.
    /// </summary>
    public IReadOnlyList<SegmentInfo> GetNrtSegments()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.GetNrtSegments(_snapshotState, _commitState, _buffer, _config,
            _directory.DirectoryPath, _writeLock,
            _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
    }

    /// <summary>
    /// Creates a point-in-time snapshot of the currently committed segments.
    /// </summary>
    public IndexSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.CreateSnapshot(_snapshotState, _commitState, _buffer, _config,
            _directory.DirectoryPath, _writeLock,
            _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
    }

    /// <summary>
    /// Releases a previously held snapshot.
    /// </summary>
    public void ReleaseSnapshot(IndexSnapshot snapshot)
    {
        SnapshotManager.ReleaseSnapshot(_snapshotState, snapshot, _writeLock);
    }

    /// <summary>
    /// Creates a backup manifest for a held snapshot without copying files.
    /// </summary>
    public IndexBackupManifest CreateBackupManifest(IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.CreateBackupManifest(snapshot, _directory.DirectoryPath);
    }

    /// <summary>
    /// Creates a backup for a held snapshot.
    /// </summary>
    public IndexBackupResult BackupSnapshot(IndexSnapshot snapshot, string backupDirectoryPath, IndexBackupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.BackupSnapshot(snapshot, backupDirectoryPath, _directory.DirectoryPath, options);
    }

    /// <summary>
    /// Releases all resources held by this writer, including the directory write lock.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        var spinWait = new SpinWait();
        const long drainTimeoutTicks = 30 * TimeSpan.TicksPerSecond;
        long started = Environment.TickCount64;
        while (Volatile.Read(ref _inFlightAdds) != 0)
        {
            spinWait.SpinOnce();
            if (spinWait.NextSpinWillYield)
            {
                if (Environment.TickCount64 - started > drainTimeoutTicks)
                    break;
                Thread.Sleep(1);
            }
        }

        _mergeState.MergeCts.Cancel();
        try { _mergeState.MergeTask?.Wait(); }
        catch (AggregateException) { }
        catch (ObjectDisposedException) { }
        _mergeState.MergeCts.Dispose();

        _backpressureState.BackpressureSemaphore?.Dispose();

        _writeLockFile.Dispose();
        var lockPath = Path.Combine(_directory.DirectoryPath, "write.lock");
        try { File.Delete(lockPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "write-lock file delete"); }
    }

    private void EnterIndexingOperation()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfIndexingFailed();
        Interlocked.Increment(ref _inFlightAdds);
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Decrement(ref _inFlightAdds);
            throw new ObjectDisposedException(nameof(IndexWriter));
        }
        try
        {
            ThrowIfIndexingFailed();
        }
        catch
        {
            Interlocked.Decrement(ref _inFlightAdds);
            throw;
        }
    }

    private void ExitIndexingOperation()
    {
        Interlocked.Decrement(ref _inFlightAdds);
    }

    private void ThrowIfIndexingFailed()
    {
        if (Volatile.Read(ref _indexingFailed) != 0)
        {
            throw new InvalidOperationException(
                "The writer is unusable because an indexing operation failed after mutating the in-memory buffer. Dispose the writer and reopen from the last commit.");
        }
    }

    private void MarkIndexingFailed()
    {
        Volatile.Write(ref _indexingFailed, 1);
    }

    private void ValidateDocument(LeanDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _config.Schema?.Validate(doc);
    }

    private void ValidateDocuments(IReadOnlyList<LeanDocument> documents)
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

    private bool ShouldThrottleForMerge()
    {
        return _config.MergeThrottleSegments > 0
            && _commitState.CommittedSegments.Count >= _config.MergeThrottleSegments;
    }

    private long ComputeEstimatedRamBytes()
    {
        return _buffer.PostingsRamBytes + _buffer.EstimatedRamBytes;
    }

    // --- Internal static helpers called by extracted manager classes ---

    /// <summary>
    /// Flushes the in-memory buffer to a segment on disk. Called both from IndexWriter
    /// instance methods and from <see cref="CommitManager"/> / <see cref="BackpressureController"/>.
    /// </summary>
    internal static void FlushSegmentStatic(
        DocumentBufferState buffer,
        IndexWriterConfig config,
        string directoryPath,
        CommitState commitState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        if (buffer.DocCount == 0) return;

        int docCountToFlush = buffer.DocCount;

        var segInfo = SegmentFlusher.Flush(
            buffer, config, directoryPath,
            ref commitState.NextSegmentOrdinal, commitState.CommitGeneration,
            commitState.FlushSeqNoStart, commitState.NextSequenceNumber);

        commitState.CommittedSegments.Add(segInfo);
        ResetBufferStatic(buffer, commitState);

        if (backpressureSemaphore is not null && docCountToFlush > 0)
        {
            int toRelease = Math.Min(docCountToFlush, semaphoreSlotsHeld);
            if (toRelease > 0)
            {
                backpressureSemaphore.Release(toRelease);
                semaphoreSlotsHeld -= toRelease;
            }
        }
    }

    internal static void ResetBufferStatic(DocumentBufferState buffer, CommitState commitState)
    {
        buffer.Reset();
        commitState.FlushSeqNoStart = commitState.NextSequenceNumber;
    }

    /// <summary>Instance-level flush — delegates to the static helper.</summary>
    private void FlushSegment()
    {
        FlushSegmentStatic(_buffer, _config, _directory.DirectoryPath, _commitState,
            _backpressureState.BackpressureSemaphore, ref _backpressureState.SemaphoreSlotsHeld);
    }

    /// <summary>
    /// Provides access to <see cref="DwptState"/> for the Concurrent indexing partial.
    /// </summary>
    internal DwptState DwptState => _dwptState;

    /// <summary>
    /// Provides access to <see cref="CommitState"/> for partial classes.
    /// </summary>
    internal CommitState CommitState => _commitState;

    /// <summary>
    /// Provides access to <see cref="BackpressureState"/> for partial classes.
    /// </summary>
    internal BackpressureState BackpressureState => _backpressureState;

    /// <summary>
    /// Provides access to <see cref="MergeState"/> for partial classes.
    /// </summary>
    internal MergeState MergeState => _mergeState;

    /// <summary>
    /// Provides access to <see cref="SnapshotState"/> for partial classes.
    /// </summary>
    internal SnapshotState SnapshotState => _snapshotState;

    /// <summary>
    /// Provides access to <see cref="MMapDirectory"/> for partial classes.
    /// </summary>
    internal MMapDirectory Directory => _directory;

    /// <summary>
    /// Provides access to <see cref="IndexWriterConfig"/> for partial classes.
    /// </summary>
    internal IndexWriterConfig Config => _config;

    /// <summary>
    /// Provides access to <see cref="IAnalyser"/> for partial classes.
    /// </summary>
    internal IAnalyser DefaultAnalyser => _defaultAnalyser;

    /// <summary>
    /// Provides access to the write lock for partial classes.
    /// </summary>
    internal Lock WriteLock => _writeLock;

    /// <summary>
    /// Provides access to the buffer for partial classes.
    /// </summary>
    internal DocumentBufferState Buffer => _buffer;

    /// <summary>
    /// Provides access to the backpressure semaphore for tests and diagnostics.
    /// </summary>
    internal SemaphoreSlim? BackpressureSemaphoreForTests => _backpressureState.BackpressureSemaphore;
}
