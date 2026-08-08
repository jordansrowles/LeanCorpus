using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Searches an immutable composition of multiple directory snapshots without merging their files.
/// </summary>
public sealed class MultiReader : IDisposable
{
    private readonly IndexSearcher[] _searchers;
    private readonly int[] _docBases;
    private readonly int _totalDocCount;
    private int _disposed;

    /// <summary>
    /// Opens one immutable searcher snapshot per directory in input order.
    /// </summary>
    /// <param name="directories">The directories to compose. Input order defines global document-ID offsets.</param>
    /// <param name="config">Optional searcher configuration applied to every component.</param>
    public MultiReader(IReadOnlyList<Store.MMapDirectory> directories, IndexSearcherConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(directories);
        if (directories.Count == 0)
            throw new ArgumentException("At least one directory is required.", nameof(directories));

        config ??= new IndexSearcherConfig();
        _searchers = new IndexSearcher[directories.Count];
        _docBases = new int[directories.Count];
        int docBase = 0;
        try
        {
            for (int i = 0; i < directories.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(directories[i]);
                var searcher = new IndexSearcher(directories[i], config);
                _searchers[i] = searcher;
                _docBases[i] = docBase;
                docBase = checked(docBase + searcher.Stats.TotalDocCount);
            }
            _totalDocCount = docBase;
        }
        catch
        {
            foreach (var searcher in _searchers)
                searcher?.Dispose();
            throw;
        }
    }

    /// <summary>Gets the number of component snapshots in this composition.</summary>
    public int ReaderCount => _searchers.Length;

    /// <summary>Gets the total number of documents across component snapshots.</summary>
    public int MaxDoc => _totalDocCount;

    /// <summary>Gets the captured commit generation for each component in input order.</summary>
    public IReadOnlyList<int> CommitGenerations
        => _searchers.Select(static searcher => searcher.CommitGeneration).ToArray();

