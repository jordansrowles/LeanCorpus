namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Tiered merge policy: groups segments by size tier (powers of 2) and merges
/// the smallest segments when a tier reaches the configured threshold.
/// </summary>
public sealed class TieredMergePolicy : IMergePolicy
{
    private readonly int _mergeThreshold;

    /// <summary>
    /// Initialises a tiered merge policy with the given threshold.
    /// </summary>
    /// <param name="mergeThreshold">
    /// Number of segments at one size tier before a merge is triggered. Default 10.
    /// </param>
    public TieredMergePolicy(int mergeThreshold = 10)
    {
        _mergeThreshold = mergeThreshold;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SegmentInfo> FindMerges(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlySet<string> protectedSegmentIds)
    {
        if (segments.Count < _mergeThreshold)
            return Array.Empty<SegmentInfo>();

        // Group segments by size tier without LINQ allocations.
        var tierBuckets = new Dictionary<int, List<SegmentInfo>>();
        foreach (var s in segments)
        {
            int tier = GetSizeTier(s.DocCount);
            if (!tierBuckets.TryGetValue(tier, out var bucket))
            {
                bucket = new List<SegmentInfo>();
                tierBuckets[tier] = bucket;
            }
            bucket.Add(s);
        }

        // Collect tiers that meet the merge threshold.
        var eligibleTiers = new List<int>();
        foreach (var (tier, candidates) in tierBuckets)
        {
            int mergeableCount = 0;
            foreach (var segment in candidates)
            {
                if (!protectedSegmentIds.Contains(segment.SegmentId))
                    mergeableCount++;
            }
            if (mergeableCount >= _mergeThreshold)
                eligibleTiers.Add(tier);
        }

        if (eligibleTiers.Count == 0)
            return Array.Empty<SegmentInfo>();

        // Collect all mergeable segments from all eligible tiers, sorted by doc count.
        var allMergeable = new List<SegmentInfo>();
        foreach (var tierKey in eligibleTiers)
        {
            foreach (var segment in tierBuckets[tierKey])
            {
                if (!protectedSegmentIds.Contains(segment.SegmentId))
                    allMergeable.Add(segment);
            }
        }

        if (allMergeable.Count < 2)
            return Array.Empty<SegmentInfo>();

        allMergeable.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
        int takeCount = Math.Min(allMergeable.Count, _mergeThreshold);
        var result = new SegmentInfo[takeCount];
        for (int i = 0; i < takeCount; i++)
            result[i] = allMergeable[i];
        return result;
    }

    private static int GetSizeTier(int docCount)
    {
        if (docCount <= 0) return 0;
        return (int)Math.Log10(Math.Max(1, docCount));
    }
}
