using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Computes numeric aggregations over matching documents using numeric doc values.
/// Operates per-segment for cache-friendliness.
/// </summary>

public static class NumericAggregator
{
    /// <summary>Maximum number of histogram buckets. Prevents OOM from malicious or misconfigured requests.</summary>
    internal const int MaxBucketCount = 100_000;

    /// <summary>
    /// Computes all requested aggregations over the given matching document IDs.
    /// </summary>
    /// <param name="matchingDocs">Global document IDs that matched the query.</param>
    /// <param name="requests">Aggregation requests to compute.</param>
    /// <param name="readers">Segment readers.</param>
    /// <param name="docBases">Per-segment document base offsets.</param>
    /// <param name="totalDocCount">Total number of documents across all segments.</param>
    public static AggregationResult[] Aggregate(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest[] requests,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        int[] docBases,
        int totalDocCount)
    {
        return AggregateCore(matchingDocs, requests, readers, docBases, totalDocCount);
    }

    /// <summary>
    /// Computes all requested aggregations directly from search result documents.
    /// Avoids the intermediate <see cref="HashSet{T}"/> and <c>int[]</c> allocation
    /// that the <c>ReadOnlySpan&lt;int&gt;</c> overload requires callers to build.
    /// </summary>
    /// <param name="matchingDocs">ScoreDocs from the search — doc IDs are extracted in-place.</param>
    /// <param name="requests">Aggregation requests to compute.</param>
    /// <param name="readers">Segment readers.</param>
    /// <param name="docBases">Per-segment document base offsets.</param>
    /// <param name="totalDocCount">Total number of documents across all segments.</param>
    public static AggregationResult[] Aggregate(
        ReadOnlySpan<ScoreDoc> matchingDocs,
        AggregationRequest[] requests,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        int[] docBases,
        int totalDocCount)
    {
        // Extract doc IDs onto the stack for the shared implementation.
        Span<int> docIds = stackalloc int[Math.Min(matchingDocs.Length, 4096)];
        if (matchingDocs.Length > docIds.Length)
        {
            // Fall back to heap allocation only for very large result sets.
            var rented = System.Buffers.ArrayPool<int>.Shared.Rent(matchingDocs.Length);
            var heapSpan = rented.AsSpan(0, matchingDocs.Length);
            ExtractDocIds(matchingDocs, heapSpan);
            var result = AggregateCore(heapSpan, requests, readers, docBases, totalDocCount);
            System.Buffers.ArrayPool<int>.Shared.Return(rented);
            return result;
        }

        ExtractDocIds(matchingDocs, docIds);
        return AggregateCore(docIds, requests, readers, docBases, totalDocCount);
    }

    private static void ExtractDocIds(ReadOnlySpan<ScoreDoc> scoreDocs, Span<int> docIds)
    {
        for (int i = 0; i < scoreDocs.Length; i++)
            docIds[i] = scoreDocs[i].DocId;
    }

    private static AggregationResult[] AggregateCore(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest[] requests,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        int[] docBases,
        int totalDocCount)
    {
        var results = new AggregationResult[requests.Length];

        // Pre-compute segment boundaries for O(1) reader resolution.
        // Each entry: (maxGlobalDocId, readerIdx, docBase).
        // The last segment has max = int.MaxValue as a sentinel.
        var segments = new (int MaxGlobal, int ReaderIdx, int DocBase)[docBases.Length];
        for (int s = 0; s < docBases.Length; s++)
        {
            int maxGlobal = s + 1 < docBases.Length ? docBases[s + 1] - 1 : int.MaxValue;
            segments[s] = (maxGlobal, s, docBases[s]);
        }

        // Pre-resolve field access strategy once per request.
        var fieldAccessors = new FieldAccessor[requests.Length];
        for (int r = 0; r < requests.Length; r++)
            fieldAccessors[r] = ResolveFieldAccessor(requests[r].Field, readers);

        for (int r = 0; r < requests.Length; r++)
        {
            var req = requests[r];
            results[r] = req.Type switch
            {
                AggregationType.Stats => ComputeStats(matchingDocs, req, readers, segments, fieldAccessors[r]),
                AggregationType.Histogram => ComputeHistogram(matchingDocs, req, readers, segments, fieldAccessors[r]),
                _ => AggregationResult.Empty(req.Name, req.Field)
            };
        }

        return results;
    }



    private readonly record struct FieldAccessor(
        bool IsInt64,
        bool IsSortedNumeric,
        bool IsSingleNumeric);

