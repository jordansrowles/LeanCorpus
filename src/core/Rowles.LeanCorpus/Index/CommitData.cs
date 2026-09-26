namespace Rowles.LeanCorpus.Index;

internal sealed class CommitData
{
    public List<string> Segments { get; set; } = [];

    /// <summary>
    /// Per-commit mutable segment state. Null is reserved for legacy commit files
    /// written before deletion state became commit-scoped.
    /// </summary>
    public List<SegmentCommitState>? SegmentStates { get; set; }

    public int Generation { get; set; }

    public long ContentToken { get; set; }

    internal SegmentCommitState? GetSegmentState(int index)
        => SegmentStates is null ? null : SegmentStates[index];

    /// <summary>
    /// Validates invariants after deserialisation. Throws <see cref="InvalidDataException"/>
    /// when required fields are missing, empty, or out of range.
    /// </summary>
    internal void Validate()
    {
        if (Segments is null)
            throw new InvalidDataException("Commit data has a null Segments list.");
        if (Generation < 0)
            throw new InvalidDataException($"Commit data has a negative Generation ({Generation}).");

        if (SegmentStates is null)
            return;

        if (SegmentStates.Count != Segments.Count)
            throw new InvalidDataException("Commit data must contain exactly one segment state for each segment.");

        for (int i = 0; i < SegmentStates.Count; i++)
        {
            var state = SegmentStates[i];
            if (state is null)
                throw new InvalidDataException($"Commit data contains null state for segment at index {i}.");
            state.Validate();
            if (!string.Equals(Segments[i], state.SegmentId, StringComparison.Ordinal))
                throw new InvalidDataException($"Commit segment state at index {i} names '{state.SegmentId}', expected '{Segments[i]}'.");
        }
    }
}

internal sealed class SegmentCommitState
{
    public string SegmentId { get; set; } = string.Empty;

    public int? DelGeneration { get; set; }

    public int LiveDocCount { get; set; }

    public long? EarliestSoftDeleteTimestamp { get; set; }

    internal static SegmentCommitState FromSegmentInfo(Segment.SegmentInfo segment)
        => new()
        {
            SegmentId = segment.SegmentId,
            DelGeneration = segment.DelGeneration,
            LiveDocCount = segment.LiveDocCount,
            EarliestSoftDeleteTimestamp = segment.EarliestSoftDeleteTimestamp
        };

    internal void ApplyTo(Segment.SegmentInfo segment)
    {
        if (!string.Equals(SegmentId, segment.SegmentId, StringComparison.Ordinal))
            throw new InvalidDataException($"Commit state names segment '{SegmentId}', but metadata names '{segment.SegmentId}'.");
        if (LiveDocCount > segment.DocCount)
            throw new InvalidDataException($"Commit state for segment '{SegmentId}' has LiveDocCount={LiveDocCount}, greater than DocCount={segment.DocCount}.");

        segment.DelGeneration = DelGeneration;
        segment.LiveDocCount = LiveDocCount;
        segment.EarliestSoftDeleteTimestamp = EarliestSoftDeleteTimestamp;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SegmentId))
            throw new InvalidDataException("Commit segment state has a null or empty SegmentId.");
        if (DelGeneration is < 0)
            throw new InvalidDataException($"Commit segment state for '{SegmentId}' has a negative deletion generation.");
        if (LiveDocCount < 0)
            throw new InvalidDataException($"Commit segment state for '{SegmentId}' has a negative LiveDocCount.");
        if (EarliestSoftDeleteTimestamp is < 0)
            throw new InvalidDataException($"Commit segment state for '{SegmentId}' has a negative soft-delete timestamp.");
    }
}
