namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Tracks a DWPT flush that was detached from the publication lock.
/// Holds the detached snapshot only until physical execution takes ownership,
/// then retains only metadata needed for ordered publication.
/// </summary>
internal sealed class FlushPendingState
{
    private DwptFlushSnapshot? _snapshot;

    /// <summary>Segment ordinal assigned to this flush.</summary>
    internal int SegmentOrdinal { get; }

    /// <summary>Commit generation captured at detachment.</summary>
    internal int CommitGeneration { get; }

    /// <summary>First sequence number in this flush (0 if tracking disabled).</summary>
    internal long SeqStart { get; }

    /// <summary>Last sequence number in this flush (0 if tracking disabled).</summary>
    internal long SeqEnd { get; }

    /// <summary>Estimated bytes captured when the snapshot was detached.</summary>
    internal long EstimatedBytes { get; }

    /// <summary>Whether this flush was included in pending-byte accounting.</summary>
    internal bool PendingBytesAccounted { get; }

    /// <summary>Number of documents in the detached snapshot.</summary>
    internal int DocCount { get; }

    internal FlushPendingState(
        DwptFlushSnapshot snapshot,
        int segmentOrdinal,
        int commitGeneration,
        long seqStart,
        long seqEnd)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        SegmentOrdinal = segmentOrdinal;
        CommitGeneration = commitGeneration;
        SeqStart = seqStart;
        SeqEnd = seqEnd;
        EstimatedBytes = snapshot.EstimatedBytes;
        PendingBytesAccounted = snapshot.PendingBytesAccounted;
        DocCount = snapshot.DocCount;
    }

    /// <summary>
    /// Transfers the detached snapshot to the physical execution task. A state
    /// can only transfer ownership once.
    /// </summary>
    internal DwptFlushSnapshot TakeSnapshotForExecution()
        => Interlocked.Exchange(ref _snapshot, null)
            ?? throw new InvalidOperationException(
                "The detached flush snapshot has already been transferred.");

    /// <summary>Whether the pending state still retains the detached snapshot.</summary>
    internal bool SnapshotRetainedForTests => Volatile.Read(ref _snapshot) is not null;

    /// <summary>
    /// The physical flush task. It is assigned exactly once by
    /// <see cref="FlushCoordinator"/> when this state enters active execution.
    /// </summary>
    internal Task<SegmentInfo>? ExecutionTask { get; set; }

    /// <summary>Whether the result has been published into writer state.</summary>
    internal bool Published { get; set; }

}
