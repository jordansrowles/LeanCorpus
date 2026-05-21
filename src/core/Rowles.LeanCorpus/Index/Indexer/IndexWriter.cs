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

    // Unified posting accumulator keyed by qualified term ("field\0term")
    private Dictionary<string, PostingAccumulator> _postings = new(8192, StringComparer.Ordinal);
    // Flat stored field buffer: parallel arrays indexed by entry position
    private List<int> _sfFieldIds = new(4096);
    private List<StoredFieldValue> _sfValues = new(4096);
    private List<int> _sfDocStarts = new(256);
    private readonly Dictionary<string, int> _sfFieldNameToId = new(StringComparer.Ordinal);
    private readonly List<string> _sfFieldIdToName = new();
    // Buffered numeric fields per document
    private List<Dictionary<string, double>> _numericFields = [];
    // Per-field numeric values for range indexing: field → docId → value
    private Dictionary<string, Dictionary<int, double>> _numericIndex = new();
    private Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> _bufferedVectors =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _termPool = new(4096, StringComparer.Ordinal);
    // Per-field per-doc token counts for O(1) per-field norm computation
    private Dictionary<string, int[]> _docTokenCounts = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<int, float>> _fieldBoosts = new(StringComparer.Ordinal);
    // Track field names seen in this flush
    private readonly HashSet<string> _fieldNames = new(StringComparer.Ordinal);
    // Cache qualified term strings to avoid repeated string.Concat, keyed by the qualified term itself
    private Dictionary<string, string> _qualifiedTermPool = new(8192, StringComparer.Ordinal);
    // Cache field name prefixes ("fieldName\0") to avoid repeated prefix construction
    private readonly Dictionary<string, string> _fieldPrefixCache = new(StringComparer.Ordinal);
    // DocValues accumulators: field → per-doc values
    private Dictionary<string, List<double>> _numericDocValues = new(StringComparer.Ordinal);
    private Dictionary<string, List<string?>> _sortedDocValues = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<int, List<string>>> _sortedSetDocValues = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<int, List<double>>> _sortedNumericDocValues = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<int, List<byte[]>>> _binaryDocValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAnalyser> _analyserCache = new(StringComparer.Ordinal);
    private readonly SpanCountingTokenSink _spanCountingSink = new();
    private readonly SpanPostingTokenSink _spanPostingSink;
    private readonly List<string> _sortedTermsBuffer = new(capacity: 10000);
    // Parent bitset for block-join indexing: tracks which buffered doc IDs are parent docs
    private HashSet<int>? _parentDocIds;
    // Sequence number tracking
    private long _nextSequenceNumber;
    private long _flushSeqNoStart;

    private int _bufferedDocCount;
    private long _estimatedRamBytes;
    private long _postingsRamBytes; // incrementally tracked sum of all PostingAccumulator.EstimatedBytes
    private int _nextSegmentOrdinal;
    private int _commitGeneration;
    private long _contentToken;
    private bool _contentChangedSinceCommit;
    private readonly List<SegmentInfo> _committedSegments = [];
    // Pending deletions: field → term → set of matching terms to delete
    private readonly List<(string field, string term, bool isSoftDelete)> _pendingDeletes = [];
    private readonly Lock _writeLock = new();
    private readonly SemaphoreSlim? _backpressureSemaphore;
    private int _flushElection;
    // Files modified after this time are considered dirty for the next durable commit.
    // Initialised to MinValue so the first commit fsyncs every file in the directory.
    private DateTime _lastCommitFsyncUtc = DateTime.MinValue;
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
        _spanPostingSink = new SpanPostingTokenSink(this);

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
                    if (ShouldThrottleForMerge() && _bufferedDocCount > 0)
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
                            _parentDocIds ??= new HashSet<int>();
                            _parentDocIds.Add(_bufferedDocCount);
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
                    if (_bufferedDocCount > 0)
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
                    if (_bufferedDocCount > 0)
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
                if (_bufferedDocCount > 0)
                    FlushSegment();

                var merger = new SegmentMerger(_directory, _config.MergeThreshold, _config.PostingsSkipInterval);
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

    private void CommitWithLocks()
    {
        // Lock ordering: _mergeIoLock first (so a running merge can finish before we
        // mutate .del files), then _writeLock. AddDocument holds only _writeLock and
        // continues to run while a merge IO phase is in progress.
        lock (_mergeIoLock)
        lock (_writeLock)
        {
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
        if (_bufferedDocCount > 0)
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

    private void WriteCommitFile(int generation)
    {
        var commitFile = Path.Combine(_directory.DirectoryPath, $"segments_{generation}");
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
            // Belt-and-braces durability: fsync every segment file modified since the last
            // commit. The first commit ever (cutoff == MinValue) fsyncs everything; subsequent
            // commits only fsync newly created or rewritten files. A small skew is subtracted
            // to tolerate filesystems with low-resolution mtimes and clock drift.
            var fsyncCutoff = _lastCommitFsyncUtc == DateTime.MinValue
                ? DateTime.MinValue
                : _lastCommitFsyncUtc - TimeSpan.FromSeconds(2);
            foreach (var path in Directory.EnumerateFiles(_directory.DirectoryPath))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("segments_", StringComparison.Ordinal)) continue;
                if (string.Equals(name, "write.lock", StringComparison.Ordinal)) continue;
                if (name.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                if (fsyncCutoff != DateTime.MinValue && File.GetLastWriteTimeUtc(path) <= fsyncCutoff) continue;
                Store.DirectoryFsync.SyncFile(path, strict: true);
            }

            // Sync the directory itself so any prior file creations are durable before the rename.
            Store.DirectoryFsync.Sync(_directory.DirectoryPath, strict: true);

            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: true);
            _lastCommitFsyncUtc = DateTime.UtcNow;
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

        var stats = new IndexStats(totalDocCount, liveDocCount, avgFieldLengths, fieldDocCounts);
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
        using var reader = new SegmentReader(_directory, segment);
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
        var spinWait = new SpinWait();
        while (Volatile.Read(ref _inFlightAdds) != 0)
            spinWait.SpinOnce();

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
        if (_bufferedDocCount >= _config.MaxBufferedDocs)
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
                    if (_bufferedDocCount > 0)
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
    /// incrementally tracked <c>_postingsRamBytes</c> instead of iterating
    /// every <see cref="PostingAccumulator"/>.
    /// </summary>
    private long ComputeEstimatedRamBytes()
    {
        return _postingsRamBytes + _estimatedRamBytes;
    }

    private void ResetBuffer()
    {
        // Return pooled arrays from all accumulators before clearing
        foreach (var acc in _postings.Values)
            acc.ReturnBuffers();
        _postings.Clear();
        _sfFieldIds.Clear();
        _sfValues.Clear();
        _sfDocStarts.Clear();
        _numericFields.Clear();
        _termPool.Clear();
        _fieldNames.Clear();
        _qualifiedTermPool.Clear();
        _numericIndex.Clear();
        _bufferedVectors.Clear();
        _numericDocValues.Clear();
        _sortedDocValues.Clear();
        _sortedSetDocValues.Clear();
        _sortedNumericDocValues.Clear();
        _binaryDocValues.Clear();
        _fieldBoosts.Clear();
        _sortedTermsBuffer.Clear();
        _bufferedDocCount = 0;
        _estimatedRamBytes = 0;
        _postingsRamBytes = 0;
        _docTokenCounts.Clear();
        _parentDocIds = null;
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
        _nextSegmentOrdinal = recovery.SegmentIds.Count;

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
}
