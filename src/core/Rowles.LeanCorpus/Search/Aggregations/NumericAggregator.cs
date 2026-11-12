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

    /// <summary>Creates request-local streaming aggregation state for one exhaustive search traversal.</summary>
    internal static NumericAggregationCollector CreateCollector(
        AggregationRequest[] requests,
        IReadOnlyList<Index.Segment.SegmentReader> readers)
        => new(requests, readers);

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
            ArgumentNullException.ThrowIfNull(requests[r]);
            requests[r].Validate();
            fieldAccessors[r] = NumericFieldValues.ResolveFieldAccessor(requests[r].Field, readers);
            states[r] = NumericAggregationStateFactory.Create(requests[r]);
        }

        // Traverse matching documents once, in their given order. Every state receives the same
        // representation-aware values without owning document traversal. Matching documents may
        // interleave segments, so each (segment, request) pair keeps its own resolution and the
        // representation check runs once per pair.
        var segmentAccessors = new NumericSegmentAccessor[readers.Count * requests.Length];
        foreach (int globalDocId in matchingDocs)
        {
            var (readerIdx, localDocId) = ResolveDoc(globalDocId, segments);
            var reader = readers[readerIdx];
            int row = readerIdx * requests.Length;
            for (int r = 0; r < requests.Length; r++)
            {
                if (segmentAccessors[row + r].TryRead(reader, requests[r].Field, localDocId, fieldAccessors[r], out var values))
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

/// <summary>Consumes matching documents directly from search execution without retaining their IDs.</summary>
internal sealed class NumericAggregationCollector
{
    private readonly string[] _fields;
    private readonly NumericFieldAccessor[] _accessors;
    private readonly int[][] _requestIndexes;
    private readonly NumericSegmentAccessor[] _segmentAccessors;
    private readonly INumericAggregationState[] _states;

    public NumericAggregationCollector(
        AggregationRequest[] requests,
        IReadOnlyList<Index.Segment.SegmentReader> readers)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _states = new INumericAggregationState[requests.Length];
        // One plan per distinct field, in first-request order.
        var fieldPlans = new Dictionary<string, (NumericFieldAccessor Accessor, List<int> RequestIndexes)>(StringComparer.Ordinal);
        var fieldOrder = new List<string>();
        for (int i = 0; i < requests.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(requests[i]);
            requests[i].Validate();
            _states[i] = NumericAggregationStateFactory.Create(requests[i]);
            if (!fieldPlans.TryGetValue(requests[i].Field, out var plan))
            {
                plan = (NumericFieldValues.ResolveFieldAccessor(requests[i].Field, readers), []);
                fieldPlans.Add(requests[i].Field, plan);
                fieldOrder.Add(requests[i].Field);
            }
            plan.RequestIndexes.Add(i);
        }

        _fields = fieldOrder.ToArray();
        _accessors = new NumericFieldAccessor[_fields.Length];
        _requestIndexes = new int[_fields.Length][];
        _segmentAccessors = new NumericSegmentAccessor[_fields.Length];
        for (int f = 0; f < _fields.Length; f++)
        {
            var plan = fieldPlans[_fields[f]];
            _accessors[f] = plan.Accessor;
            _requestIndexes[f] = plan.RequestIndexes.ToArray();
        }
    }

    public void Collect(Index.Segment.SegmentReader reader, int localDocId)
    {
        for (int f = 0; f < _fields.Length; f++)
        {
            if (!_segmentAccessors[f].TryRead(reader, _fields[f], localDocId, _accessors[f], out var values))
                continue;

            foreach (int requestIndex in _requestIndexes[f])
                _states[requestIndex].Collect(values);
        }
    }

    public AggregationResult[] Finish(CancellationToken cancellationToken = default)
    {
        var results = new AggregationResult[_states.Length];
        for (int i = 0; i < _states.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[i] = _states[i].Finish(cancellationToken);
        }
        return results;
    }
}