    /// <summary>
    /// Builds an immutable global ordinal map for a sorted DocValues field across the
    /// component snapshots in input order.
    /// </summary>
    /// <param name="fieldName">The sorted or sorted-set DocValues field.</param>
    /// <param name="sortedSet">When true, reads the sorted-set term dictionary.</param>
    /// <returns>A map whose source indexes match the component directory order.</returns>
    public OrdinalMap GetOrdinalMap(string fieldName, bool sortedSet = false)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var sourceTerms = new IReadOnlyList<string>[_searchers.Length];
        for (int i = 0; i < _searchers.Length; i++)
            sourceTerms[i] = _searchers[i].GetOrdinalMap(fieldName, sortedSet).Terms;
        return OrdinalMap.Build(sourceTerms);
    }

    /// <summary>Searches all component snapshots by score.</summary>
    public TopDocs Search(Query query, int topN)
        => Search(query, topN, [SortField.Score]);

    /// <summary>Searches all component snapshots using the supplied sort order.</summary>
    public TopDocs Search(Query query, int topN, params SortField[] sorts)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sorts);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (topN <= 0)
            return TopDocs.Empty;
        if (sorts.Length == 0)
            sorts = [SortField.Score];

        var candidates = CollectCandidates(query, topN, sorts, out int totalHits);
        candidates.Sort((left, right) => Compare(left, right, sorts));
        int count = Math.Min(topN, candidates.Count);
        var results = new ScoreDoc[count];
        for (int i = 0; i < count; i++)
            results[i] = candidates[i].Document;
        return new TopDocs(totalHits, results);
    }

    /// <summary>
    /// Returns the next page after a result from this immutable composition.
    /// </summary>
    public TopDocs SearchAfter(ScoreDoc after, Query query, int topN, params SortField[] sorts)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sorts);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (topN <= 0)
            return TopDocs.Empty;
        if (sorts.Length == 0)
            sorts = [SortField.Score];

        var afterValues = CaptureAfterValues(after, sorts);
        var candidates = CollectCandidates(query, _totalDocCount, sorts, out int totalHits);
        candidates.RemoveAll(candidate => Compare(candidate, after, afterValues, sorts) <= 0);
        candidates.Sort((left, right) => Compare(left, right, sorts));
        int count = Math.Min(topN, candidates.Count);
        var results = new ScoreDoc[count];
        for (int i = 0; i < count; i++)
            results[i] = candidates[i].Document;
        return new TopDocs(totalHits, results);
    }

    /// <summary>Searches and merges facet counts from all component snapshots.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacets(
        Query query, int topN, params string[] facetFields)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(facetFields);
        var results = Search(query, topN);
        var ordinalMaps = new Dictionary<string, OrdinalMap>(StringComparer.Ordinal);
        var ordinalCounts = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        var fallbackCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        for (int i = 0; i < _searchers.Length; i++)
        {
            var component = _searchers[i].SearchWithFacets(query, _searchers[i].Stats.TotalDocCount, facetFields);
            foreach (var facet in component.Facets)
            {
                if (!ordinalMaps.TryGetValue(facet.FieldName, out var ordinalMap))
                {
                    ordinalMap = GetOrdinalMap(facet.FieldName, sortedSet: true);
                    ordinalMaps.Add(facet.FieldName, ordinalMap);
                }

                foreach (var bucket in facet.Buckets)
                {
                    if (ordinalMap.TryGetGlobalOrdinal(i, bucket.Value, out int globalOrdinal))
                    {
                        if (!ordinalCounts.TryGetValue(facet.FieldName, out var fieldCounts))
                            ordinalCounts.Add(facet.FieldName, fieldCounts = new Dictionary<int, int>());
                        fieldCounts[globalOrdinal] = fieldCounts.GetValueOrDefault(globalOrdinal) + bucket.Count;
                    }
                    else
                    {
                        if (!fallbackCounts.TryGetValue(facet.FieldName, out var fieldCounts))
                            fallbackCounts.Add(facet.FieldName, fieldCounts = new Dictionary<string, int>(StringComparer.Ordinal));
                        fieldCounts[bucket.Value] = fieldCounts.GetValueOrDefault(bucket.Value) + bucket.Count;
                    }
                }
            }
        }

        var fieldNames = new HashSet<string>(ordinalCounts.Keys, StringComparer.Ordinal);
        fieldNames.UnionWith(fallbackCounts.Keys);
        var facets = fieldNames.Select(fieldName =>
        {
            var buckets = new List<FacetBucket>();
            if (ordinalCounts.TryGetValue(fieldName, out var byOrdinal)
                && ordinalMaps.TryGetValue(fieldName, out var map))
            {
                foreach (var (ordinal, count) in byOrdinal)
                    buckets.Add(new FacetBucket(map.GetTerm(ordinal), count));
            }
            if (fallbackCounts.TryGetValue(fieldName, out var byValue))
            {
                foreach (var (value, count) in byValue)
                    buckets.Add(new FacetBucket(value, count));
            }
            buckets.Sort(static (left, right) =>
            {
                int comparison = right.Count.CompareTo(left.Count);
                return comparison != 0 ? comparison : string.CompareOrdinal(left.Value, right.Value);
            });
            return new FacetResult(fieldName, buckets);
        }).ToArray();
        return (results, facets);
    }

    /// <summary>Disposes all component searcher snapshots.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        foreach (var searcher in _searchers)
            searcher.Dispose();
    }

    private List<CompositeHit> CollectCandidates(Query query, int topN, IReadOnlyList<SortField> sorts, out int totalHits)
    {
        var candidates = new List<CompositeHit>();
        totalHits = 0;
        for (int i = 0; i < _searchers.Length; i++)
        {
            int componentTopN = Math.Min(topN, _searchers[i].Stats.TotalDocCount);
            var local = _searchers[i].Search(query, componentTopN, sorts.ToArray());
            totalHits = checked(totalHits + local.TotalHits);
            foreach (var document in local.ScoreDocs)
            {
                var global = new ScoreDoc(document.DocId + _docBases[i], document.Score);
                candidates.Add(new CompositeHit(global, CaptureCompositeSortValues(i, document, global, sorts)));
            }
        }
        return candidates;
    }

    private CursorSortValue[] CaptureAfterValues(ScoreDoc after, IReadOnlyList<SortField> sorts)
    {
        int readerIndex = ResolveReader(after.DocId, out int localDocId);
        var local = new ScoreDoc(localDocId, after.Score);
        return CaptureCompositeSortValues(readerIndex, local, after, sorts);
    }

    private CursorSortValue[] CaptureCompositeSortValues(
        int readerIndex,
        ScoreDoc local,
        ScoreDoc global,
        IReadOnlyList<SortField> sorts)
    {
        var values = _searchers[readerIndex].CaptureCursorSortValues(local, sorts);
        for (int i = 0; i < sorts.Count; i++)
        {
            if (sorts[i].Type == SortFieldType.DocId)
                values[i] = CursorSortValue.FromInt64(SortFieldType.DocId, global.DocId);
        }
        return values;
    }

    private int ResolveReader(int globalDocId, out int localDocId)
    {
        if ((uint)globalDocId >= (uint)_totalDocCount)
            throw new ArgumentOutOfRangeException(nameof(globalDocId));
        int low = 0;
        int high = _docBases.Length - 1;
        int result = 0;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            if (_docBases[middle] <= globalDocId)
            {
                result = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        localDocId = globalDocId - _docBases[result];
        return result;
    }

    private static int Compare(CompositeHit left, CompositeHit right, IReadOnlyList<SortField> sorts)
        => Compare(left.Values, left.Document.DocId, right.Values, right.Document.DocId, sorts);

    private static int Compare(CompositeHit left, ScoreDoc right, CursorSortValue[] rightValues, IReadOnlyList<SortField> sorts)
        => Compare(left.Values, left.Document.DocId, rightValues, right.DocId, sorts);

    private static int Compare(
        IReadOnlyList<CursorSortValue> left,
        int leftDocId,
        IReadOnlyList<CursorSortValue> right,
        int rightDocId,
        IReadOnlyList<SortField> sorts)
    {
        for (int i = 0; i < sorts.Count; i++)
        {
            int comparison = sorts[i].Type switch
            {
                SortFieldType.Score or SortFieldType.Numeric => left[i].Numeric.CompareTo(right[i].Numeric),
                SortFieldType.DocId or SortFieldType.Int64 => left[i].Int64.CompareTo(right[i].Int64),
                SortFieldType.String => string.CompareOrdinal(left[i].String, right[i].String),
                _ => throw new NotSupportedException($"Sort type '{sorts[i].Type}' is not supported by MultiReader.")
            };
            if (comparison != 0)
                return sorts[i].Descending ? -comparison : comparison;
        }
        return leftDocId.CompareTo(rightDocId);
    }

    private sealed record CompositeHit(ScoreDoc Document, CursorSortValue[] Values);
}
