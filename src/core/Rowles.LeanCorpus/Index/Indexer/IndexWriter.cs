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
    private readonly List<SegmentInfo> _committedSegments = [];
    private readonly List<(string field, string term, bool isSoftDelete)> _pendingDeletes = [];
    private DateTime _lastCommitFsyncUtc = DateTime.MinValue;

    // --- Backpressure state ---
    private SemaphoreSlim? _backpressureSemaphore;
    private int _flushElection;
    private int _semaphoreSlotsHeld;

    // --- Merge state ---
    private Task? _mergeTask;
    private readonly CancellationTokenSource _mergeCts = new();
    private readonly Lock _mergeLock = new();
    private readonly Lock _mergeIoLock = new();

    // --- Snapshot state ---
    private readonly List<IndexSnapshot> _heldSnapshots = [];

    // --- DWPT state ---
    private DocumentsWriterPerThread[]? _dwptPool;
    private int _dwptCounter;

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
            _backpressureSemaphore = new SemaphoreSlim(config.MaxQueuedDocs, config.MaxQueuedDocs);

        // Load existing commit state if present
        CommitManager.LoadLatestCommit(this);
    }

    public long NextSequenceNumber => Volatile.Read(ref _nextSequenceNumber);
    public bool HasPreparedCommit => _preparedGeneration >= 0;

    public void AddDocument(LeanDocument doc)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            BackpressureController.AcquireBackpressureSlot(this);

            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    if (_backpressureSemaphore is not null)
                        _semaphoreSlotsHeld++;

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

                if (_backpressureSemaphore is not null)
                {
                    _backpressureSemaphore.Release();
                    lock (_writeLock)
                    {
                        _semaphoreSlotsHeld--;
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

    public void AddDocuments(IReadOnlyList<LeanDocument> documents)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;
            ValidateDocuments(documents);
            if (_backpressureSemaphore is not null && documents.Count > _config.MaxQueuedDocs)
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
                if (_backpressureSemaphore is not null)
                {
                    for (int i = 0; i < documents.Count; i++)
                    {
                        BackpressureController.AcquireBackpressureSlot(this);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (_backpressureSemaphore is not null)
                    {
                        _semaphoreSlotsHeld += acquired;
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
                BackpressureController.ReleaseFailedBackpressureSlots(this, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public void AddDocumentBlock(IReadOnlyList<LeanDocument> block)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            ValidateDocuments(block);
            if (_backpressureSemaphore is not null && block.Count > _config.MaxQueuedDocs)
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({_config.MaxQueuedDocs}).");

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (_backpressureSemaphore is not null)
                {
                    for (int i = 0; i < block.Count; i++)
                    {
                        BackpressureController.AcquireBackpressureSlot(this);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (_backpressureSemaphore is not null)
                    {
                        _semaphoreSlotsHeld += acquired;
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
                BackpressureController.ReleaseFailedBackpressureSlots(this, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

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
                    int preFlushSegmentCount = _committedSegments.Count;

                    DwptManager.FlushDwptPool(this);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    _pendingDeletes.Add((field, term, isSoftDelete: false));
                    DeletionApplier.ApplyPendingDeletions(
                        _pendingDeletes, _committedSegments.GetRange(0, preFlushSegmentCount),
                        _directory, _commitGeneration, _config.DurableCommits);
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

    public void SoftDeleteDocuments(TermQuery query)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_config.SoftDeletesEnabled)
            throw new InvalidOperationException(
                "SoftDeletesEnabled must be true in IndexWriterConfig to use SoftDeleteDocuments.");

        lock (_writeLock)
        {
            _pendingDeletes.Add((query.Field, query.Term, isSoftDelete: true));
            _contentChangedSinceCommit = true;
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

                    DwptManager.FlushDwptPool(this);
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    var terms = ResolveQueryToTerms(query, _committedSegments.GetRange(0, preFlushSegmentCount));
                    foreach (var (f, t) in terms)
                        _pendingDeletes.Add((f, t, isSoftDelete: false));

                    DeletionApplier.ApplyPendingDeletions(
                        _pendingDeletes, _committedSegments.GetRange(0, preFlushSegmentCount),
                        _directory, _commitGeneration, _config.DurableCommits);
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
                DwptManager.FlushDwptPool(this);
                if (_buffer.DocCount > 0)
                    FlushSegment();

                var merger = new SegmentMerger(_directory, _config.MergePolicy, _config.PostingsSkipInterval,
                    _config.SoftDeleteRetentionSeconds);
                int localOrdinal = _nextSegmentOrdinal;
                _nextSegmentOrdinal += sourceSegments.Count + 8;

                var merged = merger.MergeSegmentsFromDirectory(
                    sourceDirectory, sourceSegments, ref localOrdinal, _config);
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

    public IndexBackupResult BackupSnapshot(IndexSnapshot snapshot, string backupDirectoryPath, IndexBackupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return SnapshotManager.BackupSnapshot(snapshot, backupDirectoryPath, _directory.DirectoryPath, options);
    }

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

        _mergeCts.Cancel();
        try { _mergeTask?.Wait(); }
        catch (AggregateException) { }
        catch (ObjectDisposedException) { }
        _mergeCts.Dispose();

        _backpressureSemaphore?.Dispose();

        _writeLockFile.Dispose();
        var lockPath = Path.Combine(_directory.DirectoryPath, "write.lock");
        try { File.Delete(lockPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "write-lock file delete"); }
    }

    internal void EnterIndexingOperation()
    {
        Interlocked.Increment(ref _inFlightAdds);
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _indexingFailed) != 0)
        {
            Interlocked.Decrement(ref _inFlightAdds);
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(IndexWriter));
            throw new InvalidOperationException(
                "The writer is unusable because an indexing operation failed after mutating the in-memory buffer. Dispose the writer and reopen from the last commit.");
        }
    }

    internal void ExitIndexingOperation()
    {
        Interlocked.Decrement(ref _inFlightAdds);
    }

    internal void MarkIndexingFailed()
    {
        Volatile.Write(ref _indexingFailed, 1);
    }

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

    private bool ShouldThrottleForMerge()
    {
        return _config.MergeThrottleSegments > 0
            && _committedSegments.Count >= _config.MergeThrottleSegments;
    }

    private long ComputeEstimatedRamBytes()
    {
        return _buffer.PostingsRamBytes + _buffer.EstimatedRamBytes;
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
                writer._backpressureSemaphore.Release(toRelease);
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

    // --- Internal accessors for partial classes and manager classes ---
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
    internal ref List<SegmentInfo>? PreparedSegments => ref _preparedSegments;
    internal ref DateTime LastCommitFsyncUtc => ref _lastCommitFsyncUtc;
    internal ref int FlushElection => ref _flushElection;
    internal ref int SemaphoreSlotsHeld => ref _semaphoreSlotsHeld;
    internal ref Task? MergeTask => ref _mergeTask;
    internal ref int DwptCounter => ref _dwptCounter;

    internal List<SegmentInfo> CommittedSegments => _committedSegments;
    internal List<(string field, string term, bool isSoftDelete)> PendingDeletes => _pendingDeletes;
    internal List<IndexSnapshot> HeldSnapshots => _heldSnapshots;
    internal DocumentsWriterPerThread[]? DwptPool { get => _dwptPool; set => _dwptPool = value; }
    internal SemaphoreSlim? BackpressureSemaphore => _backpressureSemaphore;
    internal SemaphoreSlim? BackpressureSemaphoreForTests => _backpressureSemaphore;
}
