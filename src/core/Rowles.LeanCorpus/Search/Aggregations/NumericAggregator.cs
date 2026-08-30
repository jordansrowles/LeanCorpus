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
        var fieldAccessors = new NumericFieldAccessor[requests.Length];
        for (int r = 0; r < requests.Length; r++)
            fieldAccessors[r] = NumericFieldValues.ResolveFieldAccessor(requests[r].Field, readers);

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
        NumericFieldAccessor accessor)
    {
        long count = 0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double sum = 0;

        foreach (int globalDocId in matchingDocs)
        {
            var (readerIdx, localDocId) = ResolveDoc(globalDocId, segments);
            var reader = readers[readerIdx];

            if (!NumericFieldValues.TryRead(reader, req.Field, localDocId, accessor, out var values))
                continue;

            if (values.IsInt64)
            {
                if (values.Int64Values is not null)
                {
                    foreach (long value in values.Int64Values)
                        AddStatsValue(value, ref count, ref min, ref max, ref sum);
                }
                else
                {
                    AddStatsValue(values.Int64Value, ref count, ref min, ref max, ref sum);
                }
            }
            else if (values.DoubleValues is not null)
            {
                foreach (double value in values.DoubleValues)
                    AddStatsValue(value, ref count, ref min, ref max, ref sum);
            }
            else
            {
                AddStatsValue(values.DoubleValue, ref count, ref min, ref max, ref sum);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddStatsValue(
        double value,
        ref long count,
        ref double min,
        ref double max,
        ref double sum)
    {
        count++;

        // NumericField accepts all IEEE-754 double values. Count every observed
        // value, and make NaN propagation explicit instead of silently ignoring
        // it in the extrema comparisons.
        if (double.IsNaN(value))
        {
            min = double.NaN;
            max = double.NaN;
        }
        else if (!double.IsNaN(min))
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        sum += value;
    }

    private static AggregationResult ComputeHistogram(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest req,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        (int MaxGlobal, int ReaderIdx, int DocBase)[] segments,
        NumericFieldAccessor accessor)
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

            if (!NumericFieldValues.TryRead(reader, req.Field, localDocId, accessor, out var documentValues))
                continue;

            if (documentValues.IsInt64)
            {
                if (documentValues.Int64Values is not null)
                {
                    foreach (long value in documentValues.Int64Values)
                        AddHistogramValue(value, values, ref min, ref max, ref sum);
                }
                else
                {
                    AddHistogramValue(documentValues.Int64Value, values, ref min, ref max, ref sum);
                }
            }
            else if (documentValues.DoubleValues is not null)
            {
                foreach (double value in documentValues.DoubleValues)
                    AddHistogramValue(value, values, ref min, ref max, ref sum);
            }
            else
            {
                AddHistogramValue(documentValues.DoubleValue, values, ref min, ref max, ref sum);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddHistogramValue(
        double value,
        List<double> values,
        ref double min,
        ref double max,
        ref double sum)
    {
        values.Add(value);
        if (value < min) min = value;
        if (value > max) max = value;
        sum += value;
    }
}
