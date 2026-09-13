namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Owns detached DWPT flushes. Physical work is admitted independently from
/// logical ordering, while publication remains a short writer-lock operation.
/// </summary>
internal sealed class FlushCoordinator
{
    private readonly IndexWriter _writer;
    private readonly Lock _gate = new();
    private readonly List<FlushPendingState> _pending = [];
    private int _activeExecutions;

    internal FlushCoordinator(IndexWriter writer) => _writer = writer;

    internal void Submit(DwptFlushBatch batch, int segmentOrdinal, int commitGeneration, long seqStart, long seqEnd)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            _pending.Add(new FlushPendingState
            {
                Batch = batch,
                SegmentOrdinal = segmentOrdinal,
                CommitGeneration = commitGeneration,
                SeqStart = seqStart,
                SeqEnd = seqEnd
            });
            StartEligibleExecutions();
        }
    }

    /// <summary>
    /// Publishes the completed contiguous prefix. The caller owns WriteLock,
    /// preserving the global order: WriteLock then coordinator gate.
    /// </summary>
    internal void PublishCompletedPrefix()
    {
        lock (_gate)
        {
            StartEligibleExecutions();
            int published = 0;
            while (published < _pending.Count)
            {
                var state = _pending[published];
                var execution = state.ExecutionTask;
                if (execution is null || !execution.IsCompleted)
                    break;

                // GetAwaiter preserves the physical-flush exception rather than
                // exposing an AggregateException from implementation details.
                var segment = execution.GetAwaiter().GetResult();
                if (!state.Published)
                {
                    _writer.CommittedSegments.Add(segment);
                    _writer.ContentChangedSinceCommit = true;
                    state.Published = true;
                }
                published++;
            }

            if (published > 0)
                _pending.RemoveRange(0, published);
        }
    }

    /// <summary>Waits for every submitted flush and publishes in reserved order.</summary>
    internal void DrainAndPublish()
    {
        while (true)
        {
            Task<SegmentInfo>[] active;
            lock (_gate)
            {
                StartEligibleExecutions();
                if (_pending.Count == 0)
                    return;
                active = _pending
                    .Select(static state => state.ExecutionTask)
                    .Where(static task => task is not null)
                    .Cast<Task<SegmentInfo>>()
                    .ToArray();
            }

            // This happens with WriteLock held by the caller. Execution never
            // acquires WriteLock, so waiting cannot invert the lock order.
            foreach (var execution in active)
                execution.GetAwaiter().GetResult();

            PublishCompletedPrefix();
        }
    }

    internal int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    private void StartEligibleExecutions()
    {
        while (_activeExecutions < _writer.Config.MaxConcurrentFlushes)
        {
            var next = _pending.FirstOrDefault(static state => state.ExecutionTask is null);
            if (next is null)
                return;

            _activeExecutions++;
            next.ExecutionTask = Task.Run(() => Execute(next));
            _ = next.ExecutionTask.ContinueWith(
                static (_, state) => ((FlushCoordinator)state!).ExecutionFinished(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private SegmentInfo Execute(FlushPendingState state)
    {
        try
        {
            Interlocked.Increment(ref _writer.ActiveFlushCount);
            _writer.Config.PhysicalFlushStarted?.Invoke();
            return SegmentFlusher.FlushFromBatch(state.Batch, _writer.Config,
                _writer.Directory.DirectoryPath, state.SegmentOrdinal,
                state.CommitGeneration, state.SeqStart, state.SeqEnd);
        }
        catch (Exception ex)
        {
            _writer.MarkIndexingFailed(ex);
            // A failed detached flush is never publishable. Remove any files
            // created before the physical failure so reopening cannot mistake
            // partial output for an orphaned completed segment.
            CommitManager.DeleteSegmentFiles($"seg_{state.SegmentOrdinal}", _writer.Directory);
            throw;
        }
        finally
        {
            try
            {
                _writer.Config.PhysicalFlushCompleted?.Invoke();
            }
            finally
            {
                state.Batch.Dispose();
                if (state.Batch.PendingBytesAccounted)
                    Interlocked.Add(ref _writer.PendingFlushBytes, -state.Batch.EstimatedBytes);
                Interlocked.Decrement(ref _writer.ActiveFlushCount);
            }
        }
    }

    private void ExecutionFinished()
    {
        lock (_gate)
        {
            _activeExecutions--;
            StartEligibleExecutions();
        }
    }
}
