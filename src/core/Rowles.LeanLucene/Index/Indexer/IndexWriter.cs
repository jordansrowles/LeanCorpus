using System.Text.Json;
using Rowles.LeanLucene.Serialization;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Indexer;

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
    private List<string> _sfValues = new(4096);
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
    // Track field names seen in this flush
    private readonly HashSet<string> _fieldNames = new(StringComparer.Ordinal);
    // Cache qualified term strings to avoid repeated string.Concat, keyed by the qualified term itself
    private Dictionary<string, string> _qualifiedTermPool = new(8192, StringComparer.Ordinal);
    // Cache field name prefixes ("fieldName\0") to avoid repeated prefix construction
    private readonly Dictionary<string, string> _fieldPrefixCache = new(StringComparer.Ordinal);
    // DocValues accumulators: field → per-doc values
    private Dictionary<string, List<double>> _numericDocValues = new(StringComparer.Ordinal);
    private Dictionary<string, List<string?>> _sortedDocValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAnalyser> _analyserCache = new(StringComparer.Ordinal);
    private readonly List<string> _sortedTermsBuffer = new(capacity: 10000);
    // Parent bitset for block-join indexing: tracks which buffered doc IDs are parent docs
    private HashSet<int>? _parentDocIds;

    private int _bufferedDocCount;
    private long _estimatedRamBytes;
    private long _postingsRamBytes; // incrementally tracked sum of all PostingAccumulator.EstimatedBytes
    private int _nextSegmentOrdinal;
    private int _commitGeneration;
    private long _contentToken;
    private bool _contentChangedSinceCommit;
    private readonly List<SegmentInfo> _committedSegments = [];
    // Pending deletions: field → term → set of matching terms to delete
    private readonly List<(string field, string term)> _pendingDeletes = [];
    private readonly Lock _writeLock = new();
    private readonly SemaphoreSlim? _backpressureSemaphore;
    private int _flushElection;
    // Files modified after this time are considered dirty for the next durable commit.
    // Initialised to MinValue so the first commit fsyncs every file in the directory.
    private DateTime _lastCommitFsyncUtc = DateTime.MinValue;
    private int _semaphoreSlotsHeld;
    private readonly List<IndexSnapshot> _heldSnapshots = [];
    private int _disposed;      // 0 = alive, 1 = disposed (atomically set via Interlocked)
    private int _inFlightAdds;  // count of AddDocumentLockFree callers currently inside the hot path
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
    /// Indexes a single document. Validates the document against the schema if one is configured.
    /// May block if <see cref="IndexWriterConfig.MaxQueuedDocs"/> backpressure is enabled.
    /// Automatically flushes a segment when the RAM or document count threshold is reached.
    /// </summary>
    /// <param name="doc">The document to index.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    /// <exception cref="SchemaValidationException">Thrown if the document violates the configured schema.</exception>
    public void AddDocument(LeanDocument doc)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _config.Schema?.Validate(doc);

        // Apply backpressure if enabled: wait for a semaphore slot before acquiring the write lock.
        // This prevents unbounded memory growth when documents are queued faster than they can be flushed.
        // When the semaphore is exhausted, ONE thread is elected to flush while others
        // simply Wait() for the slot to become available. The election prevents N threads from all
        // re-acquiring _writeLock and sequentially calling FlushSegment when only one flush is needed.
        AcquireBackpressureSlot();

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

                AddDocumentCore(doc);
            }
        }
        catch
        {
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

    /// <summary>
    /// Indexes a batch of documents with a single lock acquisition.
    /// Faster than calling <see cref="AddDocument"/> in a loop because lock
    /// and backpressure overhead is paid once for the entire batch.
    /// </summary>
    public void AddDocuments(IReadOnlyList<LeanDocument> documents)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (documents.Count == 0) return;
        if (_backpressureSemaphore is not null && documents.Count > _config.MaxQueuedDocs)
        {
            foreach (var document in documents)
                AddDocument(document);
            return;
        }

        int acquired = 0;
        bool addedToHeldSlots = false;
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
                    AddDocumentCore(documents[i]);
            }
        }
        catch
        {
            if (_backpressureSemaphore is not null && acquired > 0)
            {
                if (!addedToHeldSlots)
                {
                    _backpressureSemaphore.Release(acquired);
                    throw;
                }

                int toRelease;
                lock (_writeLock)
                {
                    toRelease = Math.Min(acquired, Math.Max(0, _semaphoreSlotsHeld));
                    if (toRelease > 0)
                        _semaphoreSlotsHeld -= toRelease;
                }
                if (toRelease > 0)
                    _backpressureSemaphore.Release(toRelease);
            }
            throw;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (block.Count < 2)
            throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
        if (_backpressureSemaphore is not null && block.Count > _config.MaxQueuedDocs)
            throw new InvalidOperationException(
                $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({_config.MaxQueuedDocs}).");

        int acquired = 0;
        bool addedToHeldSlots = false;
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

                // Index all docs in the block contiguously.
                // Record the parent ID BEFORE its AddDocumentCore call so that
                // a mid-block flush (triggered inside AddDocumentCore) includes
                // the correct parent in the ParentBitSet.
                for (int i = 0; i < block.Count; i++)
                {
                    if (i == block.Count - 1)
                    {
                        _parentDocIds ??= new HashSet<int>();
                        _parentDocIds.Add(_bufferedDocCount);
                    }
                    AddDocumentCore(block[i]);
                }
            }
        }
        catch
        {
            if (_backpressureSemaphore is not null && acquired > 0)
            {
                if (!addedToHeldSlots)
                {
                    _backpressureSemaphore.Release(acquired);
                    throw;
                }

                int toRelease;
                lock (_writeLock)
                {
                    toRelease = Math.Min(acquired, Math.Max(0, _semaphoreSlotsHeld));
                    if (toRelease > 0)
                        _semaphoreSlotsHeld -= toRelease;
                }
                if (toRelease > 0)
                    _backpressureSemaphore.Release(toRelease);
            }
            throw;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            // Capture the count of already-committed segments before any flush.
            // Deletions must not target the replacement segment that is about to be added.
            int preFlushSegmentCount = _committedSegments.Count;

            FlushDwptPool();
            if (_bufferedDocCount > 0)
                FlushSegment();

            _pendingDeletes.Add((field, term));
            // Restrict deletions to the segments that existed before this call.
            ApplyPendingDeletions(_committedSegments.GetRange(0, preFlushSegmentCount));
            AddDocumentCore(replacement);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // Lock ordering: _mergeIoLock first (so a running merge can finish before we
        // mutate .del files), then _writeLock. AddDocument holds only _writeLock and
        // continues to run while a merge IO phase is in progress.
        lock (_mergeIoLock)
        lock (_writeLock)
        {
            using var activity = Diagnostics.LeanLuceneActivitySource.Source
                .StartActivity(Diagnostics.LeanLuceneActivitySource.Commit);
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
        var tempFile = commitFile + ".tmp";
        var segmentIds = new List<string>(_committedSegments.Count);
        foreach (var seg in _committedSegments)
            segmentIds.Add(seg.SegmentId);
        var commitData = new CommitData
        {
            Segments = segmentIds,
            Generation = generation,
            ContentToken = _contentToken
        };
        var commitJson = JsonSerializer.Serialize(commitData, LeanLuceneJsonContext.Default.CommitData);

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

            // Write segments_N.tmp durably so its contents survive a crash before the rename.
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(fileContent);
                sw.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(tempFile, commitFile, overwrite: true);

            // Sync the directory again so the rename itself is durable.
            Store.DirectoryFsync.Sync(_directory.DirectoryPath, strict: true);
            _lastCommitFsyncUtc = DateTime.UtcNow;
        }
        else
        {
            File.WriteAllText(tempFile, fileContent);
            File.Move(tempFile, commitFile, overwrite: true);
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

        // Drain any AddDocumentLockFree callers that passed the disposed-check gate
        // but have not yet completed their work. Without this fence they would race
        // the semaphore dispose below and produce an ObjectDisposedException.
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
        _sortedTermsBuffer.Clear();
        _bufferedDocCount = 0;
        _estimatedRamBytes = 0;
        _postingsRamBytes = 0;
        _docTokenCounts.Clear();
        _parentDocIds = null;
    }

    private void LoadLatestCommit()
    {
        var recovery = IndexRecovery.RecoverLatestCommit(_directory.DirectoryPath);
        if (recovery is null) return;

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
            }
            else
            {
                seg.LiveDocCount = seg.DocCount;
            }

            _committedSegments.Add(seg);
        }
    }
}
