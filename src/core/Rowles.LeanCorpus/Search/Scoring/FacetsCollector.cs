namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Collects per-field value counts across a result set for faceted navigation.
/// </summary>
public sealed class FacetsCollector
{
    private readonly List<FacetAccumulator> _accumulators = [];
    private readonly Dictionary<string, FacetAccumulator> _legacyAccumulators = new(StringComparer.Ordinal);
    private readonly bool _includeEmptyResults;

    /// <summary>Initialises a collector that creates accumulators as values are collected.</summary>
    public FacetsCollector()
    {
    }

    /// <summary>Initialises a collector for a fixed set of facet requests.</summary>
    internal FacetsCollector(IReadOnlyList<IFacetRequest> requests, bool includeEmptyResults = true, int maxExactBuckets = 100_000)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _includeEmptyResults = includeEmptyResults;
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            _accumulators.Add(new FacetAccumulator(request, maxExactBuckets));
        }
    }

    /// <summary>Records a facet value hit for a document.</summary>
    internal void Collect(string field, string value)
    {
        GetOrCreateAccumulator(field).Collect(value);
    }

    /// <summary>Records a value for a matching document, deduplicating the value within that document.</summary>
    internal void CollectDocumentValue(string field, int documentId, string value)
    {
        var matches = GetMatchingAccumulators(field);
        foreach (var accumulator in matches)
            accumulator.CollectDocumentValue(documentId, value);
    }

    internal void CollectDocumentValue(int requestIndex, int documentId, string value)
        => _accumulators[requestIndex].CollectDocumentValue(documentId, value);

    /// <summary>Records a sorted DocValues term that has already been deduplicated for its document.</summary>
    internal void CollectFlatDocumentValue(int requestIndex, string value)
        => _accumulators[requestIndex].Collect(value);

    /// <summary>Records a global ordinal for an already deduplicated flat value.</summary>
    internal void CollectFlatDocumentOrdinal(int requestIndex, int globalOrdinal)
        => _accumulators[requestIndex].CollectOrdinal(globalOrdinal);

    /// <summary>Configures a flat request to retain sparse ordinal counts.</summary>
    internal void ConfigureOrdinalFlat(int requestIndex, OrdinalMap ordinalMap)
        => _accumulators[requestIndex].ConfigureOrdinal(ordinalMap);

    /// <summary>Records a UTC date histogram bucket for a matching document.</summary>
    internal void CollectDateHistogramBucket(string field, int documentId, long startUnixMilliseconds, long endUnixMilliseconds)
    {
        var matches = GetMatchingAccumulators(field);
        foreach (var accumulator in matches)
            accumulator.CollectDateHistogramBucket(documentId, startUnixMilliseconds, endUnixMilliseconds);
    }

    internal void CollectDateHistogramBucket(int requestIndex, int documentId, long startUnixMilliseconds, long endUnixMilliseconds)
        => _accumulators[requestIndex].CollectDateHistogramBucket(documentId, startUnixMilliseconds, endUnixMilliseconds);

    /// <summary>Registers a declared bucket even when no matching document contributes to it.</summary>
    internal void RegisterBucket(string field, string value)
    {
        var matches = GetMatchingAccumulators(field);
        foreach (var accumulator in matches)
            accumulator.RegisterBucket(value);
    }

    internal void RegisterBucket(int requestIndex, string value)
        => _accumulators[requestIndex].RegisterBucket(value);

    /// <summary>Records that a matching document has no value for a requested field.</summary>
    internal void CollectMissing(string field, int documentId)
    {
        var matches = GetMatchingAccumulators(field);
        foreach (var accumulator in matches)
            accumulator.CollectMissing(documentId);
    }

    internal void CollectMissing(int requestIndex, int documentId)
        => _accumulators[requestIndex].CollectMissing(documentId);

    /// <summary>Returns the accumulated facet results in each request's order, offset and limit.</summary>
    public IReadOnlyList<FacetResult> GetResults(CancellationToken cancellationToken = default)
    {
        var results = new List<FacetResult>(_accumulators.Count);
        foreach (var accumulator in _accumulators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_includeEmptyResults && accumulator.BucketCount == 0 && accumulator.MissingCount is null)
                continue;

            int totalBucketCount = checked(accumulator.BucketCount + (accumulator.MissingCount is > 0 ? 1 : 0));
            var page = CreatePage(accumulator, totalBucketCount, cancellationToken);
            results.Add(new FacetResult(
                accumulator.Name,
                accumulator.FieldName,
                page,
                totalBucketCount,
                accumulator.MissingCount,
                accumulator.CreateDateHistogramBuckets(page, cancellationToken)));
        }
        return results;
    }

    private static IReadOnlyList<FacetBucket> CreatePage(
        FacetAccumulator accumulator,
        int totalBucketCount,
        CancellationToken cancellationToken)
    {
        if (accumulator.Limit == 0 || accumulator.Offset >= totalBucketCount)
            return [];

        var comparer = FacetBucketHelpers.GetComparer(accumulator.Order);
        if (accumulator.Limit == int.MaxValue)
        {
            var all = new List<FacetBucket>(totalBucketCount);
            foreach (var (value, count) in accumulator.EnumerateBuckets())
            {
                cancellationToken.ThrowIfCancellationRequested();
                all.Add(new FacetBucket(value, count));
            }
            if (accumulator.MissingCount is > 0)
                all.Add(FacetBucket.Missing(accumulator.MissingCount.Value));
            all.Sort(comparer);
            return FacetBucketHelpers.Page(all, accumulator.Offset, accumulator.Limit);
        }

        int capacity = checked(accumulator.Offset + accumulator.Limit);
        var worstFirst = Comparer<FacetBucket>.Create((left, right) => comparer.Compare(right, left));
        var candidates = new PriorityQueue<FacetBucket, FacetBucket>(worstFirst);
        foreach (var (value, count) in accumulator.EnumerateBuckets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Consider(new FacetBucket(value, count), candidates, capacity, comparer);
        }
        if (accumulator.MissingCount is > 0)
            Consider(FacetBucket.Missing(accumulator.MissingCount.Value), candidates, capacity, comparer);

        var selected = new List<FacetBucket>(candidates.Count);
        while (candidates.TryDequeue(out var bucket, out _))
            selected.Add(bucket);
        selected.Sort(comparer);
        return FacetBucketHelpers.Page(selected, accumulator.Offset, accumulator.Limit);
    }

    private static void Consider(
        FacetBucket candidate,
        PriorityQueue<FacetBucket, FacetBucket> candidates,
        int capacity,
        IComparer<FacetBucket> comparer)
    {
        if (candidates.Count < capacity)
        {
            candidates.Enqueue(candidate, candidate);
            return;
        }

        if (comparer.Compare(candidate, candidates.Peek()) < 0)
        {
            candidates.Dequeue();
            candidates.Enqueue(candidate, candidate);
        }
    }

    private FacetAccumulator GetOrCreateAccumulator(string field)
    {
        if (_legacyAccumulators.TryGetValue(field, out var accumulator))
            return accumulator;

        accumulator = new FacetAccumulator(field);
        _legacyAccumulators.Add(field, accumulator);
        _accumulators.Add(accumulator);
        return accumulator;
    }

    private IReadOnlyList<FacetAccumulator> GetMatchingAccumulators(string field)
    {
        var matches = new List<FacetAccumulator>();
        foreach (var accumulator in _accumulators)
        {
            if (string.Equals(accumulator.FieldName, field, StringComparison.Ordinal))
                matches.Add(accumulator);
        }

        return matches.Count > 0 ? matches : [GetOrCreateAccumulator(field)];
    }

    private sealed class FacetAccumulator
    {
        private readonly bool _includeMissing;
        private readonly FacetDocumentValueTracker _valueTracker = new();
        private readonly Dictionary<string, (long Start, long End)> _dateHistogramBoundaries = new(StringComparer.Ordinal);
        private OrdinalMap? _ordinalMap;
        private Dictionary<int, int>? _ordinalCounts;
        private int? _lastMissingDocumentId;

        public FacetAccumulator()
            : this(string.Empty)
        {
        }

        public FacetAccumulator(string fieldName)
        {
            FieldName = fieldName;
            Name = fieldName;
            MaxExactBuckets = 100_000;
            Order = FacetBucketOrder.CountDescending;
            Offset = 0;
            Limit = int.MaxValue;
        }

        public FacetAccumulator(IFacetRequest request, int maxExactBuckets)
        {
            FieldName = request.Field;
            Name = request.Name;
            _includeMissing = request.IncludeMissing;
            Order = request.Order;
            Offset = request.Offset;
            Limit = request.Limit;
            MissingCount = _includeMissing ? 0 : null;
            MaxExactBuckets = maxExactBuckets;
        }

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public int BucketCount => _ordinalCounts?.Count ?? Counts.Count;

        public string FieldName { get; }

        public string Name { get; }

        public FacetBucketOrder Order { get; }

        public int Offset { get; }

        public int Limit { get; }

        public int? MissingCount { get; private set; }

        public int MaxExactBuckets { get; }

        public void Collect(string value)
        {
            if (!Counts.TryGetValue(value, out int current) && Counts.Count >= MaxExactBuckets)
            {
                throw new InvalidOperationException(
                    $"Facet field '{FieldName}' observed more than {MaxExactBuckets} exact buckets.");
            }
            Counts[value] = current + 1;
        }

        public void ConfigureOrdinal(OrdinalMap ordinalMap)
        {
            ArgumentNullException.ThrowIfNull(ordinalMap);
            if (Counts.Count != 0 || _ordinalCounts is not null)
                throw new InvalidOperationException("Ordinal facet storage must be configured before collection.");
            _ordinalMap = ordinalMap;
            _ordinalCounts = new Dictionary<int, int>();
        }

        public void CollectOrdinal(int globalOrdinal)
        {
            if (_ordinalCounts is null)
            {
                Collect(_ordinalMap?.GetTerm(globalOrdinal)
                    ?? throw new InvalidOperationException("Ordinal facet storage is not configured."));
                return;
            }

            if (!_ordinalCounts.TryGetValue(globalOrdinal, out int current) && _ordinalCounts.Count >= MaxExactBuckets)
            {
                throw new InvalidOperationException(
                    $"Facet field '{FieldName}' observed more than {MaxExactBuckets} exact buckets.");
            }
            _ordinalCounts[globalOrdinal] = current + 1;
        }

        public IEnumerable<(string Value, int Count)> EnumerateBuckets()
        {
            if (_ordinalCounts is null)
            {
                foreach (var entry in Counts)
                    yield return (entry.Key, entry.Value);
                yield break;
            }

            foreach (var (ordinal, count) in _ordinalCounts)
                yield return (_ordinalMap!.GetTerm(ordinal), count);
        }

        public void RegisterBucket(string value)
        {
            Counts.TryAdd(value, 0);
        }

        public void CollectDocumentValue(int documentId, string value)
        {
            if (!_valueTracker.MarkSeen(documentId, value))
                return;

            Collect(value);
        }

        public void CollectDateHistogramBucket(int documentId, long startUnixMilliseconds, long endUnixMilliseconds)
        {
            string value = DateTimeOffset.FromUnixTimeMilliseconds(startUnixMilliseconds).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            _dateHistogramBoundaries.TryAdd(value, (startUnixMilliseconds, endUnixMilliseconds));
            CollectDocumentValue(documentId, value);
        }

        public IReadOnlyList<DateHistogramBucket>? CreateDateHistogramBuckets(
            IReadOnlyList<FacetBucket> page,
            CancellationToken cancellationToken)
        {
            if (_dateHistogramBoundaries.Count == 0)
                return null;

            var result = new List<DateHistogramBucket>(page.Count);
            foreach (var bucket in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_dateHistogramBoundaries.TryGetValue(bucket.Value, out var boundary))
                {
                    result.Add(new DateHistogramBucket(
                        DateTimeOffset.FromUnixTimeMilliseconds(boundary.Start),
                        DateTimeOffset.FromUnixTimeMilliseconds(boundary.End),
                        bucket.Count));
                }
            }
            return result;
        }

        public void CollectMissing(int documentId)
        {
            if (!_includeMissing)
                return;
            if (_lastMissingDocumentId == documentId)
                return;

            _lastMissingDocumentId = documentId;
            MissingCount = (MissingCount ?? 0) + 1;
        }
    }
}
