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
        var states = new INumericAggregationState[requests.Length];

        // Pre-compute segment boundaries for O(1) reader resolution.
        // Each entry: (maxGlobalDocId, readerIdx, docBase).
        // The last segment has max = int.MaxValue as a sentinel.
        var segments = new (int MaxGlobal, int ReaderIdx, int DocBase)[docBases.Length];
        for (int s = 0; s < docBases.Length; s++)
        {
            int maxGlobal = s + 1 < docBases.Length ? docBases[s + 1] - 1 : int.MaxValue;
            segments[s] = (maxGlobal, s, docBases[s]);
        }

        // Resolve storage representation and create each aggregation state once.
        var fieldAccessors = new NumericFieldAccessor[requests.Length];
        for (int r = 0; r < requests.Length; r++)
        {
            fieldAccessors[r] = NumericFieldValues.ResolveFieldAccessor(requests[r].Field, readers);
            states[r] = NumericAggregationStateFactory.Create(requests[r]);
        }

        // Traverse matching documents once. Every state receives the same
        // representation-aware values without owning document traversal.
        foreach (int globalDocId in matchingDocs)
        {
            var (readerIdx, localDocId) = ResolveDoc(globalDocId, segments);
            var reader = readers[readerIdx];
            for (int r = 0; r < requests.Length; r++)
            {
                if (NumericFieldValues.TryRead(reader, requests[r].Field, localDocId, fieldAccessors[r], out var values))
                    states[r].Collect(values);
            }
        }

        var results = new AggregationResult[states.Length];
        for (int r = 0; r < states.Length; r++)
            results[r] = states[r].Finish();
        return results;
    }

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

}
