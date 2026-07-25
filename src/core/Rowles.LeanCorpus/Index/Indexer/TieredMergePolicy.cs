namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>Selects balanced merges using physical bytes and reclaimable deletions.</summary>
public sealed class TieredMergePolicy : IMergePolicy
{
    /// <summary>Segments smaller than this value are treated as this size for tier budgeting.</summary>
    public double FloorSegmentMB { get; init; } = 2;

    /// <summary>Target number of segments allowed per logarithmic size tier.</summary>
    public int SegmentsPerTier { get; init; }

    /// <summary>Maximum number of source segments in one merge.</summary>
    public int MaxMergeAtOnce { get; init; }

    /// <summary>Maximum normal merged-segment size.</summary>
    public double MaxMergedSegmentMB { get; init; } = 5 * 1024;

    /// <summary>Deletion percentage that makes a segment eligible for reclamation.</summary>
    public double DeletesPctAllowed { get; init; } = 20;

    /// <summary>Initialises a byte-aware tiered policy.</summary>
    public TieredMergePolicy(int mergeThreshold = 10)
    {
        if (mergeThreshold < 2)
            throw new ArgumentOutOfRangeException(nameof(mergeThreshold));
        SegmentsPerTier = mergeThreshold;
        MaxMergeAtOnce = mergeThreshold;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SegmentInfo> FindMerges(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlySet<string> protectedSegmentIds)
    {
        var candidates = new List<SegmentInfo>(segments.Count);
        foreach (var segment in segments)
        {
            if (!protectedSegmentIds.Contains(segment.SegmentId))
                candidates.Add(segment);
        }

        bool hasDeletionCandidate = candidates.Any(segment => segment.DeletionDensity * 100 >= DeletesPctAllowed);
        if (candidates.Count < SegmentsPerTier && !hasDeletionCandidate)
            return Array.Empty<SegmentInfo>();

        candidates.Sort(static (left, right) => EffectiveBytes(left).CompareTo(EffectiveBytes(right)));
        int width = Math.Min(MaxMergeAtOnce, candidates.Count);
        if (width < 2)
            return Array.Empty<SegmentInfo>();

        double floorBytes = FloorSegmentMB * 1024 * 1024;
        double maxMergedBytes = MaxMergedSegmentMB * 1024 * 1024;
        SegmentInfo[]? best = null;
        double bestScore = double.MaxValue;

        for (int start = 0; start + width <= candidates.Count; start++)
        {
            int count = width;
            double total = 0;
            double smallest = double.MaxValue;
            double largest = 0;
            long reclaimed = 0;
            var selection = new SegmentInfo[count];

            for (int i = 0; i < count; i++)
            {
                var segment = candidates[start + i];
                selection[i] = segment;
                double effective = Math.Max(floorBytes, EffectiveBytes(segment));
                total += effective;
                smallest = Math.Min(smallest, effective);
                largest = Math.Max(largest, effective);
                reclaimed += Math.Max(0, segment.TotalBytes - EffectiveBytes(segment));
            }

            if (total > maxMergedBytes)
                continue;

            double skew = largest / Math.Max(1, smallest);
            double reclaimRatio = reclaimed / Math.Max(1, total);
            double score = skew + total / Math.Max(1, maxMergedBytes) - reclaimRatio;
            if (score < bestScore)
            {
                bestScore = score;
                best = selection;
            }
        }

        return best ?? Array.Empty<SegmentInfo>();
    }

    private static long EffectiveBytes(SegmentInfo segment)
    {
        long bytes = segment.TotalBytes > 0 ? segment.TotalBytes : (long)segment.DocCount * 256;
        if (segment.DocCount == 0)
            return bytes;
        return Math.Max(1, bytes * segment.LiveDocCount / segment.DocCount);
    }
}
