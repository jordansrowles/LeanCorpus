namespace Rowles.LeanLucene.Index.Indexer;

public sealed partial class IndexWriter
{
    private void ScheduleBackgroundMerge()
    {
        lock (_mergeLock)
        {
            if (_mergeTask is not null && !_mergeTask.IsCompleted)
                return;

            var ct = _mergeCts.Token;
            _mergeTask = Task.Factory.StartNew(() =>
            {
                if (ct.IsCancellationRequested) return;

                // Lock ordering: _mergeIoLock before _writeLock. _mergeIoLock is held for
                // the entire merge so Commit (which also acquires it before _writeLock)
                // cannot mutate .del files of segments being merged.
                lock (_mergeIoLock)
                {
                    if (ct.IsCancellationRequested) return;

                    using var mergeActivity = Diagnostics.LeanLuceneActivitySource.Source
                        .StartActivity(Diagnostics.LeanLuceneActivitySource.Merge);
                    var mergeSw = System.Diagnostics.Stopwatch.StartNew();

                    SegmentInfo[] sourceSegments;
                    HashSet<string> protectedSegments;
                    int localNextOrd;
                    lock (_writeLock)
                    {
                        if (ct.IsCancellationRequested) return;
                        sourceSegments = _committedSegments.ToArray();
                        protectedSegments = GetSnapshotProtectedSegments();
                        // Reserve a contiguous block of segment ordinals so concurrent
                        // FlushSegment calls (running under _writeLock during the unlocked
                        // merge IO phase) cannot collide with merge-output ordinals.
                        int reservation = Math.Max(8, sourceSegments.Length);
                        localNextOrd = _nextSegmentOrdinal;
                        _nextSegmentOrdinal += reservation;
                    }

                    // Heavy IO phase runs without _writeLock so AddDocument can buffer.
                    var merger = new SegmentMerger(_directory, _config.MergeThreshold, _config.PostingsSkipInterval);
                    var sourceList = sourceSegments.ToList();
                    var merged = merger.MaybeMerge(sourceList, ref localNextOrd, protectedSegments);

                    bool didMerge = !ReferenceEquals(merged, sourceList) && merged.Count != sourceSegments.Length;
                    mergeSw.Stop();

                    if (!didMerge)
                    {
                        mergeActivity?.SetTag("index.segments_merged", 0);
                        return;
                    }

                    // Compute deltas: which source segments were consumed, which outputs are new.
                    var sourceSet = new HashSet<string>(
                        sourceSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
                    var mergedSet = new HashSet<string>(
                        merged.Select(static s => s.SegmentId), StringComparer.Ordinal);
                    var consumedIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var s in sourceSegments)
                        if (!mergedSet.Contains(s.SegmentId))
                            consumedIds.Add(s.SegmentId);
                    var newSegments = new List<SegmentInfo>();
                    foreach (var s in merged)
                        if (!sourceSet.Contains(s.SegmentId))
                            newSegments.Add(s);

                    int segmentsMerged = consumedIds.Count - newSegments.Count + 1;
                    mergeActivity?.SetTag("index.segments_merged", segmentsMerged);
                    if (segmentsMerged > 0)
                        _config.Metrics.RecordMerge(mergeSw.Elapsed, segmentsMerged);

                    lock (_writeLock)
                    {
                        if (ct.IsCancellationRequested) return;

                        // Apply the swap: remove consumed inputs, add merged outputs.
                        // Newly flushed segments added during the unlocked IO phase remain in place.
                        _committedSegments.RemoveAll(s => consumedIds.Contains(s.SegmentId));
                        _committedSegments.AddRange(newSegments);
                        _nextSegmentOrdinal = Math.Max(_nextSegmentOrdinal, localNextOrd);

                        // Increment content token so searcher refresh logic detects
                        // the changed segment composition produced by the merge.
                        _contentToken++;
                        _commitGeneration++;
                        WriteCommitFile(_commitGeneration);
                        WriteCommitStats(_commitGeneration);
                        _config.DeletionPolicy.OnCommit(_directory.DirectoryPath, _commitGeneration, GetSnapshotProtectedSegments());

                        var activeSegments = new HashSet<string>(
                            _committedSegments.Select(static segment => segment.SegmentId),
                            StringComparer.Ordinal);
                        foreach (var segment in sourceSegments)
                        {
                            if (!activeSegments.Contains(segment.SegmentId) &&
                                !protectedSegments.Contains(segment.SegmentId))
                            {
                                merger.CleanupSegmentFiles(segment);
                            }
                        }
                    }
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Returns all committed and flushed segments for near-real-time search.
    /// Flushes any buffered documents first but does not write a commit file.
    /// Returns a snapshot copy of the segment list that is safe to read concurrently.
    /// </summary>
    public IReadOnlyList<SegmentInfo> GetNrtSegments()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            if (_bufferedDocCount > 0)
                FlushSegment();
            return _committedSegments.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Creates a point-in-time snapshot of the currently committed segments.
    /// While held, segments referenced by the snapshot will not be deleted during merge.
    /// Call <see cref="ReleaseSnapshot"/> when the snapshot is no longer needed.
    /// </summary>
    public IndexSnapshot CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            if (_bufferedDocCount > 0)
                FlushSegment();

            var snapshot = new IndexSnapshot(
                _commitGeneration,
                _committedSegments.Select(s => new SegmentInfo
                {
                    SegmentId = s.SegmentId,
                    DocCount = s.DocCount,
                    LiveDocCount = s.LiveDocCount,
                    CommitGeneration = s.CommitGeneration,
                    DelGeneration = s.DelGeneration,
                    FieldNames = [.. s.FieldNames],
                    IndexSortFields = s.IndexSortFields is null ? null : [.. s.IndexSortFields],
                    VectorFields = [.. s.VectorFields]
                }).ToList().AsReadOnly());

            _heldSnapshots.Add(snapshot);
            return snapshot;
        }
    }

    /// <summary>
    /// Releases a previously held snapshot, allowing its segments to be merged away.
    /// </summary>
    public void ReleaseSnapshot(IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_writeLock)
        {
            _heldSnapshots.Remove(snapshot);
        }
    }

    /// <summary>Returns the set of segment IDs protected by active snapshots.</summary>
    internal HashSet<string> GetSnapshotProtectedSegments()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snap in _heldSnapshots)
        {
            foreach (var seg in snap.Segments)
                ids.Add(seg.SegmentId);
        }
        return ids;
    }
}