    private static FieldAccessor ResolveFieldAccessor(
        string fieldName,
        IReadOnlyList<Index.Segment.SegmentReader> readers)
    {
        // Determine the field type from the first segment that contains the field,
        // regardless of which documents have values.
        foreach (var reader in readers)
        {
            if (!reader.HasNumericField(fieldName))
                continue;

            // Sorted-numeric takes priority — it's the multi-value form.
            if (reader.GetSortedNumericDocValues(fieldName) is not null)
                return new FieldAccessor(IsInt64: false, IsSortedNumeric: true, IsSingleNumeric: false);
            if (reader.GetSortedInt64DocValues(fieldName) is not null)
                return new FieldAccessor(IsInt64: true, IsSortedNumeric: true, IsSingleNumeric: false);

            // Either sparse .num/.numl index or dense .dvn/.dvnl array.
            if (reader.GetInt64DocValues(fieldName) is not null || reader.GetNumericDocValues(fieldName) is null)
                return new FieldAccessor(IsInt64: true, IsSortedNumeric: false, IsSingleNumeric: true);
            return new FieldAccessor(IsInt64: false, IsSortedNumeric: false, IsSingleNumeric: true);
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int ReaderIdx, int LocalDocId) ResolveDoc(
        int globalDocId, (int MaxGlobal, int ReaderIdx, int DocBase)[] segments)
    {
        // Linear scan over segments (typically 1-4). The sentinel on the last
        // segment guarantees a match.
        for (int s = 0; s < segments.Length; s++)
        {
            if (globalDocId <= segments[s].MaxGlobal)
                return (segments[s].ReaderIdx, globalDocId - segments[s].DocBase);
        }
        return (0, globalDocId);
    }

    private static AggregationResult ComputeStats(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest req,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        (int MaxGlobal, int ReaderIdx, int DocBase)[] segments,
        FieldAccessor accessor)
    {
        long count = 0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double sum = 0;

        foreach (int globalDocId in matchingDocs)
        {
            var (readerIdx, localDocId) = ResolveDoc(globalDocId, segments);
            var reader = readers[readerIdx];

            if (accessor.IsInt64)
            {
                if (accessor.IsSortedNumeric)
                {
                    if (reader.TryGetSortedInt64DocValues(req.Field, localDocId, out var values))
                    {
                        foreach (long value in values)
                        {
                            count++;
                            double d = value;
                            if (d < min) min = d;
                            if (d > max) max = d;
                            sum += d;
                        }
                    }
                }
                else if (accessor.IsSingleNumeric)
                {
                    if (reader.TryGetInt64Value(req.Field, localDocId, out long value))
                    {
                        count++;
                        double d = value;
                        if (d < min) min = d;
                        if (d > max) max = d;
                        sum += d;
                    }
                }
            }
            else if (accessor.IsSortedNumeric)
            {
                if (reader.TryGetSortedNumericDocValues(req.Field, localDocId, out var values))
                {
                    foreach (double value in values)
                    {
                        count++;
                        if (value < min) min = value;
                        if (value > max) max = value;
                        sum += value;
                    }
                }
            }
            else if (accessor.IsSingleNumeric)
            {
                if (reader.TryGetNumericValue(req.Field, localDocId, out double value))
                {
                    count++;
                    if (value < min) min = value;
                    if (value > max) max = value;
                    sum += value;
                }
            }
        }

        return new AggregationResult
        {
            Name = req.Name,
            Field = req.Field,
            Count = count,
            Min = count > 0 ? min : 0,
            Max = count > 0 ? max : 0,
            Sum = sum
        };
    }

    private static AggregationResult ComputeHistogram(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest req,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        (int MaxGlobal, int ReaderIdx, int DocBase)[] segments,
        FieldAccessor accessor)
    {
        double interval = req.HistogramInterval;
        if (interval <= 0) interval = 10.0;

        // First pass: collect all values and find range.
        var values = new List<double>(matchingDocs.Length);
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double sum = 0;

        foreach (int globalDocId in matchingDocs)
        {
            var (readerIdx, localDocId) = ResolveDoc(globalDocId, segments);
            var reader = readers[readerIdx];

            if (accessor.IsInt64)
            {
                if (accessor.IsSortedNumeric)
                {
                    if (reader.TryGetSortedInt64DocValues(req.Field, localDocId, out var docValues))
                    {
                        foreach (long value in docValues)
                        {
                            double d = value;
                            values.Add(d);
                            if (d < min) min = d;
                            if (d > max) max = d;
                            sum += d;
                        }
                    }
                }
                else if (accessor.IsSingleNumeric)
                {
                    if (reader.TryGetInt64Value(req.Field, localDocId, out long value))
                    {
                        double d = value;
                        values.Add(d);
                        if (d < min) min = d;
                        if (d > max) max = d;
                        sum += d;
                    }
                }
            }
            else if (accessor.IsSortedNumeric)
            {
                if (reader.TryGetSortedNumericDocValues(req.Field, localDocId, out var docValues))
                {
                    foreach (double value in docValues)
                    {
                        values.Add(value);
                        if (value < min) min = value;
                        if (value > max) max = value;
                        sum += value;
                    }
                }
            }
            else if (accessor.IsSingleNumeric)
            {
                if (reader.TryGetNumericValue(req.Field, localDocId, out double value))
                {
                    values.Add(value);
                    if (value < min) min = value;
                    if (value > max) max = value;
                    sum += value;
                }
            }
        }

        if (values.Count == 0)
            return AggregationResult.Empty(req.Name, req.Field);

        // Build histogram buckets.
        double bucketStart = Math.Floor(min / interval) * interval;
        double rawBuckets = (max - bucketStart) / interval;
        int bucketCount = double.IsNaN(rawBuckets) || double.IsInfinity(rawBuckets) || rawBuckets > MaxBucketCount
            ? MaxBucketCount
            : Math.Max(1, (int)Math.Ceiling(rawBuckets) + 1);
        var bucketCounts = new long[bucketCount];

        foreach (double v in values)
        {
            int idx = (int)((v - bucketStart) / interval);
            idx = Math.Clamp(idx, 0, bucketCount - 1);
            bucketCounts[idx]++;
        }

        var buckets = new HistogramBucket[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            double lo = bucketStart + i * interval;
            buckets[i] = new HistogramBucket(lo, lo + interval, bucketCounts[i]);
        }

        return new AggregationResult
        {
            Name = req.Name,
            Field = req.Field,
            Count = values.Count,
            Min = min,
            Max = max,
            Sum = sum,
            Buckets = buckets
        };
    }
}
