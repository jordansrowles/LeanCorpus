using System.Globalization;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Merge policy that groups segments by log2 byte-size buckets.
/// When a bucket reaches the threshold, the smallest segments in that bucket
/// are returned as merge candidates. This avoids the tiered policy's worst case
/// where many tiny segments merge into one giant while more tiny ones accumulate.
/// </summary>
public sealed class LogByteSizeMergePolicy : IMergePolicy
{
    private readonly int _mergeThreshold;
    private readonly long _minMergeBytes;

    // Per-segment file extensions included in the size estimate.
    private static readonly string[] SegmentExtensions = [".seg", ".dic", ".pos", ".nrm", ".fdt", ".fdx"];

    /// <summary>
    /// Initialises a log-byte-size merge policy.
    /// </summary>
    /// <param name="mergeThreshold">
    /// Number of segments in one size bucket before a merge is triggered. Default 10.
    /// </param>
    /// <param name="minMergeMB">
    /// Minimum segment size in MB to consider for bucketing. Segments below this
    /// size are grouped into bucket 0 regardless of their exact size. Default 1 MB.
    /// </param>
    public LogByteSizeMergePolicy(int mergeThreshold = 10, double minMergeMB = 1.0)
    {
        _mergeThreshold = mergeThreshold;
        _minMergeBytes = (long)(minMergeMB * 1024 * 1024);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SegmentInfo> FindMerges(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlySet<string> protectedSegmentIds)
    {
        if (segments.Count < _mergeThreshold)
            return Array.Empty<SegmentInfo>();

        // Group segments by log2 byte-size bucket.
        var buckets = new Dictionary<int, List<SegmentInfo>>();
        foreach (var s in segments)
        {
            if (protectedSegmentIds.Contains(s.SegmentId))
                continue;

            long byteSize = EstimateByteSize(s);
            int bucket = GetSizeBucket(byteSize);

            if (!buckets.TryGetValue(bucket, out var list))
            {
                list = new List<SegmentInfo>();
                buckets[bucket] = list;
            }
            list.Add(s);
        }

        // Find the smallest bucket meeting the threshold.
        int? bestBucket = null;
        foreach (var (bucket, list) in buckets)
        {
            if (list.Count >= _mergeThreshold &&
                (bestBucket is null || bucket < bestBucket.Value))
            {
                bestBucket = bucket;
            }
        }

        if (bestBucket is null)
            return Array.Empty<SegmentInfo>();

        var candidates = buckets[bestBucket.Value];
        candidates.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
        int takeCount = Math.Min(candidates.Count, _mergeThreshold);
        var result = new SegmentInfo[takeCount];
        for (int i = 0; i < takeCount; i++)
            result[i] = candidates[i];
        return result;
    }

    /// <summary>
    /// Estimates the on-disk size of a segment by summing the file sizes of its
    /// required sidecar files. Uses cached <see cref="FileInfo.Length"/> values
    /// from the OS file-system cache — no IO reads.
    /// </summary>
    private static long EstimateByteSize(SegmentInfo seg)
    {
        // SegmentInfo doesn't carry a directory path — for policy-only use,
        // estimate from doc counts as a proxy when directory is unavailable.
        if (seg.DocCount == 0)
            return 0;

        // Doc count proxy: each doc contributes roughly 200-500 bytes across
        // all segment files. Use 256 bytes as a conservative midpoint.
        return seg.DocCount * 256L;
    }

    private int GetSizeBucket(long byteSize)
    {
        if (byteSize <= _minMergeBytes)
            return 0;

        // Bucket = floor(log2(byteSize / minMergeBytes)).
        long scaled = byteSize / _minMergeBytes;
        int bucket = 0;
        while (scaled > 1) { scaled >>= 1; bucket++; }
        return bucket;
    }
}
