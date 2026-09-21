namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Tracks a DWPT flush that was detached from the publication lock.
/// Holds the owned detached snapshot, its asynchronous physical execution, and
/// metadata needed for ordered publication.
/// </summary>
internal sealed class FlushPendingState
{
    /// <summary>The owned snapshot detached from the DWPT.</summary>
    internal required DwptFlushSnapshot Snapshot { get; init; }

    /// <summary>Segment ordinal assigned to this flush.</summary>
    internal required int SegmentOrdinal { get; init; }

    /// <summary>Commit generation captured at detachment.</summary>
    internal required int CommitGeneration { get; init; }

    /// <summary>First sequence number in this flush (0 if tracking disabled).</summary>
    internal required long SeqStart { get; init; }

    /// <summary>Last sequence number in this flush (0 if tracking disabled).</summary>
    internal required long SeqEnd { get; init; }

    /// <summary>
    /// The physical flush task. It is assigned exactly once by
    /// <see cref="FlushCoordinator"/> when this state enters active execution.
    /// </summary>
    internal Task<SegmentInfo>? ExecutionTask { get; set; }

    /// <summary>Whether the result has been published into writer state.</summary>
    internal bool Published { get; set; }

    /// <summary>
    /// Number of documents in this flush, for backpressure accounting.
    /// </summary>
    internal int DocCount => Snapshot.DocCount;
}
