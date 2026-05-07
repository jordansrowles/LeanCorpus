namespace Rowles.LeanLucene.Search.Aggregations;

/// <summary>
/// Computes numeric aggregations over matching documents using numeric doc values.
/// Operates per-segment for cache-friendliness.
/// </summary>
public static class NumericAggregator
{
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
        var results = new AggregationResult[requests.Length];

        for (int r = 0; r < requests.Length; r++)
        {
            var req = requests[r];
            results[r] = req.Type switch
            {
                AggregationType.Stats => ComputeStats(matchingDocs, req, readers, docBases, totalDocCount),
                AggregationType.Histogram => ComputeHistogram(matchingDocs, req, readers, docBases, totalDocCount),
                _ => AggregationResult.Empty(req.Name, req.Field)
            };
        }

        return results;
    }

    private static AggregationResult ComputeStats(
        ReadOnlySpan<int> matchingDocs,
        AggregationRequest req,
        IReadOnlyList<Index.Segment.SegmentReader> readers,
        int[] docBases,
        int totalDocCount)
    {
        long count = 0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double sum = 0;

        foreach (int globalDocId in matchingDocs)
        {
            int readerIdx = ResolveReaderIndex(globalDocId, docBases);
            var reader = readers[readerIdx];
            int localDocId = globalDocId - docBases[readerIdx];

            if (reader.TryGetNumericValue(req.Field, localDocId, out double value))
            {
                count++;
                if (value < min) min = value;
                if (value > max) max = value;
                sum += value;
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
        int[] docBases,
        int totalDocCount)
    {
        double interval = req.HistogramInterval;
        if (interval <= 0) interval = 10.0;

        // First pass: collect all values and find range
        var values = new List<double>(matchingDocs.Length);
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        double sum = 0;

        foreach (int globalDocId in matchingDocs)
        {
            int readerIdx = ResolveReaderIndex(globalDocId, docBases);
            var reader = readers[readerIdx];
            int localDocId = globalDocId - docBases[readerIdx];

            if (reader.TryGetNumericValue(req.Field, localDocId, out double value))
            {
                values.Add(value);
                if (value < min) min = value;
                if (value > max) max = value;
                sum += value;
            }
        }

        if (values.Count == 0)
            return AggregationResult.Empty(req.Name, req.Field);

        // Build histogram buckets
        double bucketStart = Math.Floor(min / interval) * interval;
        int bucketCount = Math.Max(1, (int)Math.Ceiling((max - bucketStart) / interval) + 1);
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

    private static int ResolveReaderIndex(int globalDocId, int[] docBases)
    {
        for (int i = docBases.Length - 1; i >= 0; i--)
        {
            if (globalDocId >= docBases[i])
                return i;
        }
        return 0;
    }
}
