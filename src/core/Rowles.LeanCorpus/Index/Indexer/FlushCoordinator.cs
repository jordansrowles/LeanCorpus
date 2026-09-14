using System.Runtime.ExceptionServices;

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

    /// <summary>
    /// Transfers a detached batch to the coordinator. Ordinal and sequence
    /// reservation happen under the same gate as pending-queue insertion, so
    /// physical publication cannot be reordered by a submitter race.
    /// </summary>
    internal int Submit(DwptFlushBatch batch, int commitGeneration)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            int segmentOrdinal = _writer.ReserveSegmentOrdinal();
            long seqStart = 0;
            long seqEnd = 0;
            if (_writer.Config.TrackSequenceNumbers)
            {
                seqEnd = Interlocked.Add(ref _writer.NextSequenceNumberMut, batch.DocCount);
                seqStart = seqEnd - batch.DocCount;
            }
            _pending.Add(new FlushPendingState
            {
                Batch = batch,
                SegmentOrdinal = segmentOrdinal,
                CommitGeneration = commitGeneration,
                SeqStart = seqStart,
                SeqEnd = seqEnd
            });
            _writer.Config.FlushSubmissionReserved?.Invoke();
            StartEligibleExecutions();
            return segmentOrdinal;
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
            try
            {
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
            }
            finally
            {
                if (published > 0)
                    _pending.RemoveRange(0, published);
            }
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
            // Do not let one fault skip the remaining accepted physical work:
            // disposal must retain every resource until all of it is terminal.
            foreach (var execution in active)
            {
                try { execution.GetAwaiter().GetResult(); }
                catch { /* Selected after every accepted task is terminal. */ }
            }

            ExceptionDispatchInfo? failure = null;
            lock (_gate)
            {
                StartEligibleExecutions();
                if (_pending.Any(static state => state.ExecutionTask is null || !state.ExecutionTask.IsCompleted))
                    continue;

                int published = 0;
                while (published < _pending.Count)
                {
                    var state = _pending[published];
                    try
                    {
                        var segment = state.ExecutionTask!.GetAwaiter().GetResult();
                        if (!state.Published)
                        {
                            _writer.CommittedSegments.Add(segment);
                            _writer.ContentChangedSinceCommit = true;
                            state.Published = true;
                        }
                        published++;
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                        break;
                    }
                }

                // All batches are terminal and have released their owned memory.
                // A failed reserved state prevents later work from becoming writer-visible.
                _pending.Clear();
            }

            failure?.Throw();
            return;
        }
    }

    /// <summary>Waits until an accepted physical flush reaches a terminal state.</summary>
    internal bool WaitForPhysicalProgress()
    {
        Task[] active;
        lock (_gate)
        {
            StartEligibleExecutions();
            active = _pending
                .Select(static state => state.ExecutionTask)
                .Where(static task => task is { IsCompleted: false })
                .Cast<Task>()
                .ToArray();
            if (active.Length == 0)
                return false;
        }

        // Waiting on a snapshot of incomplete work cannot miss a completion
        // that occurred before this method acquired the coordinator gate.
        Task.WhenAny(active).GetAwaiter().GetResult();
        return true;
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
