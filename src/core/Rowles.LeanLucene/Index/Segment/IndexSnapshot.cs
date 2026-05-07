namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// A point-in-time, read-only snapshot of the committed segments.
/// Holds segment IDs so callers can open readers or back up files
/// without risk of segments being merged away.
/// </summary>
public sealed class IndexSnapshot
{
    /// <summary>Unique generation of the commit this snapshot represents.</summary>
    public int CommitGeneration { get; }

    /// <summary>Segment infos captured at snapshot time (defensive copy).</summary>
    public IReadOnlyList<SegmentInfo> Segments { get; }

    /// <summary>UTC timestamp when the snapshot was taken.</summary>
    public DateTimeOffset TakenAtUtc { get; }

    internal IndexSnapshot(int commitGeneration, IReadOnlyList<SegmentInfo> segments)
    {
        CommitGeneration = commitGeneration;
        Segments = segments;
        TakenAtUtc = DateTimeOffset.UtcNow;
    }
}
