using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Accepts documents, analyses text fields, buffers in memory,
/// and flushes immutable segments to disk.
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

    // Sequence number tracking
    private long _nextSequenceNumber;
    private long _flushSeqNoStart;
    private int _nextSegmentOrdinal;
    private int _commitGeneration;
    private long _contentToken;
    private bool _contentChangedSinceCommit;
    // Two-phase commit state
    private int _preparedGeneration = -1;
    private List<SegmentInfo>? _preparedSegments;
    private long _preparedContentToken;
    private readonly List<SegmentInfo> _committedSegments = [];
    // Pending deletions: field  ->  term  ->  set of matching terms to delete
    private readonly List<(string field, string term, bool isSoftDelete)> _pendingDeletes = [];
    private readonly Lock _writeLock = new();
    private readonly SemaphoreSlim? _backpressureSemaphore;
    private int _flushElection;
    private int _semaphoreSlotsHeld;
    private readonly List<IndexSnapshot> _heldSnapshots = [];
    private int _disposed;      // 0 = alive, 1 = disposed (atomically set via Interlocked)
    private int _inFlightAdds;  // count of indexing callers that passed the disposed-check gate
    private int _indexingFailed;
    private readonly FileStream _writeLockFile;
    // Background merge
    private Task? _mergeTask;
    private readonly CancellationTokenSource _mergeCts = new();
    private readonly Lock _mergeLock = new();
    // Serialises merge IO against operations that mutate per-segment files (ApplyPendingDeletions).
    // Lock ordering invariant: _mergeIoLock is acquired BEFORE _writeLock.
    // This lets long-running merge IO release _writeLock so AddDocument can proceed,
    // while still preventing concurrent .del file mutation by Commit.
    private readonly Lock _mergeIoLock = new();

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
        LoadLatestCommit();
    }

    /// <summary>
    /// Returns the sequence number that will be assigned to the next indexed document.
    /// Only meaningful when <see cref="IndexWriterConfig.TrackSequenceNumbers"/> is enabled.
    /// </summary>
    public long NextSequenceNumber => Volatile.Read(ref _nextSequenceNumber);

    /// <summary>
    /// Indexes a single document. Validates the document against the schema if one is configured.
    /// May block if <see cref="IndexWriterConfig.MaxQueuedDocs"/> backpressure is enabled.
    /// Automatically flushes a segment when the RAM or document count threshold is reached.
    /// </summary>
    /// <param name="doc">The document to index.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    /// <exception cref="SchemaValidationException">Thrown if the document violates the configured schema.</exception>
    public void AddDocument(LeanDocument doc)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            // Apply backpressure if enabled: wait for a semaphore slot before acquiring the write lock.
            // This prevents unbounded memory growth when documents are queued faster than they can be flushed.
            // When the semaphore is exhausted, ONE thread is elected to flush while others
            // simply Wait() for the slot to become available. The election prevents N threads from all
            // re-acquiring _writeLock and sequentially calling FlushSegment when only one flush is needed.
            AcquireBackpressureSlot();

            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    // We hold a semaphore slot at this point. Track it under the
                    // existing write lock so AddDocument does not take a second lock
                    // just for backpressure accounting.
                    if (_backpressureSemaphore is not null)
                        _semaphoreSlotsHeld++;

                    // Merge backpressure: if too many unmerged segments, flush and merge now
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

                // If AddDocumentCore fails, release the semaphore slot immediately
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

    /// <summary>
    /// Indexes a batch of documents with a single lock acquisition.
    /// Faster than calling <see cref="AddDocument"/> in a loop because lock
    /// and backpressure overhead is paid once for the entire batch.
    /// </summary>
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
                        AcquireBackpressureSlot();
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
                ReleaseFailedBackpressureSlots(acquired, addedToHeldSlots);
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
    /// The last document in <paramref name="block"/> is the parent; all preceding
    /// documents are children. Children are stored contiguously before their parent
    /// in the segment, enabling block-join queries.
    /// </summary>
    /// <param name="block">The documents to index as a block. The last element is the parent. Must have at least 2 documents.</param>
    /// <exception cref="ArgumentException">Thrown if the block has fewer than 2 documents.</exception>
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
                        AcquireBackpressureSlot();
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

                    // Suppress threshold flushes until the parent marker is in place so
                    // the block is never split across segments without its parent bit.
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
                ReleaseFailedBackpressureSlots(acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Atomically deletes documents matching the selector and adds the replacement.</summary>
    /// <remarks>
    /// The deletion targets only segments that existed at the time of this call.
    /// Documents buffered before this call but not yet committed are flushed first,
    /// but the delete is applied only to segments that were already committed before
    /// the flush, not to any segment produced by this flush.
    /// </remarks>
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
                    // Capture the count of already-committed segments before any flush.
                    // Deletions must not target the replacement segment that is about to be added.
                    int preFlushSegmentCount = _committedSegments.Count;

                    FlushDwptPool();
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    _pendingDeletes.Add((field, term, isSoftDelete: false));
                    // Restrict deletions to the segments that existed before this call.
                    ApplyPendingDeletions(_committedSegments.GetRange(0, preFlushSegmentCount));
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
    /// Soft-deletes documents matching the given term query by marking them as deleted
    /// in the live-docs bitmap and recording an expiry timestamp. Soft-deleted documents
    /// remain on disk until the retention period elapses, after which merges reclaim them.
    /// The timestamp is recorded as Unix milliseconds in the <c>.del</c> file.
    /// </summary>
    /// <param name="query">The term query identifying documents to soft-delete.</param>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="IndexWriterConfig.SoftDeletesEnabled"/> is <c>false</c>.</exception>
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

    /// <summary>
    /// Atomically deletes documents matching the given query and adds the replacement document.
    /// Supports <see cref="TermQuery"/>, <see cref="BooleanQuery"/> (composed of <see cref="TermQuery"/> clauses),
    /// <see cref="MatchAllDocsQuery"/>, and <see cref="BooleanQuery"/> with a <see cref="MatchAllDocsQuery"/> clause.
    /// Other query types fall back to manual search-then-delete-by-terms.
    /// </summary>
    /// <param name="query">The query identifying documents to delete and replace.</param>
    /// <param name="replacement">The document to add in place of the deleted documents.</param>
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

                    FlushDwptPool();
                    if (_buffer.DocCount > 0)
                        FlushSegment();

                    var terms = ResolveQueryToTerms(query, _committedSegments.GetRange(0, preFlushSegmentCount));
                    foreach (var (field, term) in terms)
                        _pendingDeletes.Add((field, term, isSoftDelete: false));

                    ApplyPendingDeletions(_committedSegments.GetRange(0, preFlushSegmentCount));
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
    /// Resolves a <see cref="Query"/> into a list of (field, term) pairs suitable for deletion.
    /// Supports <see cref="TermQuery"/>, <see cref="BooleanQuery"/> (with <see cref="TermQuery"/> clauses),
    /// and <see cref="MatchAllDocsQuery"/>.
    /// </summary>
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
                // Enumerate all terms from all committed segments
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
    /// Segments are validated for format compatibility, merged into a single new segment
    /// in the target directory, and the commit is updated. Existing source segments are not modified.
    /// </summary>
    /// <param name="sourceDirectory">The directory whose segments will be merged into this index.</param>
    /// <exception cref="InvalidDataException">Thrown if source segment files are missing or incompatible.</exception>
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
                FlushDwptPool();
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

    /// <summary>
    /// Flushes all buffered documents and pending deletions to disk, writes a new
    /// <c>segments_N</c> commit file, and applies the configured deletion policy.
    /// Schedules a background merge after the flush.
    /// </summary>
    /// <remarks>
    /// If <see cref="PrepareCommit"/> was called previously, this publishes the
    /// prepared commit by atomically renaming the <c>.pending</c> file. Otherwise
    /// it performs a full flush-and-commit cycle.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public void Commit()
    {
        EnterIndexingOperation();
        try
        {
            CommitWithLocks();
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes all buffered documents and pending deletions to disk and writes a
    /// <c>segments_N.pending</c> commit file WITHOUT publishing it as the current
    /// commit point. The prepared commit is not visible to readers until
    /// <see cref="Commit"/> is called.
    /// </summary>
    /// <remarks>
    /// <para>Call <see cref="Commit"/> to publish the prepared commit, or
    /// <see cref="Rollback"/> to discard it. The <c>.pending</c> file is durable
    /// — if the process crashes after <c>PrepareCommit</c> but before
    /// <c>Commit</c>, recovery will promote it on next open.</para>
    /// <para>Only one prepared commit may be outstanding at a time. Calling
    /// <c>PrepareCommit</c> again overwrites the previous prepared state.</para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public void PrepareCommit()
    {
        EnterIndexingOperation();
        try
        {
            lock (_mergeIoLock)
            lock (_writeLock)
            {
                // Apply pending deletions, flush DWPT pool, flush buffered docs.
                var preFlushSegmentCount = _committedSegments.Count;
                if (preFlushSegmentCount > 0 && _pendingDeletes.Count > 0)
                    ApplyPendingDeletions(_committedSegments.GetRange(0, preFlushSegmentCount));

                FlushDwptPool();
                if (_buffer.DocCount > 0)
                    FlushSegment();

                if (_pendingDeletes.Count > 0)
                    ApplyPendingDeletions(_committedSegments);

                if (_contentChangedSinceCommit)
                    _contentToken++;

                int gen = _commitGeneration + 1;
                WriteCommitFile(gen, pending: true);
                _contentChangedSinceCommit = false;

                _preparedGeneration = gen;
                _preparedSegments = new List<SegmentInfo>(_committedSegments);
                _preparedContentToken = _contentToken;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Discards a prepared commit created by <see cref="PrepareCommit"/>.
    /// Deletes the <c>.pending</c> file and any segment files that were
    /// created exclusively for the prepared commit. Has no effect if no
    /// commit has been prepared.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public void Rollback()
    {
        EnterIndexingOperation();
        try
        {
            lock (_mergeIoLock)
            lock (_writeLock)
            {
                if (_preparedGeneration < 0)
                    return;

                // Delete the pending commit file.
                var pendingPath = Path.Combine(_directory.DirectoryPath,
                    $"segments_{_preparedGeneration}.pending");
                try { File.Delete(pendingPath); } catch { /* best-effort */ }

                // Delete segment files that exist only in the prepared state.
                if (_preparedSegments is not null)
                {
                    var committedIds = new HashSet<string>(
                        _committedSegments.Select(static s => s.SegmentId),
                        StringComparer.Ordinal);
                    foreach (var seg in _preparedSegments)
                    {
                        if (!committedIds.Contains(seg.SegmentId))
                        {
                            _committedSegments.Remove(seg);
                            DeleteSegmentFiles(seg.SegmentId);
                        }
                    }
                }

                _preparedGeneration = -1;
                _preparedSegments = null;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    private void DeleteSegmentFiles(string segId)
    {
        foreach (var file in Directory.GetFiles(_directory.DirectoryPath, segId + ".*"))
        {
            try { File.Delete(file); } catch { /* best-effort */ }
        }
        foreach (var file in Directory.GetFiles(_directory.DirectoryPath, segId + "_v_*.*"))
        {
            try { File.Delete(file); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if a commit has been prepared via <see cref="PrepareCommit"/>
    /// but not yet published via <see cref="Commit"/>.
    /// </summary>
    public bool HasPreparedCommit => _preparedGeneration >= 0;

    /// <summary>
    /// Force-merges all committed segments into a single segment, reclaiming disk space
    /// from hard-deleted documents and consolidating soft-deleted documents past their
    /// retention window. This is a synchronous, blocking operation — it holds the merge
    /// I/O lock for the duration and should only be called during maintenance windows
    /// or when write throughput is low.
    /// </summary>
    /// <returns>The number of segments that were merged, or 0 if no merge was performed.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public int Compact()
    {
        EnterIndexingOperation();
        try
        {
            return CompactWithLocks();
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Force-merges segments until the segment count reaches <paramref name="maxSegments"/>
    /// or fewer. Merges the smallest unprotected segments first. This is a synchronous,
    /// blocking operation.
    /// </summary>
    /// <param name="maxSegments">The target maximum number of segments. Must be at least 1.</param>
    /// <returns>The total number of segments that were merged, or 0 if no merge was needed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxSegments"/> is less than 1.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public int ForceMerge(int maxSegments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        EnterIndexingOperation();
        try
        {
            int totalMerged = 0;
            lock (_mergeIoLock)
            lock (_writeLock)
            {
                // Flush and apply deletions first.
                if (_pendingDeletes.Count > 0)
                    ApplyPendingDeletions(_committedSegments);
                if (_buffer.DocCount > 0)
                    FlushSegment();

                var protectedSegments = GetSnapshotProtectedSegments();

                while (_committedSegments.Count > maxSegments)
                {
                    var mergeable = _committedSegments
                        .Where(s => !protectedSegments.Contains(s.SegmentId))
                        .ToList();

                    if (mergeable.Count < 2)
                        break;

                    // Merge the smallest mergeable segments.
                    mergeable.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
                    int count = Math.Min(mergeable.Count, _committedSegments.Count - maxSegments + 1);
                    var toMerge = mergeable.GetRange(0, count);

                    var merger = new SegmentMerger(_directory, _config.MergePolicy, _config.PostingsSkipInterval,
                        _config.SoftDeleteRetentionSeconds);
                    var merged = merger.MergeAll(toMerge, ref _nextSegmentOrdinal);

                    if (merged is null)
                    {
                        foreach (var seg in toMerge)
                            _committedSegments.Remove(seg);
                    }
                    else
                    {
                        foreach (var seg in toMerge)
                            _committedSegments.Remove(seg);
                        _committedSegments.Add(merged);
                    }

                    totalMerged += toMerge.Count;
                }

                if (totalMerged > 0)
                {
                    _contentToken++;
                    _commitGeneration++;
                    WriteCommitFile(_commitGeneration);
                    WriteCommitStats(_commitGeneration);
                    _config.DeletionPolicy.OnCommit(_directory.DirectoryPath, _commitGeneration, GetSnapshotProtectedSegments());
                }
            }
            return totalMerged;
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    private int CompactWithLocks()
    {
        // Lock ordering: _mergeIoLock before _writeLock.
        lock (_mergeIoLock)
        lock (_writeLock)
        {
            // Apply any pending deletions then flush buffered documents
            if (_pendingDeletes.Count > 0)
                ApplyPendingDeletions(_committedSegments);

            if (_buffer.DocCount > 0)
                FlushSegment();

            if (_committedSegments.Count <= 1)
                return 0;

            var segmentsToMerge = _committedSegments.ToList();
            var protectedSegments = GetSnapshotProtectedSegments();

            // Remove protected segments from the merge set — they are held by
            // active snapshots and must not be deleted.
            var mergeable = segmentsToMerge
                .Where(s => !protectedSegments.Contains(s.SegmentId))
                .ToList();

            if (mergeable.Count < 2)
                return 0;

            int mergeableCount = mergeable.Count;

            var merger = new SegmentMerger(_directory, _config.MergePolicy, _config.PostingsSkipInterval,
                _config.SoftDeleteRetentionSeconds);
            var merged = merger.MergeAll(mergeable, ref _nextSegmentOrdinal);

            if (merged is null)
            {
                // All documents were deleted — remove the merged segments entirely.
                foreach (var seg in mergeable)
                    _committedSegments.Remove(seg);
            }
            else
            {
                // Replace the merged segments with the single result.
                foreach (var seg in mergeable)
                    _committedSegments.Remove(seg);
                _committedSegments.Add(merged);
            }

            // Commit before cleaning up old files — ensures the commit file references
            // the new segment before we delete anything the old segments depend on.
            _contentToken++;
            _commitGeneration++;
            WriteCommitFile(_commitGeneration);
            WriteCommitStats(_commitGeneration);
            _config.DeletionPolicy.OnCommit(_directory.DirectoryPath, _commitGeneration, GetSnapshotProtectedSegments());

            // Clean up old segment files no longer referenced. Must happen AFTER the
            // commit is durably written so a crash doesn't leave a commit referencing
            // deleted files.
            var activeSegments = new HashSet<string>(
                _committedSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
            foreach (var seg in segmentsToMerge)
            {
                if (!activeSegments.Contains(seg.SegmentId) &&
                    !protectedSegments.Contains(seg.SegmentId))
                {
                    merger.CleanupSegmentFiles(seg);
                }
            }

            return mergeableCount;
        }
    }

    private void CommitWithLocks()
    {
        // Lock ordering: _mergeIoLock first (so a running merge can finish before we
        // mutate .del files), then _writeLock. AddDocument holds only _writeLock and
        // continues to run while a merge IO phase is in progress.
        lock (_mergeIoLock)
        lock (_writeLock)
        {
            if (_preparedGeneration >= 0)
            {
                // Publish the prepared commit by renaming .pending → final.
                PublishPreparedCommit();
                return;
            }

            using var activity = Diagnostics.LeanCorpusActivitySource.Source
                .StartActivity(Diagnostics.LeanCorpusActivitySource.Commit);
            activity?.SetTag("index.commit_generation", _commitGeneration + 1);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            CommitCore();
            sw.Stop();
            _config.Metrics.RecordCommit(sw.Elapsed);

            activity?.SetTag("index.segment_count", _committedSegments.Count);
        }
    }

    private void PublishPreparedCommit()
    {
        var pendingPath = Path.Combine(_directory.DirectoryPath,
            $"segments_{_preparedGeneration}.pending");
        var finalPath = Path.Combine(_directory.DirectoryPath,
            $"segments_{_preparedGeneration}");

        // Atomically rename .pending → final commit file.
        File.Move(pendingPath, finalPath, overwrite: false);

        _commitGeneration = _preparedGeneration;
        _contentToken = _preparedContentToken;
        _contentChangedSinceCommit = false;

        // Persist stats alongside the commit.
        WriteCommitStats(_commitGeneration);

        // Apply deletion policy to prune old commits.
        _config.DeletionPolicy.OnCommit(_directory.DirectoryPath, _commitGeneration,
            GetSnapshotProtectedSegments());

        // Schedule background merge.
        ScheduleBackgroundMerge();

        _preparedGeneration = -1;
        _preparedSegments = null;
    }

    private void CommitCore()
    {
        // Snapshot the segments that exist BEFORE flushing. Deletions from
        // UpdateDocument must only target these older segments, not the
        // replacement segment that will be flushed next.
        var preFlushSegmentCount = _committedSegments.Count;

        // Apply pending deletions to pre-existing segments only (for UpdateDocument)
        if (preFlushSegmentCount > 0 && _pendingDeletes.Count > 0)
            ApplyPendingDeletions(_committedSegments.GetRange(0, preFlushSegmentCount));

        // Flush any DWPT pool buffers before the main flush
        FlushDwptPool();

        // Flush any remaining buffered documents
        if (_buffer.DocCount > 0)
            FlushSegment();

        // Apply any remaining deletions to ALL segments (including the just-flushed one).
        // This handles the case where DeleteDocuments + Commit are called without UpdateDocument.
        if (_pendingDeletes.Count > 0)
            ApplyPendingDeletions(_committedSegments);

        if (_contentChangedSinceCommit)
            _contentToken++;

        // Write segments_N commit file (atomic: write to temp, then rename)
        _commitGeneration++;
        WriteCommitFile(_commitGeneration);
        _contentChangedSinceCommit = false;

        // Persist index statistics alongside the commit so IndexSearcher can
        // skip the expensive full-segment scan on construction.
        WriteCommitStats(_commitGeneration);

        // Apply deletion policy to prune old commit files
        _config.DeletionPolicy.OnCommit(_directory.DirectoryPath, _commitGeneration, GetSnapshotProtectedSegments());

        // Schedule merge in background (non-blocking) only after every reader of
        // _committedSegments and its files has completed. Scheduling earlier allowed
        // a fast merge to delete segment files the post-commit work still needed.
        ScheduleBackgroundMerge();
    }

    private void WriteCommitFile(int generation, bool pending = false)
    {
        var commitFile = Path.Combine(_directory.DirectoryPath, $"segments_{generation}");
        if (pending)
            commitFile += ".pending";

        var segmentIds = new List<string>(_committedSegments.Count);
        foreach (var seg in _committedSegments)
            segmentIds.Add(seg.SegmentId);
        var commitData = new CommitData
        {
            Segments = segmentIds,
            Generation = generation,
            ContentToken = _contentToken
        };
        var commitJson = JsonSerializer.Serialize(commitData, LeanCorpusJsonContext.Default.CommitData);

        // Append a CRC32 trailer so torn writes (where the JSON byte-tail survives but
        // the rename did not flush) can be detected on recovery. The trailer line is
        // optional on read, for backward compatibility with files written before this
        // line was added.
        var fileContent = CommitFileFormat.Wrap(commitJson);

        if (_config.DurableCommits)
        {
            // Segment data files are immutable once written. They are flushed to disk
            // at creation time (via Stream.Flush(flushToDisk: true) in each writer or
            // IndexOutput with durable: true). Only the directory entry sync is needed
            // here to make file-name metadata durable before the commit marker rename.
            Store.DirectoryFsync.Sync(_directory.DirectoryPath, strict: true);

            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: true);
        }
        else
        {
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: false);
        }
    }

    /// <summary>
    /// Computes current index statistics from committed segments and writes
    /// a stats_N.json file for the given commit generation.
    /// </summary>
    private void WriteCommitStats(int generation)
    {
        int totalDocCount = 0;
        int liveDocCount = 0;
        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var seg in _committedSegments)
        {
            var segmentStats = SegmentStats.TryLoadFrom(SegmentStats.GetStatsPath(_directory.DirectoryPath, seg.SegmentId));
            if (segmentStats is not null &&
                segmentStats.TotalDocCount == seg.DocCount &&
                segmentStats.LiveDocCount == seg.LiveDocCount)
            {
                AccumulateSegmentStats(segmentStats, fieldLengthSums, fieldDocCounts);
                totalDocCount += segmentStats.TotalDocCount;
                liveDocCount += segmentStats.LiveDocCount;
                continue;
            }

            AccumulateSegmentStatsByScan(seg, fieldLengthSums, fieldDocCounts, ref totalDocCount, ref liveDocCount);
        }

        var avgFieldLengths = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (field, sum) in fieldLengthSums)
        {
            int count = fieldDocCounts.GetValueOrDefault(field, 1);
            avgFieldLengths[field] = count > 0 ? (float)sum / count : 1.0f;
        }

        var stats = new IndexStats(totalDocCount, liveDocCount, avgFieldLengths, fieldDocCounts, fieldLengthSums);
        stats.WriteTo(IndexStats.GetStatsPath(_directory.DirectoryPath, generation));
    }

    private static void AccumulateSegmentStats(
        SegmentStats segmentStats,
        Dictionary<string, long> fieldLengthSums,
        Dictionary<string, int> fieldDocCounts)
    {
        foreach (var (field, sum) in segmentStats.FieldLengthSums)
            fieldLengthSums[field] = fieldLengthSums.GetValueOrDefault(field) + sum;

        foreach (var (field, count) in segmentStats.FieldDocCounts)
            fieldDocCounts[field] = fieldDocCounts.GetValueOrDefault(field) + count;
    }

    private void AccumulateSegmentStatsByScan(
        SegmentInfo segment,
        Dictionary<string, long> fieldLengthSums,
        Dictionary<string, int> fieldDocCounts,
        ref int totalDocCount,
        ref int liveDocCount)
    {
        SegmentReader? reader = null;
        try
        {
            reader = new SegmentReader(_directory, segment);
        }
        catch (FileNotFoundException)
        {
            // A background merge may have deleted this segment's files.
            // Skip the segment rather than failing the commit.
            return;
        }

        using (reader)
        {
            totalDocCount += reader.MaxDoc;
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                if (!reader.IsLive(docId))
                    continue;

                liveDocCount++;
                foreach (var field in segment.FieldNames)
                {
                    int length = reader.GetFieldLength(docId, field);
                    fieldLengthSums[field] = fieldLengthSums.GetValueOrDefault(field) + length;
                    fieldDocCounts[field] = fieldDocCounts.GetValueOrDefault(field) + 1;
                }
            }
        }
    }

    /// <summary>
    /// Releases all resources held by this writer, including the directory write lock.
    /// Cancels any background merge task and waits for in-progress merge I/O to complete.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        // Drain indexing callers that passed the disposed-check gate but have not
        // yet completed their work. Without this fence they could race resource
        // disposal below while holding writer state or semaphore slots.
        // A hard timeout of 30 seconds prevents a stuck AddDocument call from
        // hanging process shutdown indefinitely.
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

        // Cancel and await background merge
        _mergeCts.Cancel();
        try { _mergeTask?.Wait(); }
        catch (AggregateException) { /* Expected: merge task cancelled during shutdown */ }
        catch (ObjectDisposedException) { /* CTS already disposed */ }
        _mergeCts.Dispose();

        _backpressureSemaphore?.Dispose();

        // Release the directory write lock
        _writeLockFile.Dispose();
        var lockPath = Path.Combine(_directory.DirectoryPath, "write.lock");
        try { File.Delete(lockPath); } catch { /* best-effort */ }
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

    /// <summary>
    /// Checks whether merge backpressure should pause indexing.
    /// When <see cref="IndexWriterConfig.MergeThrottleSegments"/> is set and the
    /// number of committed segments exceeds it, this returns true.
    /// </summary>
    /// <summary>
    /// Acquires one semaphore slot. If the semaphore is exhausted, the first contending
    /// thread is elected to perform a flush; the rest simply <see cref="SemaphoreSlim.Wait()"/>.
    /// Caller must hold no locks.
    /// </summary>
    private void AcquireBackpressureSlot()
    {
        if (_backpressureSemaphore is null) return;
        if (_backpressureSemaphore.Wait(0)) return;

        if (Interlocked.CompareExchange(ref _flushElection, 1, 0) == 0)
        {
            try
            {
                lock (_writeLock)
                {
                    if (_buffer.DocCount > 0)
                        FlushSegment();
                }
            }
            finally
            {
                Volatile.Write(ref _flushElection, 0);
            }
        }
        _backpressureSemaphore.Wait();
    }

    private bool ShouldThrottleForMerge()
    {
        return _config.MergeThrottleSegments > 0
            && _committedSegments.Count >= _config.MergeThrottleSegments;
    }

    /// <summary>
    /// Returns the estimated RAM used by all buffered data. O(1), using the
    /// incrementally tracked <c>_buffer.PostingsRamBytes</c> instead of iterating
    /// every <see cref="PostingAccumulator"/>.
    /// </summary>
    private long ComputeEstimatedRamBytes()
    {
        return _buffer.PostingsRamBytes + _buffer.EstimatedRamBytes;
    }

    private void ResetBuffer()
    {
        _buffer.Reset();
        _flushSeqNoStart = _nextSequenceNumber;
    }

    private void LoadLatestCommit()
    {
        IndexOpenGuard.EnsureNoBlockingMigration(_directory, _config.CompatibilityMode);
        var recovery = IndexRecovery.RecoverLatestCommit(_directory.DirectoryPath);
        if (recovery is null) return;
        IndexOpenGuard.EnsureCanOpenSegments(_directory, recovery.SegmentIds, _config.CompatibilityMode, forWriting: true);

        _commitGeneration = recovery.Generation;
        _contentToken = recovery.ContentToken;
        // Parse the maximum segment ordinal from the segment IDs (e.g. "seg_557" → 557)
        // rather than using Segments.Count, which is incorrect after merges reduce
        // the segment count while ordinals keep climbing.
        int maxOrdinal = 0;
        foreach (var segId in recovery.SegmentIds)
        {
            if (segId.StartsWith("seg_", StringComparison.Ordinal) &&
                int.TryParse(segId.AsSpan(4), out int ordinal) &&
                ordinal >= maxOrdinal)
            {
                maxOrdinal = ordinal;
            }
        }
        _nextSegmentOrdinal = maxOrdinal + 1;

        foreach (var segId in recovery.SegmentIds)
        {
            var segPath = Path.Combine(_directory.DirectoryPath, segId + ".seg");
            if (!File.Exists(segPath))
                continue;

            var seg = SegmentInfo.ReadFrom(segPath);

            var basePath = Path.Combine(_directory.DirectoryPath, segId);
            var delPath = seg.DelGeneration.HasValue
                ? basePath + $"_gen_{seg.DelGeneration.Value}.del"
                : basePath + ".del";
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

            _committedSegments.Add(seg);
        }

        // Recover sequence number counter from the highest known seqno across all segments.
        if (_config.TrackSequenceNumbers)
        {
            long maxSeq = 0;
            foreach (var seg in _committedSegments)
            {
                if (seg.MaxSequenceNumber.HasValue && seg.MaxSequenceNumber.Value > maxSeq)
                    maxSeq = seg.MaxSequenceNumber.Value;
            }
            _nextSequenceNumber = maxSeq + 1;
            _flushSeqNoStart = _nextSequenceNumber;
        }
    }
    private void FlushSegment()
    {
        if (_buffer.DocCount == 0) return;

        int docCountToFlush = _buffer.DocCount;

        var segInfo = SegmentFlusher.Flush(
            _buffer, _config, _directory.DirectoryPath,
            ref _nextSegmentOrdinal, _commitGeneration,
            _flushSeqNoStart, _nextSequenceNumber);

        _committedSegments.Add(segInfo);
        ResetBuffer();

        // Release semaphore slots AFTER the flush is complete and buffers are cleared.
        if (_backpressureSemaphore is not null && docCountToFlush > 0)
        {
            int toRelease = Math.Min(docCountToFlush, _semaphoreSlotsHeld);
            if (toRelease > 0)
            {
                _backpressureSemaphore.Release(toRelease);
                _semaphoreSlotsHeld -= toRelease;
            }
        }
    }
}
