using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing sorting functionality for search results.
/// </summary>
public sealed partial class IndexSearcher
{
    internal enum SearchExecutionCheckpoint
    {
        BeforeQueryRewrite,
        AfterQueryRewrite,
        BeforePrecompute,
        AfterPrecompute,
        BeforeSegment,
        AfterSegment,
        BeforeFilterBitmap,
        AfterFilterBitmap,
        BeforeSpatialTraversal
    }

    internal Action<SearchExecutionCheckpoint>? SearchExecutionCheckpointForTesting { get; set; }
    internal bool EnableSortedSearchDiagnosticsForTesting { get; set; }

    private int _lastSortedSearchPeakCandidateCountForTesting;

    internal int LastSortedSearchPeakCandidateCountForTesting
        => System.Threading.Volatile.Read(ref _lastSortedSearchPeakCandidateCountForTesting);

    private void ResetSortedSearchPeakCandidateCountForTesting()
    {
        if (EnableSortedSearchDiagnosticsForTesting)
            System.Threading.Interlocked.Exchange(ref _lastSortedSearchPeakCandidateCountForTesting, 0);
    }

    private void RecordSortedSearchPeakCandidateCountForTesting(int candidateCount)
    {
        if (!EnableSortedSearchDiagnosticsForTesting)
            return;

        int current;
        while ((current = System.Threading.Volatile.Read(ref _lastSortedSearchPeakCandidateCountForTesting)) < candidateCount)
        {
            if (System.Threading.Interlocked.CompareExchange(
                    ref _lastSortedSearchPeakCandidateCountForTesting,
                    candidateCount,
                    current) == current)
                return;
        }
    }

    /// <summary>Searches using an ordered list of sort fields.</summary>
    public TopDocs Search(Query query, int topN, params SortField[] sorts)
        => Search(query, topN, (IReadOnlyList<SortField>)sorts, SearchOptions.Default);

    /// <summary>Searches using an ordered list of sort fields and resource controls.</summary>
    public TopDocs Search(
        Query query,
        int topN,
        IReadOnlyList<SortField> sorts,
        SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sorts);
        if (sorts.Count == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(sorts));
        if (sorts.Count == 1)
            return Search(query, topN, sorts[0], options);
        if (topN <= 0)
            return TopDocs.Empty;

        ArgumentNullException.ThrowIfNull(options);
        ValidateSortedTopNBudget(topN, options);
        ResetSortedSearchPeakCandidateCountForTesting();

