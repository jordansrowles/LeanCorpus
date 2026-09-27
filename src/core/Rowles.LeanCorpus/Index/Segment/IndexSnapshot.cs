namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// A point-in-time, read-only snapshot of the committed segments.
/// Holds segment IDs so callers can open readers or back up files
/// without risk of segments being merged away.
/// </summary>
public sealed class IndexSnapshot
{
    /// <summary>Unique generation of the commit this snapshot represents.</summary>
    public int CommitGeneration { get; }

    /// <summary>Immutable segment descriptors captured at snapshot time.</summary>
    public IReadOnlyList<SegmentDescriptor> Segments { get; }

    /// <summary>UTC timestamp when the snapshot was taken.</summary>
    public DateTimeOffset TakenAtUtc { get; }

    internal IndexSnapshot(int commitGeneration, IReadOnlyList<SegmentInfo> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        CommitGeneration = commitGeneration;
        Segments = Array.AsReadOnly(segments.Select(static segment => new SegmentDescriptor(segment)).ToArray());
        TakenAtUtc = DateTimeOffset.UtcNow;
    }
}
