namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Tracks a DWPT flush that was detached from the publication lock.
/// Holds the immutable snapshot, the flush I/O result (once completed),
/// and metadata needed to publish the segment after the flush finishes.
/// </summary>
internal sealed class FlushPendingState
{
    /// <summary>The immutable snapshot of the DWPT taken before clearing it.</summary>
    internal required DwptFlushSnapshot Snapshot { get; init; }

    /// <summary>Segment ordinal assigned to this flush.</summary>
    internal required int SegmentOrdinal { get; init; }

    /// <summary>First sequence number in this flush (0 if tracking disabled).</summary>
    internal required long SeqStart { get; init; }

    /// <summary>Last sequence number in this flush (0 if tracking disabled).</summary>
    internal required long SeqEnd { get; init; }

    /// <summary>
    /// The flush result, set once <see cref="SegmentFlusher.FlushFromSnapshot"/>
    /// completes. Null until the flush I/O finishes.
    /// </summary>
    internal SegmentInfo? Result { get; set; }

    /// <summary>
    /// The task performing the flush I/O, or null for synchronous flushes.
    /// Only used when <see cref="IndexWriterConfig.MaxConcurrentFlushes"/> > 1.
    /// </summary>
    internal Task? Task { get; set; }

    /// <summary>
    /// Number of documents in this flush, for backpressure accounting.
    /// </summary>
    internal int DocCount => Snapshot.DocCount;
}