        var budget = new SearchExecutionBudget(this, options);
        if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeQueryRewrite))
            return budget.EmptyPartialResult();

        Query rewrittenQuery = RewriteQuery(query);
        if (budget.Checkpoint(SearchExecutionCheckpoint.AfterQueryRewrite))
            return budget.EmptyPartialResult();

        var strategy = new FieldSortCollectorStrategy(this, topN, sorts);
        return SearchWithBudgetedCollector(rewrittenQuery, strategy, budget);
    }

    /// <summary>
    /// Returns the next page after a result from the same searcher snapshot.
    /// </summary>
    public TopDocs SearchAfter(
        ScoreDoc after,
        Query query,
        int topN,
        params SortField[] sorts)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sorts);
        if (topN <= 0)
            return TopDocs.Empty;
        if ((uint)after.DocId >= (uint)_totalDocCount)
            throw new ArgumentOutOfRangeException(
                nameof(after),
                "The search-after document is outside this searcher snapshot.");
        if (sorts.Length == 0)
            sorts = [SortField.Score];

        ITopNCollectorStrategy strategy = sorts.Length == 1
            && sorts[0].Type == SortFieldType.Score
            && sorts[0].Descending
            ? new ScoreAfterCollectorStrategy(after, topN)
            : new FieldAfterCollectorStrategy(this, after, topN, sorts);
        return SearchWithCollectorStrategy(query, strategy);
    }

    /// <summary>
    /// Returns the next page after explicit typed sort values.
    /// </summary>
    /// <remarks>
    /// This overload allows callers to retain a product-level stable tie-break
    /// instead of using the internal document ID as the cursor boundary.
    /// </remarks>
    public TopDocs SearchAfter(
        IReadOnlyList<SearchAfterValue> afterValues,
        Query query,
        int topN,
        IReadOnlyList<SortField> sorts)
    {
        ArgumentNullException.ThrowIfNull(afterValues);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sorts);
        if (sorts.Count == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(sorts));
        if (afterValues.Count != sorts.Count)
            throw new ArgumentException("The search-after value count must match the sort field count.", nameof(afterValues));
        if (topN <= 0)
            return TopDocs.Empty;

        for (int i = 0; i < sorts.Count; i++)
        {
            SearchAfterValue value = afterValues[i];
            SortField sort = sorts[i];
            if (value.Type != sort.Type)
                throw new ArgumentException($"Search-after value {i} has type '{value.Type}', expected '{sort.Type}'.", nameof(afterValues));
            bool spatialType = value.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance;
            if (value.IsMissing && !spatialType)
                throw new ArgumentException("Only spatial distance boundaries can be missing.", nameof(afterValues));
            if ((value.Type is SortFieldType.Score or SortFieldType.Numeric && !double.IsFinite(value.NumericValue))
                || (spatialType && !value.IsMissing && !double.IsFinite(value.NumericValue)))
                throw new ArgumentException("Search-after numeric values must be finite.", nameof(afterValues));
            if (value.Type == SortFieldType.String && value.StringValue is null)
                throw new ArgumentException("Search-after string values cannot be null.", nameof(afterValues));
        }

        var strategy = new FieldAfterCollectorStrategy(this, afterValues, topN, sorts);
        return SearchWithCollectorStrategy(query, strategy);
    }

    /// <summary>Captures the typed sort values for a result boundary.</summary>
    public SearchAfterValue[] CaptureSortValues(ScoreDoc document, IReadOnlyList<SortField> sorts)
    {
        ArgumentNullException.ThrowIfNull(sorts);
        CursorSortValue[] values = CaptureCursorSortValues(document, sorts);
        var result = new SearchAfterValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = values[i].Type switch
            {
                SortFieldType.Score or SortFieldType.Numeric => SearchAfterValue.FromNumeric(values[i].Type, values[i].Numeric),
                SortFieldType.GeoDistance or SortFieldType.XYDistance => values[i].IsMissing
                    ? SearchAfterValue.FromMissingNumeric(values[i].Type)
                    : SearchAfterValue.FromNumeric(values[i].Type, values[i].Numeric),
                SortFieldType.DocId or SortFieldType.Int64 => SearchAfterValue.FromInt64(values[i].Type, values[i].Int64),
                SortFieldType.String => SearchAfterValue.FromString(values[i].String ?? string.Empty),
                _ => throw new NotSupportedException($"Sort type '{values[i].Type}' is not cursor-compatible.")
            };
        }

        return result;
    }

    /// <summary>
    /// Searches with a custom sort order instead of relevance ranking.
    /// Matching documents are collected, then a heap-select picks the top-N
    /// by the requested field without performing a full sort over every match.
    /// </summary>
    public TopDocs Search(Query query, int topN, SortField sort)
        => Search(query, topN, sort, SearchOptions.Default);

    /// <summary>
    /// Searches with a custom sort order and resource controls.
    /// Honours <see cref="SearchOptions.Timeout"/>, <see cref="SearchOptions.CancellationToken"/>,
    /// and <see cref="SearchOptions.MaxResultBytes"/>.
    /// </summary>
    public TopDocs Search(Query query, int topN, SortField sort, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (sort.Type == SortFieldType.Score)
            return Search(query, topN, options);

        if (topN <= 0)
            return TopDocs.Empty;

        ValidateSortedTopNBudget(topN, options);
        ResetSortedSearchPeakCandidateCountForTesting();

        var budget = new SearchExecutionBudget(this, options);
        if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeQueryRewrite))
            return budget.EmptyPartialResult();

        Query rewrittenQuery = RewriteQuery(query);
        if (budget.Checkpoint(SearchExecutionCheckpoint.AfterQueryRewrite))
            return budget.EmptyPartialResult();

        if (!sort.Descending
            && (sort.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance)
            && TrySearchBestFirstSpatial(rewrittenQuery, topN, sort, budget, out TopDocs nearest))
            return nearest;

        // Fast path: if the sort matches the index sort, iterate postings in doc-ID
        // order (which is sort-key order) and stop after collecting topN live docs.
        if (_readers.Count > 0 && rewrittenQuery is TermQuery tq
            && TryGetIndexSort(out var indexSort) && MatchesSort(sort, indexSort))
        {
            return SearchWithIndexSortEarlyTermination(tq, topN, sort, budget);
        }

        var strategy = new FieldSortCollectorStrategy(this, topN, [sort]);
        return SearchWithBudgetedCollector(rewrittenQuery, strategy, budget);
    }

    private static void ValidateSortedTopNBudget(int topN, SearchOptions options)
    {
        long topNBytes = checked((long)topN * Scoring.ScoreDoc.EstimatedBytes);
        if (topNBytes > options.MaxResultBytes)
            throw new ArgumentException(
                $"MaxResultBytes ({options.MaxResultBytes}) is smaller than the requested top-N heap ({topNBytes} bytes).",
                nameof(options));
    }

    private TopDocs SearchWithBudgetedCollector(
        Query query,
        ITopNCollectorStrategy strategy,
        SearchExecutionBudget budget)
    {
        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

        if (_readers.Count == 0)
            return TopDocs.Empty;

        if (budget.Checkpoint(SearchExecutionCheckpoint.BeforePrecompute))
            return strategy.ToTopDocs().AsPartial();

        // These query families coordinate results across segments themselves. Preserve
        // their existing execution until their coordinators can consume bounded field
        // sort strategies without first materialising their full candidate set.
        if (query is MoreLikeThisQuery or RrfQuery or BlockJoinQuery)
        {
            TopDocs coordinated = SearchCore(query, _totalDocCount);
            foreach (ScoreDoc candidate in coordinated.ScoreDocs)
                strategy.Collect(candidate.DocId, candidate.Score);
            if (strategy is FieldSortCollectorStrategy fieldStrategy)
                fieldStrategy.AddUncollectedHitCount(coordinated.TotalHits - coordinated.ScoreDocs.Length);

            TopDocs coordinatedResult = strategy.ToTopDocs();
            return budget.IsStopped ? coordinatedResult.AsPartial() : coordinatedResult;
        }

        Dictionary<(string Field, string Term), int>? globalDFs = PrecomputeWithResourceChecks(
            query,
            budget.Options,
            budget.Stopwatch,
            budget.DeadlineTicks);
        if (globalDFs is null || budget.Checkpoint(SearchExecutionCheckpoint.AfterPrecompute))
            return strategy.ToTopDocs().AsPartial();

        bool partial = false;
        if (CanSearchSegmentsInParallel()
            && budget.CanRunParallel
            && strategy is IParallelTopNCollectorStrategy parallelStrategy)
        {
            var mergeLock = new Lock();
            Parallel.ForEach(
                _readers,
                new ParallelOptions { MaxDegreeOfParallelism = ResolvedSearchConcurrency },
                reader =>
                {
                    var workerStrategy = parallelStrategy.CreateWorker();
                    var workerCollector = new TopNCollector(workerStrategy);
                    ExecuteQuery(query, reader, globalDFs, ref workerCollector);
                    lock (mergeLock)
                        parallelStrategy.MergeWorker(workerStrategy);
                });
        }
        else
        {
            foreach (SegmentReader reader in _readers)
            {
                if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeSegment))
                {
                    partial = true;
                    break;
                }

                var collector = new TopNCollector(strategy);
                ExecuteQuery(query, reader, globalDFs, ref collector);
                if (budget.Checkpoint(SearchExecutionCheckpoint.AfterSegment))
                {
                    partial = true;
                    break;
                }
            }
        }

        TopDocs result = strategy.ToTopDocs();
        return partial || budget.IsStopped ? result.AsPartial() : result;
    }

    private bool TrySearchBestFirstSpatial(
        Query query,
        int topN,
        SortField sort,
        SearchExecutionBudget budget,
        out TopDocs result)
    {
        result = TopDocs.Empty;
        if (query is not (MatchAllDocsQuery or ConstantScoreQuery)
            || sort.Type is not (SortFieldType.GeoDistance or SortFieldType.XYDistance)
            || (sort.Type == SortFieldType.GeoDistance && sort.GeoOrigin is null)
            || (sort.Type == SortFieldType.XYDistance && sort.XYOrigin is null))
            return false;

        var globalTopN = new SortedSet<SpatialDistanceCandidate>(SpatialDistanceCandidateComparer.Instance);
        ConstantScoreQuery? constantScoreQuery = query as ConstantScoreQuery;
        int totalHits = 0;
        int peakCandidateCount = 0;
        if (budget.Checkpoint(SearchExecutionCheckpoint.BeforePrecompute))
        {
            result = budget.EmptyPartialResult();
            return true;
        }

        Dictionary<(string Field, string Term), int>? globalDFs = constantScoreQuery is null
            ? EmptyGlobalDFs
            : PrecomputeWithResourceChecks(
                constantScoreQuery.Inner,
                budget.Options,
                budget.Stopwatch,
                budget.DeadlineTicks);
        if (globalDFs is null || budget.Checkpoint(SearchExecutionCheckpoint.AfterPrecompute))
        {
            result = budget.EmptyPartialResult();
            return true;
        }

        bool partial = false;
        foreach (SegmentReader reader in _readers)
        {
            if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeSegment))
            {
                partial = true;
                break;
            }

            Util.RoaringBitmap? filterBitmap = null;
            int segmentMatchCount;
            if (constantScoreQuery is null)
            {
                segmentMatchCount = reader.Info.LiveDocCount;
            }
            else
            {
                if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeFilterBitmap))
                {
                    partial = true;
                    break;
                }
                filterBitmap = ExecuteFilterToBitmap(constantScoreQuery.Inner, reader, globalDFs);
                segmentMatchCount = filterBitmap.Cardinality;
                totalHits += segmentMatchCount;
                if (budget.Checkpoint(SearchExecutionCheckpoint.AfterFilterBitmap))
                {
                    partial = true;
                    break;
                }
            }

            if (constantScoreQuery is null)
                totalHits += segmentMatchCount;

            int capacity = Math.Min(topN, segmentMatchCount);
            if (capacity == 0)
            {
                if (budget.Checkpoint(SearchExecutionCheckpoint.AfterSegment))
                {
                    partial = true;
                    break;
                }
                continue;
            }

            float score = constantScoreQuery is null
                ? ((MatchAllDocsQuery)query).Boost
                : constantScoreQuery.ConstantScore * constantScoreQuery.Boost;

            var collector = new SpatialNearestCollector(
                this,
                reader,
                sort,
                capacity,
                score,
                constantScoreQuery?.Field,
                filterBitmap,
                budget);
            SpatialFieldKind expectedKind = sort.Type == SortFieldType.GeoDistance
                ? SpatialFieldKind.GeoPoint
                : SpatialFieldKind.XYPoint;
            bool compatiblePackedField = SpatialPointFieldCompatibility.TryGetCompatiblePackedField(
                reader, sort.FieldName, expectedKind, out PackedBkdFieldMetadata metadata);

            if (compatiblePackedField)
            {
                if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeSpatialTraversal))
                {
                    partial = true;
                    break;
                }
                reader.TraversePackedBkdBestFirst(sort.FieldName, ref collector, out _);
                if (!collector.ShouldStop)
                {
                    if (collector.Count < capacity)
                        collector.CollectAllDocuments(includeMissing: true);
                    else
                        collector.CollectLegacyGeoDocuments(metadata);
                }
            }
            else
            {
                collector.CollectAllDocuments(includeMissing: true);
            }

            collector.MergeInto(globalTopN, topN);
            peakCandidateCount = Math.Max(peakCandidateCount, Math.Max(collector.PeakCandidateCount, globalTopN.Count));
            RecordSortedSearchPeakCandidateCountForTesting(peakCandidateCount);
            if (collector.ShouldStop || budget.Checkpoint(SearchExecutionCheckpoint.AfterSegment))
            {
                partial = true;
                break;
            }
        }

        var sorted = new ScoreDoc[globalTopN.Count];
        int resultIndex = 0;
        foreach (SpatialDistanceCandidate candidate in globalTopN)
            sorted[resultIndex++] = new ScoreDoc(candidate.GlobalDocId, candidate.Score);
        partial |= budget.IsStopped;
        result = new TopDocs(totalHits, sorted, isPartial: partial);
        return true;
    }

    private double ResolveNumeric(int globalId, string fieldName)
        => ResolveNumeric(globalId, fieldName, SortValueSelector.Min);

    internal bool TryResolveNumericValue(int globalId, string fieldName, out double value)
    {
        int readerOrdinal = FindReaderOrdinal(globalId);
        if (readerOrdinal >= 0)
        {
            return _readers[readerOrdinal].TryGetNumericValue(
                fieldName,
                globalId - _docBases[readerOrdinal],
                out value);
        }

        value = 0;
        return false;
    }

    private double ResolveNumeric(
        int globalId,
        string fieldName,
        SortValueSelector selector)
    {
        int readerOrdinal = FindReaderOrdinal(globalId);
        if (readerOrdinal >= 0)
        {
            var reader = _readers[readerOrdinal];
            int localDocId = globalId - _docBases[readerOrdinal];
            if (reader.TryGetSortedNumericDocValues(fieldName, localDocId, out var values)
                && values.Count > 0)
                return selector == SortValueSelector.Max ? values[^1] : values[0];
            if (reader.TryGetNumericValue(fieldName, localDocId, out double value))
                return value;
        }
        var stored = GetStoredFields(globalId, new HashSet<string> { fieldName });
        if (stored.TryGetValue(fieldName, out var sv) && sv.Count > 0
            && double.TryParse(sv[0], System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private long ResolveInt64(int globalId, string fieldName)
        => ResolveInt64(globalId, fieldName, SortValueSelector.Min);

    internal bool TryResolveInt64Value(int globalId, string fieldName, out long value)
    {
        int readerOrdinal = FindReaderOrdinal(globalId);
        if (readerOrdinal >= 0)
        {
            return _readers[readerOrdinal].TryGetInt64Value(
                fieldName,
                globalId - _docBases[readerOrdinal],
                out value);
        }

        value = 0;
        return false;
    }

    private int FindReaderOrdinal(int globalId)
    {
        int low = 0;
        int high = _readers.Count - 1;
        int result = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            if (_docBases[middle] <= globalId)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result >= 0
            && globalId < _docBases[result] + _readers[result].MaxDoc
                ? result
                : -1;
    }

    private long ResolveInt64(
        int globalId,
        string fieldName,
        SortValueSelector selector)
    {
        int readerOrdinal = FindReaderOrdinal(globalId);
        if (readerOrdinal >= 0)
        {
            var reader = _readers[readerOrdinal];
            int localDocId = globalId - _docBases[readerOrdinal];
            if (reader.TryGetSortedInt64DocValues(fieldName, localDocId, out var values)
                && values.Count > 0)
                return selector == SortValueSelector.Max ? values[^1] : values[0];
            if (reader.TryGetInt64Value(fieldName, localDocId, out long value))
                return value;
        }
        var stored = GetStoredFields(globalId, new HashSet<string> { fieldName });
        if (stored.TryGetValue(fieldName, out var sv) && sv.Count > 0
            && long.TryParse(sv[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private string ResolveString(int globalId, string fieldName)
        => ResolveString(globalId, fieldName, SortValueSelector.Min);

    private string ResolveString(
        int globalId,
        string fieldName,
        SortValueSelector selector)
    {
        int readerOrdinal = FindReaderOrdinal(globalId);
        if (readerOrdinal >= 0)
        {
            var reader = _readers[readerOrdinal];
            int localDocId = globalId - _docBases[readerOrdinal];
            if (reader.TryGetSortedDocValue(fieldName, localDocId, out string value))
                return value;
            if (reader.TryGetSortedSetDocValues(fieldName, localDocId, out var values)
                && values.Count > 0)
                return selector == SortValueSelector.Max ? values[^1] : values[0];
            if (reader.TryGetBinaryDocValues(fieldName, localDocId, out var binaryValues)
                && binaryValues.Count > 0)
                return System.Text.Encoding.UTF8.GetString(binaryValues[0]);
        }
        var stored = GetStoredFields(globalId, new HashSet<string> { fieldName });
        if (stored.TryGetValue(fieldName, out var sv) && sv.Count > 0)
            return sv[0];
        return string.Empty;
    }

    private bool TryGetIndexSort(out SortField indexSortField)
    {
        indexSortField = default!;
        if (_readers.Count == 0) return false;

        SortField? commonSort = null;
        foreach (var reader in _readers)
        {
            var fields = reader.Info.IndexSortFields;
            if (fields is not { Count: 1 }
                || !IndexSort.TryParseSerialisedField(fields[0], out var readerSort))
            {
                return false;
            }

            // Legacy segments may advertise DocId ordering, but that pre-flush key is not
            // persisted through physical reordering. It cannot safely justify early termination.
            if (readerSort.Type == SortFieldType.DocId)
                return false;

            if (commonSort is not null && !MatchesSort(commonSort, readerSort))
                return false;

            commonSort = readerSort;
        }

        indexSortField = commonSort!;
        return true;
    }

    private static bool MatchesSort(SortField a, SortField b)
        => a.Type == b.Type && a.FieldName == b.FieldName
            && a.Descending == b.Descending && a.Selector == b.Selector
            && a.GeoOrigin == b.GeoOrigin && a.XYOrigin == b.XYOrigin;

    private SortColumn BuildSortColumn(ScoreDoc[] docs, SortField field)
    {
        var column = new SortColumn(field, docs.Length);
        for (int i = 0; i < docs.Length; i++)
        {
            switch (field.Type)
            {
                case SortFieldType.Score:
                    column.NumericValues![i] = docs[i].Score;
                    break;
                case SortFieldType.DocId:
                    column.Int64Values![i] = docs[i].DocId;
                    break;
                case SortFieldType.Numeric:
                    column.NumericValues![i] = ResolveNumeric(
                        docs[i].DocId, field.FieldName, field.Selector);
                    break;
                case SortFieldType.Int64:
                    column.Int64Values![i] = ResolveInt64(
                        docs[i].DocId, field.FieldName, field.Selector);
                    break;
                case SortFieldType.String:
                    column.StringValues![i] = ResolveString(
                        docs[i].DocId, field.FieldName, field.Selector);
                    break;
                case SortFieldType.GeoDistance:
                case SortFieldType.XYDistance:
                    bool hasDistance = TryResolveSpatialDistance(docs[i].DocId, field, out double distance);
                    column.NumericValues![i] = hasDistance ? distance : 0;
                    column.MissingValues![i] = !hasDistance;
                    break;
            }
        }
        return column;
    }

    internal CursorSortValue[] CaptureCursorSortValues(ScoreDoc document, IReadOnlyList<SortField> sorts)
    {
        var values = new CursorSortValue[sorts.Count];
        for (int i = 0; i < sorts.Count; i++)
        {
            var sort = sorts[i];
            values[i] = sort.Type switch
            {
                SortFieldType.Score => CursorSortValue.FromNumeric(sort.Type, document.Score),
                SortFieldType.DocId => CursorSortValue.FromInt64(sort.Type, document.DocId),
                SortFieldType.Numeric => CursorSortValue.FromNumeric(sort.Type, ResolveNumeric(document.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.Int64 => CursorSortValue.FromInt64(sort.Type, ResolveInt64(document.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.String => CursorSortValue.FromString(ResolveString(document.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.GeoDistance or SortFieldType.XYDistance => ResolveSpatialDistance(document.DocId, sort),
                _ => throw new NotSupportedException($"Sort type '{sort.Type}' is not cursor-compatible.")
            };
        }
        return values;
    }

    internal ScoreDoc[] SortCandidates(
        ScoreDoc[] docs,
        IReadOnlyList<SortField> sorts,
        int topN)
    {
        var columns = new SortColumn[sorts.Count];
        for (int i = 0; i < sorts.Count; i++)
            columns[i] = BuildSortColumn(docs, sorts[i]);

        var indices = new int[docs.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        Array.Sort(indices, (left, right) => CompareSortRows(columns, docs, left, right));

        int resultCount = Math.Min(topN, docs.Length);
        var sorted = new ScoreDoc[resultCount];
        for (int i = 0; i < resultCount; i++)
            sorted[i] = docs[indices[i]];
        return sorted;
    }

    private CursorSortValue ResolveSpatialDistance(int globalDocId, SortField sort)
        => TryResolveSpatialDistance(globalDocId, sort, out double distance)
            ? CursorSortValue.FromNumeric(sort.Type, distance)
            : CursorSortValue.FromNumeric(sort.Type, 0, isMissing: true);

    private SortValue ResolveSpatialSortValue(int globalDocId, SortField sort)
        => TryResolveSpatialDistance(globalDocId, sort, out double distance)
            ? SortValue.FromNumeric(distance)
            : SortValue.FromNumeric(0, isMissing: true);

    private bool TryResolveSpatialDistance(int globalDocId, SortField sort, out double distance)
    {
        distance = 0;
        int readerOrdinal = FindReaderOrdinal(globalDocId);
        if (readerOrdinal < 0)
            return false;

        SegmentReader reader = _readers[readerOrdinal];
        int localDocId = globalDocId - _docBases[readerOrdinal];
        if (sort.Type == SortFieldType.GeoDistance)
        {
            SpatialPointFieldResolution resolution = SpatialPointFieldCompatibility.Resolve(reader, sort.FieldName);
            if (resolution is not (SpatialPointFieldResolution.GeoPoint or SpatialPointFieldResolution.LegacyGeo))
                return false;

            Rowles.LeanCorpus.Search.Geo.GeoPoint origin = sort.GeoOrigin
                ?? throw new InvalidOperationException("A geographic distance sort has no origin.");
            string pointValuesField = GeoPointDocValues.GetFieldName(sort.FieldName);
            if (resolution == SpatialPointFieldResolution.GeoPoint
                && reader.TryGetBinaryDocValues(pointValuesField, localDocId, out var values))
            {
                double minimumDistance = double.PositiveInfinity;
                foreach (byte[] value in values)
                {
                    if (!GeoPointDocValues.TryDecode(value, out double latitude, out double longitude))
                        throw new InvalidDataException($"Geo point DocValues for field '{sort.FieldName}' are malformed.");
                    double candidate = GeoEncodingUtils.HaversineDistance(
                        origin.Latitude, origin.Longitude, latitude, longitude);
                    if (candidate < minimumDistance)
                        minimumDistance = candidate;
                }

                if (double.IsFinite(minimumDistance))
                {
                    distance = minimumDistance;
                    return true;
                }
            }

            if (reader.TryGetNumericValue(sort.FieldName + "_lat", localDocId, out double legacyLatitude)
                && reader.TryGetNumericValue(sort.FieldName + "_lon", localDocId, out double legacyLongitude))
            {
                distance = GeoEncodingUtils.HaversineDistance(
                    origin.Latitude, origin.Longitude, legacyLatitude, legacyLongitude);
                return true;
            }

            return false;
        }

        if (sort.Type == SortFieldType.XYDistance)
        {
            if (SpatialPointFieldCompatibility.Resolve(reader, sort.FieldName)
                != SpatialPointFieldResolution.XYPoint)
                return false;

            Rowles.LeanCorpus.Search.XY.XYPoint origin = sort.XYOrigin
                ?? throw new InvalidOperationException("A Cartesian distance sort has no origin.");
            if (!reader.TryGetBinaryDocValues(sort.FieldName, localDocId, out var values))
                return false;

            double minimumSquared = double.PositiveInfinity;
            foreach (byte[] value in values)
            {
                if (value.Length != 2 * PackedBkdConfig.FixedBytesPerDimension)
                    throw new InvalidDataException($"XY point DocValues for field '{sort.FieldName}' are malformed.");
                double x = XYEncodingUtils.Decode(value);
                double y = XYEncodingUtils.Decode(value.AsSpan(PackedBkdConfig.FixedBytesPerDimension));
                double dx = x - origin.X;
                double dy = y - origin.Y;
                double squared = dx * dx + dy * dy;
                if (squared < minimumSquared)
                    minimumSquared = squared;
            }

            if (double.IsFinite(minimumSquared))
            {
                distance = Math.Sqrt(minimumSquared);
                return true;
            }
        }

        return false;
    }

    private sealed class SpatialNearestCollector : IPackedBkdBestFirstVisitor
    {
        private readonly IndexSearcher _searcher;
        private readonly SegmentReader _reader;
        private readonly SortField _sort;
        private readonly int _capacity;
        private readonly float _score;
        private readonly string? _scoreField;
        private readonly Util.RoaringBitmap? _filterBitmap;
        private readonly SearchExecutionBudget _budget;
        private readonly SortedSet<SpatialDistanceCandidate> _ordered = new(SpatialDistanceCandidateComparer.Instance);
        private readonly Dictionary<int, SpatialDistanceCandidate> _byGlobalDoc = new();
        private int _peakCandidateCount;
        private long _exactDistanceCalculations;
        private long _filterCandidatesRejected;
        private long _candidateUpdates;

        internal SpatialNearestCollector(
            IndexSearcher searcher,
            SegmentReader reader,
            SortField sort,
            int capacity,
            float score,
            string? scoreField,
            Util.RoaringBitmap? filterBitmap,
            SearchExecutionBudget budget)
        {
            _searcher = searcher;
            _reader = reader;
            _sort = sort;
            _capacity = capacity;
            _score = score;
            _scoreField = scoreField;
            _filterBitmap = filterBitmap;
            _budget = budget;
        }

        internal int Count => _ordered.Count;

        public int CurrentCandidateCount => _ordered.Count;

        public int PeakCandidateCount => _peakCandidateCount;

        public long ExactDistanceCalculations => _exactDistanceCalculations;

        public long FilterCandidatesRejected => _filterCandidatesRejected;

        public long CandidateUpdates => _candidateUpdates;

        public bool ShouldStop => _budget.IsStopped;

        public bool HasFullCandidateSet => _ordered.Count >= _capacity;

        public double WorstCandidateDistance
        {
            get
            {
                SpatialDistanceCandidate worst = _ordered.Max!;
                return _sort.Type == SortFieldType.XYDistance
                    ? worst.Distance * worst.Distance
                    : worst.Distance;
            }
        }

        public double GetLowerBoundDistance(
            uint minimumX,
            uint maximumX,
            uint minimumY,
            uint maximumY)
        {
            if (_sort.Type == SortFieldType.GeoDistance)
            {
                GeoPoint origin = _sort.GeoOrigin!.Value;
                return SpatialDistanceLowerBound.GeoMetres(
                    origin.Latitude,
                    origin.Longitude,
                    minimumX,
                    maximumX,
                    minimumY,
                    maximumY);
            }

            XYPoint xyOrigin = _sort.XYOrigin!.Value;
            return SpatialDistanceLowerBound.XYSquared(
                xyOrigin.X,
                xyOrigin.Y,
                minimumX,
                maximumX,
                minimumY,
                maximumY);
        }

        public void Visit(int documentId, ReadOnlySpan<byte> packedValue)
        {
            if (ShouldStop || !_reader.IsLive(documentId))
                return;
            if (_filterBitmap is not null && !_filterBitmap.Contains(documentId))
            {
                _filterCandidatesRejected++;
                return;
            }
            AddDocument(_reader.DocBase + documentId, includeMissing: false);
        }

        internal void CollectAllDocuments(bool includeMissing)
        {
            for (int localDocId = 0; localDocId < _reader.MaxDoc && !ShouldStop; localDocId++)
            {
                if (!_reader.IsLive(localDocId)
                    || (_filterBitmap is not null && !_filterBitmap.Contains(localDocId)))
                    continue;
                AddDocument(_reader.DocBase + localDocId, includeMissing);
            }
        }

        internal void CollectLegacyGeoDocuments(PackedBkdFieldMetadata metadata)
        {
            if (_sort.Type != SortFieldType.GeoDistance)
                return;
            if (metadata.DocumentCount == _reader.MaxDoc)
                return;

            string latitudeField = _sort.FieldName + "_lat";
            string longitudeField = _sort.FieldName + "_lon";
            if (!_reader.TryGetNumericDocValuesPresence(latitudeField, out var latitudePresence)
                || !_reader.TryGetNumericDocValuesPresence(longitudeField, out var longitudePresence))
                return;

            int latitudeDocumentCount = latitudePresence?.Cardinality ?? _reader.MaxDoc;
            int longitudeDocumentCount = longitudePresence?.Cardinality ?? _reader.MaxDoc;
            if (latitudeDocumentCount <= metadata.DocumentCount
                || longitudeDocumentCount <= metadata.DocumentCount)
                return;

            Util.RoaringBitmap? legacyCandidates = latitudePresence is null
                ? longitudePresence
                : longitudePresence is null
                    ? latitudePresence
                    : Util.RoaringBitmap.And(latitudePresence, longitudePresence);
            if (legacyCandidates is not null && legacyCandidates.Cardinality <= metadata.DocumentCount)
                return;

            string exactField = GeoPointDocValues.GetFieldName(_sort.FieldName);
            if (legacyCandidates is null)
            {
                for (int localDocId = 0; localDocId < _reader.MaxDoc && !ShouldStop; localDocId++)
                    CollectLegacyGeoDocument(localDocId, exactField);
                return;
            }

            foreach (int localDocId in legacyCandidates)
            {
                if (ShouldStop)
                    break;

                CollectLegacyGeoDocument(localDocId, exactField);
            }
        }

        private void CollectLegacyGeoDocument(int localDocId, string exactField)
        {
            if (!_reader.IsLive(localDocId))
                return;
            if (_filterBitmap is not null && !_filterBitmap.Contains(localDocId))
            {
                _filterCandidatesRejected++;
                return;
            }
            if (_reader.HasBinaryDocValue(exactField, localDocId))
                return;

            AddDocument(_reader.DocBase + localDocId, includeMissing: false);
        }

        internal void MergeInto(SortedSet<SpatialDistanceCandidate> globalTopN, int topN)
        {
            foreach (SpatialDistanceCandidate candidate in _ordered)
            {
                if (globalTopN.Count < topN)
                {
                    globalTopN.Add(candidate);
                    continue;
                }

                SpatialDistanceCandidate worst = globalTopN.Max!;
                if (SpatialDistanceCandidateComparer.Instance.Compare(candidate, worst) >= 0)
                    continue;
                globalTopN.Remove(worst);
                globalTopN.Add(candidate);
            }
        }

        private void AddDocument(int globalDocId, bool includeMissing)
        {
            if (_searcher.TryResolveSpatialDistance(globalDocId, _sort, out double distance))
            {
                _exactDistanceCalculations++;
                float score = _scoreField is null
                    ? _score
                    : ApplyFieldBoost(_reader, globalDocId - _reader.DocBase, _scoreField, _score);
                AddCandidate(new SpatialDistanceCandidate(globalDocId, distance, score, IsMissing: false));
            }
            else if (includeMissing)
            {
                float score = _scoreField is null
                    ? _score
                    : ApplyFieldBoost(_reader, globalDocId - _reader.DocBase, _scoreField, _score);
                AddCandidate(new SpatialDistanceCandidate(globalDocId, 0, score, IsMissing: true));
            }
        }

        private void AddCandidate(SpatialDistanceCandidate candidate)
        {
            if (_byGlobalDoc.TryGetValue(candidate.GlobalDocId, out SpatialDistanceCandidate existing))
            {
                if (SpatialDistanceCandidateComparer.Instance.Compare(candidate, existing) >= 0)
                    return;
                _ordered.Remove(existing);
                _byGlobalDoc.Remove(existing.GlobalDocId);
            }

            if (_ordered.Count >= _capacity)
            {
                SpatialDistanceCandidate worst = _ordered.Max!;
                if (SpatialDistanceCandidateComparer.Instance.Compare(candidate, worst) >= 0)
                    return;
                _ordered.Remove(worst);
                _byGlobalDoc.Remove(worst.GlobalDocId);
            }

            _ordered.Add(candidate);
            _byGlobalDoc[candidate.GlobalDocId] = candidate;
            _candidateUpdates++;
            _peakCandidateCount = Math.Max(_peakCandidateCount, _ordered.Count);
        }
    }

    private readonly record struct SpatialDistanceCandidate(int GlobalDocId, double Distance, float Score, bool IsMissing);

    private sealed class SpatialDistanceCandidateComparer : IComparer<SpatialDistanceCandidate>
    {
        internal static readonly SpatialDistanceCandidateComparer Instance = new();

        public int Compare(SpatialDistanceCandidate left, SpatialDistanceCandidate right)
        {
            if (left.GlobalDocId == right.GlobalDocId)
                return 0;
            int missing = left.IsMissing.CompareTo(right.IsMissing);
            if (missing != 0)
                return missing;
            if (!left.IsMissing)
            {
                int distance = left.Distance.CompareTo(right.Distance);
                if (distance != 0)
                    return distance;
            }
            return left.GlobalDocId.CompareTo(right.GlobalDocId);
        }
    }

    private static int CompareSortRows(
        SortColumn[] columns,
        ScoreDoc[] docs,
        int left,
        int right)
    {
        foreach (var column in columns)
        {
            int comparison = column.Compare(left, right);
            if (comparison != 0)
                return comparison;
        }
        return docs[left].DocId.CompareTo(docs[right].DocId);
    }

    private static void FillSortValues(
        IndexSearcher searcher,
        ScoreDoc scoreDoc,
        SortField[] sorts,
        Span<SortValue> destination)
    {
        for (int i = 0; i < sorts.Length; i++)
        {
            SortField sort = sorts[i];
            destination[i] = sort.Type switch
            {
                SortFieldType.Score => SortValue.FromNumeric(scoreDoc.Score),
                SortFieldType.DocId => SortValue.FromInt64(scoreDoc.DocId),
                SortFieldType.Numeric => SortValue.FromNumeric(
                    searcher.ResolveNumeric(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.Int64 => SortValue.FromInt64(
                    searcher.ResolveInt64(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.String => SortValue.FromString(
                    searcher.ResolveString(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                SortFieldType.GeoDistance or SortFieldType.XYDistance =>
                    searcher.ResolveSpatialSortValue(scoreDoc.DocId, sort),
                _ => default
            };
        }
    }

    private static int CompareSortValues(
        SortField[] sorts,
        ReadOnlySpan<SortValue> left,
        int leftDocId,
        ReadOnlySpan<SortValue> right,
        int rightDocId,
        bool includeDocumentIdTieBreak = true)
    {
        for (int i = 0; i < sorts.Length; i++)
        {
            int comparison = CompareSortValue(sorts[i], left[i], right[i]);
            if (comparison != 0)
                return comparison;
        }

        return includeDocumentIdTieBreak ? leftDocId.CompareTo(rightDocId) : 0;
    }

    private static int CompareSortValue(SortField sort, SortValue left, SortValue right)
    {
        if (sort.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance
            && left.Missing != right.Missing)
            return left.Missing ? 1 : -1;

        int comparison = left.CompareTo(right, sort.Type);
        return sort.Descending ? -comparison : comparison;
    }

    private sealed class SortColumn
    {
        private readonly SortField _field;
        internal double[]? NumericValues { get; }
        internal long[]? Int64Values { get; }
        internal string[]? StringValues { get; }
        internal bool[]? MissingValues { get; }

        internal SortColumn(SortField field, int count)
        {
            _field = field;
            if (field.Type is SortFieldType.Score or SortFieldType.Numeric or SortFieldType.GeoDistance or SortFieldType.XYDistance)
                NumericValues = new double[count];
            else if (field.Type is SortFieldType.DocId or SortFieldType.Int64)
                Int64Values = new long[count];
            else
                StringValues = new string[count];
            if (field.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance)
                MissingValues = new bool[count];
        }

        internal int Compare(int left, int right)
            => CompareSortValue(_field, GetValue(left), GetValue(right));

        private SortValue GetValue(int index) => _field.Type switch
        {
            SortFieldType.Score or SortFieldType.Numeric => SortValue.FromNumeric(NumericValues![index]),
            SortFieldType.GeoDistance or SortFieldType.XYDistance =>
                SortValue.FromNumeric(NumericValues![index], MissingValues![index]),
            SortFieldType.DocId or SortFieldType.Int64 => SortValue.FromInt64(Int64Values![index]),
            SortFieldType.String => SortValue.FromString(StringValues![index]),
            _ => default
        };
    }

    private sealed class ScoreAfterCollectorStrategy : ITopNCollectorStrategy, IParallelTopNCollectorStrategy
    {
        private readonly ScoreDoc _after;
        private TopNCollector _collector;
        private int _totalHits;

        internal ScoreAfterCollectorStrategy(ScoreDoc after, int topN)
        {
            _after = after;
            _collector = new TopNCollector(topN);
        }

        public int TotalHits => _totalHits;
        public int Capacity => _collector.Capacity;
        public bool IsFull => _collector.IsFull;
        public float MinScore => _collector.MinScore;

        public void Collect(int docId, float score)
        {
            _totalHits++;
            if (score < _after.Score || (score == _after.Score && docId > _after.DocId))
                _collector.Collect(docId, score);
        }

        public TopDocs ToTopDocs()
        {
            var page = _collector.ToTopDocs();
            return new TopDocs(_totalHits, page.ScoreDocs);
        }

        public void Reset()
        {
            _totalHits = 0;
            _collector.Reset();
        }

        public ITopNCollectorStrategy CreateWorker()
            => new ScoreAfterCollectorStrategy(_after, _collector.Capacity);

        public void MergeWorker(ITopNCollectorStrategy worker)
        {
            var scoreWorker = (ScoreAfterCollectorStrategy)worker;
            _totalHits += scoreWorker._totalHits;
            foreach (var scoreDoc in scoreWorker._collector.ToTopDocs().ScoreDocs)
                _collector.Collect(scoreDoc.DocId, scoreDoc.Score);
        }
    }

    private sealed class FieldSortCollectorStrategy : ITopNCollectorStrategy, IParallelTopNCollectorStrategy
    {
        private readonly IndexSearcher _searcher;
        private readonly SortField[] _sorts;
        private readonly ScoreDoc[] _heap;
        private readonly SortValue[] _heapValues;
        private readonly SortValue[] _candidateValues;
        private int _size;
        private int _totalHits;

        internal FieldSortCollectorStrategy(
            IndexSearcher searcher,
            int topN,
            IReadOnlyList<SortField> sorts)
        {
            _searcher = searcher;
            _sorts = sorts.ToArray();
            _heap = new ScoreDoc[topN];
            _heapValues = new SortValue[checked(topN * sorts.Count)];
            _candidateValues = new SortValue[sorts.Count];
        }

        public int TotalHits => _totalHits;
        public int Capacity => _heap.Length;
        public bool IsFull => _size == _heap.Length;
        public float MinScore => float.NegativeInfinity;
        internal int PeakCandidateCount { get; private set; }

        public void Collect(int docId, float score)
        {
            _totalHits++;
            var candidate = new ScoreDoc(docId, score);
            FillSortValues(_searcher, candidate, _sorts, _candidateValues);
            AddCandidate(candidate, _candidateValues);
        }

        internal void AddUncollectedHitCount(int count)
        {
            if (count > 0)
                _totalHits += count;
        }

        public ITopNCollectorStrategy CreateWorker()
            => new FieldSortCollectorStrategy(_searcher, _heap.Length, _sorts);

        public void MergeWorker(ITopNCollectorStrategy worker)
        {
            var fieldWorker = (FieldSortCollectorStrategy)worker;
            _totalHits += fieldWorker._totalHits;
            PeakCandidateCount = Math.Max(PeakCandidateCount, fieldWorker.PeakCandidateCount);
            for (int i = 0; i < fieldWorker._size; i++)
            {
                ReadOnlySpan<SortValue> values = fieldWorker._heapValues.AsSpan(
                    i * _sorts.Length,
                    _sorts.Length);
                AddCandidate(fieldWorker._heap[i], values);
            }
        }

        public TopDocs ToTopDocs()
        {
            if (_size == 0)
                return new TopDocs(_totalHits, []);

            if (_size < _heap.Length)
                BuildWorstHeap();

            int remaining = _size;
            var results = new ScoreDoc[remaining];
            while (remaining > 0)
            {
                results[remaining - 1] = _heap[0];
                remaining--;
                if (remaining == 0)
                    break;

                _heap[0] = _heap[remaining];
                CopySlot(remaining, 0);
                SiftDown(0, remaining);
            }

            _size = 0;
            return new TopDocs(_totalHits, results);
        }

        public void Reset()
        {
            _size = 0;
            _totalHits = 0;
            PeakCandidateCount = 0;
        }

        private void AddCandidate(ScoreDoc candidate, ReadOnlySpan<SortValue> values)
        {
            if (_size < _heap.Length)
            {
                _heap[_size] = candidate;
                CopyValues(values, _size);
                _size++;
                PeakCandidateCount = Math.Max(PeakCandidateCount, _size);
                _searcher.RecordSortedSearchPeakCandidateCountForTesting(_size);
                if (_size == _heap.Length)
                    BuildWorstHeap();
                return;
            }

            if (CompareCandidateToSlot(candidate.DocId, values, 0) >= 0)
                return;

            _heap[0] = candidate;
            CopyValues(values, 0);
            SiftDown(0, _size);
        }

        private int CompareCandidateToSlot(int candidateDocId, ReadOnlySpan<SortValue> candidateValues, int slot)
        {
            int offset = slot * _sorts.Length;
            return CompareSortValues(
                _sorts,
                candidateValues,
                candidateDocId,
                _heapValues.AsSpan(offset, _sorts.Length),
                _heap[slot].DocId);
        }

        private int CompareSlots(int left, int right)
        {
            int leftOffset = left * _sorts.Length;
            int rightOffset = right * _sorts.Length;
            return CompareSortValues(
                _sorts,
                _heapValues.AsSpan(leftOffset, _sorts.Length),
                _heap[left].DocId,
                _heapValues.AsSpan(rightOffset, _sorts.Length),
                _heap[right].DocId);
        }

        private void CopyValues(ReadOnlySpan<SortValue> source, int slot)
            => source.CopyTo(_heapValues.AsSpan(slot * _sorts.Length, _sorts.Length));

        private void CopySlot(int source, int destination)
            => _heapValues.AsSpan(source * _sorts.Length, _sorts.Length).CopyTo(
                _heapValues.AsSpan(destination * _sorts.Length, _sorts.Length));

        private void BuildWorstHeap()
        {
            for (int i = _size / 2 - 1; i >= 0; i--)
                SiftDown(i, _size);
        }

        private void SiftDown(int index, int size)
        {
            while (true)
            {
                int worst = index;
                int left = (index * 2) + 1;
                int right = left + 1;
                if (left < size && CompareSlots(left, worst) > 0)
                    worst = left;
                if (right < size && CompareSlots(right, worst) > 0)
                    worst = right;
                if (worst == index)
                    return;

                (_heap[index], _heap[worst]) = (_heap[worst], _heap[index]);
                SwapValues(index, worst);
                index = worst;
            }
        }

        private void SwapValues(int left, int right)
        {
            int leftOffset = left * _sorts.Length;
            int rightOffset = right * _sorts.Length;
            for (int i = 0; i < _sorts.Length; i++)
                (_heapValues[leftOffset + i], _heapValues[rightOffset + i]) =
                    (_heapValues[rightOffset + i], _heapValues[leftOffset + i]);
        }
    }

    private sealed class FieldAfterCollectorStrategy : ITopNCollectorStrategy, IParallelTopNCollectorStrategy
    {
        private readonly IndexSearcher _searcher;
        private readonly SortField[] _sorts;
        private readonly ScoreDoc[] _heap;
        private readonly SortValue[] _heapValues;
        private readonly SortValue[] _candidateValues;
        private readonly SortValue[] _afterValues;
        private readonly ScoreDoc _after;
        private readonly SearchAfterValue[]? _explicitAfterValues;
        private int _size;
        private int _totalHits;

        internal FieldAfterCollectorStrategy(
            IndexSearcher searcher,
            ScoreDoc after,
            int topN,
            SortField[] sorts)
        {
            _searcher = searcher;
            _after = after;
            _sorts = sorts.ToArray();
            _heap = new ScoreDoc[topN];
            _heapValues = new SortValue[checked(topN * sorts.Length)];
            _candidateValues = new SortValue[sorts.Length];
            _afterValues = new SortValue[sorts.Length];
            FillValues(after, _afterValues);
        }

        internal FieldAfterCollectorStrategy(
            IndexSearcher searcher,
            IReadOnlyList<SearchAfterValue> afterValues,
            int topN,
            IReadOnlyList<SortField> sorts)
        {
            _searcher = searcher;
            _sorts = sorts.ToArray();
            _explicitAfterValues = afterValues.ToArray();
            _after = default;
            _heap = new ScoreDoc[topN];
            _heapValues = new SortValue[checked(topN * sorts.Count)];
            _candidateValues = new SortValue[sorts.Count];
            _afterValues = new SortValue[sorts.Count];
            for (int i = 0; i < _sorts.Length; i++)
            {
                SearchAfterValue value = _explicitAfterValues[i];
                _afterValues[i] = value.Type switch
                {
                    SortFieldType.Score or SortFieldType.Numeric => SortValue.FromNumeric(value.NumericValue),
                    SortFieldType.GeoDistance or SortFieldType.XYDistance => SortValue.FromNumeric(value.NumericValue, value.IsMissing),
                    SortFieldType.DocId or SortFieldType.Int64 => SortValue.FromInt64(value.Int64Value),
                    SortFieldType.String => SortValue.FromString(value.StringValue!),
                    _ => throw new ArgumentException($"Sort type '{value.Type}' is not cursor-compatible.", nameof(afterValues))
                };
            }
        }

        public int TotalHits => _totalHits;
        public int Capacity => _heap.Length;
        public bool IsFull => _size == _heap.Length;
        public float MinScore => float.NegativeInfinity;

        public void Collect(int docId, float score)
        {
            _totalHits++;
            AddCandidate(new ScoreDoc(docId, score));
        }

        private void AddCandidate(ScoreDoc candidate)
        {
            FillValues(candidate, _candidateValues);
            if (Compare(
                    _candidateValues,
                    candidate.DocId,
                    _afterValues,
                    _after.DocId,
                    includeDocumentIdTieBreak: _explicitAfterValues is null) <= 0)
            {
                return;
            }

            if (_size < _heap.Length)
            {
                _heap[_size] = candidate;
                CopyValues(_candidateValues, _size);
                _size++;
                if (_size == _heap.Length)
                    BuildWorstHeap();
                return;
            }

            if (CompareCandidateToSlot(candidate.DocId, 0) >= 0)
                return;

            _heap[0] = candidate;
            CopyValues(_candidateValues, 0);
            SiftDown(0);
        }

        public ITopNCollectorStrategy CreateWorker()
            => _explicitAfterValues is null
                ? new FieldAfterCollectorStrategy(_searcher, _after, _heap.Length, _sorts)
                : new FieldAfterCollectorStrategy(_searcher, _explicitAfterValues, _heap.Length, _sorts);

        public void MergeWorker(ITopNCollectorStrategy worker)
        {
            var fieldWorker = (FieldAfterCollectorStrategy)worker;
            _totalHits += fieldWorker._totalHits;
            foreach (var scoreDoc in fieldWorker.ToTopDocs().ScoreDocs)
                AddCandidate(scoreDoc);
        }

        public TopDocs ToTopDocs()
        {
            if (_size == 0)
                return new TopDocs(_totalHits, []);

            if (_size < _heap.Length)
                BuildWorstHeap();

            int remaining = _size;
            var results = new ScoreDoc[remaining];
            while (remaining > 0)
            {
                results[remaining - 1] = _heap[0];
                remaining--;
                if (remaining == 0)
                    break;

                _heap[0] = _heap[remaining];
                CopySlot(remaining, 0);
                SiftDown(0, remaining);
            }

            _size = 0;
            return new TopDocs(_totalHits, results);
        }

        public void Reset()
        {
            _size = 0;
            _totalHits = 0;
        }

        private void FillValues(ScoreDoc scoreDoc, SortValue[] destination)
            => FillSortValues(_searcher, scoreDoc, _sorts, destination);

        private int CompareCandidateToSlot(int candidateDocId, int slot)
        {
            int offset = slot * _sorts.Length;
            return Compare(
                _candidateValues,
                candidateDocId,
                _heapValues.AsSpan(offset, _sorts.Length),
                _heap[slot].DocId);
        }

        private int CompareSlots(int left, int right)
        {
            int leftOffset = left * _sorts.Length;
            int rightOffset = right * _sorts.Length;
            return Compare(
                _heapValues.AsSpan(leftOffset, _sorts.Length),
                _heap[left].DocId,
                _heapValues.AsSpan(rightOffset, _sorts.Length),
                _heap[right].DocId);
        }

        private int Compare(
            ReadOnlySpan<SortValue> left,
            int leftDocId,
            ReadOnlySpan<SortValue> right,
            int rightDocId,
            bool includeDocumentIdTieBreak = true)
            => CompareSortValues(
                _sorts,
                left,
                leftDocId,
                right,
                rightDocId,
                includeDocumentIdTieBreak);

        private void CopyValues(ReadOnlySpan<SortValue> source, int slot)
            => source.CopyTo(_heapValues.AsSpan(slot * _sorts.Length, _sorts.Length));

        private void CopySlot(int source, int destination)
        {
            _heapValues.AsSpan(source * _sorts.Length, _sorts.Length).CopyTo(
                _heapValues.AsSpan(destination * _sorts.Length, _sorts.Length));
        }

        private void BuildWorstHeap()
        {
            for (int i = _size / 2 - 1; i >= 0; i--)
                SiftDown(i);
        }

        private void SiftDown(int index)
            => SiftDown(index, _size);

        private void SiftDown(int index, int size)
        {
            while (true)
            {
                int worst = index;
                int left = (index * 2) + 1;
                int right = left + 1;
                if (left < size && CompareSlots(left, worst) > 0)
                    worst = left;
                if (right < size && CompareSlots(right, worst) > 0)
                    worst = right;
                if (worst == index)
                    return;

                (_heap[index], _heap[worst]) = (_heap[worst], _heap[index]);
                SwapValues(index, worst);
                index = worst;
            }
        }

        private void SwapValues(int left, int right)
        {
            int leftOffset = left * _sorts.Length;
            int rightOffset = right * _sorts.Length;
            for (int i = 0; i < _sorts.Length; i++)
            {
                (_heapValues[leftOffset + i], _heapValues[rightOffset + i]) =
                    (_heapValues[rightOffset + i], _heapValues[leftOffset + i]);
            }
        }
    }

    private readonly record struct SortValue(double Numeric, long Int64, string? String, bool Missing = false)
    {
        internal static SortValue FromNumeric(double value, bool isMissing = false) => new(value, 0, null, isMissing);
        internal static SortValue FromInt64(long value) => new(0, value, null);
        internal static SortValue FromString(string value) => new(0, 0, value);

        internal int CompareTo(SortValue other, SortFieldType type) => type switch
        {
            SortFieldType.Score or SortFieldType.Numeric or SortFieldType.GeoDistance or SortFieldType.XYDistance
                => Numeric.CompareTo(other.Numeric),
            SortFieldType.DocId or SortFieldType.Int64 => Int64.CompareTo(other.Int64),
            SortFieldType.String => string.CompareOrdinal(String, other.String),
            _ => 0
        };
    }

    private TopDocs SearchWithIndexSortEarlyTermination(
        TermQuery tq,
        int topN,
        SortField sort,
        SearchExecutionBudget budget)
    {
        // Every segment is independently sorted. Its first topN live matches are
        // sufficient candidates for the global topN, but stopping after the first
        // full segment is not: a later segment may contain better sort keys. Merge
        // candidates into one globally bounded field-sort heap as each segment ends.
        var candidates = new FieldSortCollectorStrategy(this, topN, [sort]);
        int observedHits = 0;
        var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
        foreach (var reader in _readers)
        {
            if (budget.Checkpoint(SearchExecutionCheckpoint.BeforeSegment))
                break;

            using var pe = reader.GetPostingsEnum(qt);
            int segmentHits = 0;
            if (!pe.IsExhausted)
            {
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                while (pe.MoveNext() && segmentHits < topN)
                {
                    int docId = pe.DocId;
                    if (hasDeletions && !reader.IsLive(docId)) continue;
                    candidates.Collect(docBase + docId, 1.0f);
                    segmentHits++;
                }
            }

            observedHits += segmentHits;
            if (budget.Checkpoint(SearchExecutionCheckpoint.AfterSegment))
                break;
        }

        TopDocs selected = candidates.ToTopDocs();
        // The sorted index lets us stop once the requested page is full, so the
        // hit count is intentionally bounded to the documents observed per segment.
        // Advertise that contract to callers rather than presenting the page
        // count as the complete query hit count.
        return new TopDocs(observedHits, selected.ScoreDocs, isPartial: true);
    }

    internal sealed class SearchExecutionBudget
    {
        private readonly IndexSearcher _searcher;
        private readonly SearchOptions _options;
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private readonly long? _deadlineTicks;

        internal SearchExecutionBudget(IndexSearcher searcher, SearchOptions options)
        {
            _searcher = searcher;
            _options = options;
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _deadlineTicks = options.Timeout.HasValue
                ? _stopwatch.ElapsedTicks
                    + (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
                : null;
        }

        internal SearchOptions Options => _options;
        internal System.Diagnostics.Stopwatch Stopwatch => _stopwatch;
        internal long? DeadlineTicks => _deadlineTicks;
        internal bool CanRunParallel
            => !_deadlineTicks.HasValue && !_options.CancellationToken.CanBeCanceled;

        internal bool IsStopped
            => _options.CancellationToken.IsCancellationRequested
                || (_deadlineTicks.HasValue && _stopwatch.ElapsedTicks >= _deadlineTicks.Value);

        internal bool Checkpoint(SearchExecutionCheckpoint checkpoint)
        {
            _searcher.SearchExecutionCheckpointForTesting?.Invoke(checkpoint);
            return IsStopped;
        }

        internal TopDocs EmptyPartialResult()
            => new(0, [], isPartial: true);
    }

    internal interface IParallelTopNCollectorStrategy
    {
        ITopNCollectorStrategy CreateWorker();
        void MergeWorker(ITopNCollectorStrategy worker);
    }
}
