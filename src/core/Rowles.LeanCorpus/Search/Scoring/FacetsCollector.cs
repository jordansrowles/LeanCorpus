namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Collects per-field value counts across a result set for faceted navigation.
/// </summary>
public sealed class FacetsCollector
{
    private readonly Dictionary<string, FacetAccumulator> _accumulators = new(StringComparer.Ordinal);
    private readonly bool _includeEmptyResults;

    /// <summary>Initialises a collector that creates accumulators as values are collected.</summary>
    public FacetsCollector()
    {
    }

    /// <summary>Initialises a collector for a fixed set of facet requests.</summary>
    internal FacetsCollector(IReadOnlyList<FacetRequest> requests, bool includeEmptyResults = true)
    {
        ArgumentNullException.ThrowIfNull(requests);
        _includeEmptyResults = includeEmptyResults;
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!_accumulators.TryAdd(request.Field, new FacetAccumulator(request.IncludeMissing)))
                throw new ArgumentException($"A facet request for field '{request.Field}' was already supplied.", nameof(requests));
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
        GetOrCreateAccumulator(field).CollectDocumentValue(documentId, value);
    }

    /// <summary>Records that a matching document has no value for a requested field.</summary>
    internal void CollectMissing(string field, int documentId)
    {
        GetOrCreateAccumulator(field).CollectMissing(documentId);
    }

    /// <summary>Returns the accumulated facet results, sorted by count descending.</summary>
    public IReadOnlyList<FacetResult> GetResults()
    {
        var results = new List<FacetResult>(_accumulators.Count);
        foreach (var (field, accumulator) in _accumulators)
        {
            if (!_includeEmptyResults && accumulator.Counts.Count == 0 && accumulator.MissingCount is null)
                continue;

            // Manual loop avoids LINQ allocation overhead
            var buckets = new List<FacetBucket>(accumulator.Counts.Count);
            foreach (var kvp in accumulator.Counts)
                buckets.Add(new FacetBucket(kvp.Key, kvp.Value));
            buckets.Sort((a, b) => b.Count.CompareTo(a.Count));
            results.Add(new FacetResult(field, buckets, accumulator.Counts.Count, accumulator.MissingCount));
        }
        return results;
    }

    private FacetAccumulator GetOrCreateAccumulator(string field)
    {
        if (_accumulators.TryGetValue(field, out var accumulator))
            return accumulator;

        accumulator = new FacetAccumulator(includeMissing: false);
        _accumulators.Add(field, accumulator);
        return accumulator;
    }

    private sealed class FacetAccumulator
    {
        private readonly bool _includeMissing;
        private readonly Dictionary<string, int> _lastDocumentByValue = new(StringComparer.Ordinal);
        private int? _lastMissingDocumentId;

        public FacetAccumulator(bool includeMissing)
        {
            _includeMissing = includeMissing;
        }

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public int? MissingCount { get; private set; }

        public void Collect(string value)
        {
            Counts.TryGetValue(value, out int current);
            Counts[value] = current + 1;
        }

        public void CollectDocumentValue(int documentId, string value)
        {
            if (_lastDocumentByValue.TryGetValue(value, out int previousDocumentId) && previousDocumentId == documentId)
                return;

            _lastDocumentByValue[value] = documentId;
            Collect(value);
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
