using System.Buffers;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document.Fields;
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
        ArgumentNullException.ThrowIfNull(sorts);
        if (sorts.Count == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(sorts));
        if (sorts.Count == 1)
            return Search(query, topN, sorts[0], options);
        if (topN <= 0)
            return TopDocs.Empty;

        ArgumentNullException.ThrowIfNull(options);
        long topNBytes = checked((long)topN * Scoring.ScoreDoc.EstimatedBytes);
        if (topNBytes > options.MaxResultBytes)
            throw new ArgumentException(
                $"MaxResultBytes ({options.MaxResultBytes}) is smaller than the requested top-N heap ({topNBytes} bytes).",
                nameof(options));

        options.CancellationToken.ThrowIfCancellationRequested();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var allDocs = Search(query, _totalDocCount);
        if (allDocs.TotalHits == 0)
            return TopDocs.Empty;

        var sorted = SortCandidates(allDocs.ScoreDocs, sorts, topN);

        bool partial = options.CancellationToken.IsCancellationRequested
            || (options.Timeout.HasValue && stopwatch.Elapsed > options.Timeout.Value);
        return partial
            ? new TopDocs(allDocs.TotalHits, sorted, isPartial: true)
            : new TopDocs(allDocs.TotalHits, sorted);
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
        ArgumentNullException.ThrowIfNull(options);

        if (sort.Type == SortFieldType.Score)
            return Search(query, topN, options);

        if (topN <= 0)
            return TopDocs.Empty;

        long topNBytes = checked((long)topN * Scoring.ScoreDoc.EstimatedBytes);
        if (topNBytes > options.MaxResultBytes)
            throw new ArgumentException(
                $"MaxResultBytes ({options.MaxResultBytes}) is smaller than the requested top-N heap ({topNBytes} bytes).",
                nameof(options));

        // Check cancellation and timeout before the expensive full fetch.
        options.CancellationToken.ThrowIfCancellationRequested();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? sw.ElapsedTicks + (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;
        if (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value)
            return TopDocs.Empty;

        Query rewrittenQuery = RewriteQuery(query);
        if (!sort.Descending
            && (sort.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance)
            && TrySearchBestFirstSpatial(rewrittenQuery, topN, sort, options, sw, deadlineTicks, out TopDocs nearest))
            return nearest;

        // Fast path: if the sort matches the index sort, iterate postings in doc-ID
        // order (which is sort-key order) and stop after collecting topN live docs.
        if (_readers.Count > 0 && query is TermQuery tq
            && TryGetIndexSort(out var indexSort) && MatchesSort(sort, indexSort))
        {
            return SearchWithIndexSortEarlyTermination(tq, topN, sort);
        }

        // We still need every match to pick the top-N by sort key, but topN itself
        // bounds how many we return. _totalDocCount is the upper bound on matches.
        // A field sort does not need relevance scores. Keep the term-query path
        // aligned with Lucene's sorted search by scanning matching postings with
        // a constant score instead of calculating BM25 for every document.
        var allDocs = query is TermQuery termQuery && sort.Type == SortFieldType.Numeric
            ? SearchTermQueryUnscored(termQuery)
            : Search(query, _totalDocCount);
        if (allDocs.TotalHits == 0) return TopDocs.Empty;

        bool partial = options.CancellationToken.IsCancellationRequested
            || (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value);

        var docs = allDocs.ScoreDocs;
        int effectiveN = Math.Min(topN, docs.Length);

        var sorted = sort.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance
            ? SortCandidates(docs, [sort], effectiveN)
            : sort.Type switch
            {
                SortFieldType.DocId => SelectTopByDocId(docs, effectiveN, sort.Descending),
                SortFieldType.Numeric => SelectTopByNumericField(
                    docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
                SortFieldType.Int64 => SelectTopByInt64Field(
                    docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
                SortFieldType.String => SelectTopByStringField(
                    docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
                _ => docs.Length > effectiveN ? docs[..effectiveN] : docs
            };

        sw.Stop();
        return partial
            ? new TopDocs(allDocs.TotalHits, sorted, isPartial: true)
            : new TopDocs(allDocs.TotalHits, sorted);
    }

    private bool TrySearchBestFirstSpatial(
        Query query,
        int topN,
        SortField sort,
        SearchOptions options,
        System.Diagnostics.Stopwatch stopwatch,
        long? deadlineTicks,
        out TopDocs result)
    {
        result = TopDocs.Empty;
        if (query is not (MatchAllDocsQuery or ConstantScoreQuery)
            || sort.Type is not (SortFieldType.GeoDistance or SortFieldType.XYDistance)
            || (sort.Type == SortFieldType.GeoDistance && sort.GeoOrigin is null)
            || (sort.Type == SortFieldType.XYDistance && sort.XYOrigin is null))
            return false;

        int totalHits = Count(query);
        if (totalHits == 0)
        {
            result = TopDocs.Empty;
            return true;
        }

        int perSegmentTopN = Math.Min(topN, totalHits);
        var globalTopN = new SortedSet<SpatialDistanceCandidate>(SpatialDistanceCandidateComparer.Instance);
        ConstantScoreQuery? constantScoreQuery = query as ConstantScoreQuery;
        Dictionary<(string Field, string Term), int> globalDFs = constantScoreQuery is null
            ? new Dictionary<(string Field, string Term), int>()
            : PrecomputeGlobalDocFreqsForSearch(constantScoreQuery.Inner);
        bool partial = false;
        foreach (SegmentReader reader in _readers)
        {
            if (options.CancellationToken.IsCancellationRequested
                || (deadlineTicks.HasValue && stopwatch.ElapsedTicks > deadlineTicks.Value))
            {
                partial = true;
                break;
            }

            int capacity = Math.Min(perSegmentTopN, reader.MaxDoc);
            if (capacity == 0)
                continue;

            Util.RoaringBitmap? filterBitmap = constantScoreQuery is null
                ? null
                : ExecuteFilterToBitmap(constantScoreQuery.Inner, reader, globalDFs);
            if (filterBitmap is { Cardinality: 0 })
                continue;

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
                options.CancellationToken,
                stopwatch,
                deadlineTicks);
            bool compatiblePackedField = reader.TryGetPackedBkdFieldMetadata(sort.FieldName, out PackedBkdFieldMetadata metadata)
                && metadata.Config.Dimensions == 2
                && metadata.Config.IndexedDimensions == 2
                && metadata.Config.BytesPerDimension == sizeof(uint);

            if (compatiblePackedField)
            {
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
            if (collector.ShouldStop)
            {
                partial = true;
                break;
            }
        }

        var sorted = new ScoreDoc[globalTopN.Count];
        int resultIndex = 0;
        foreach (SpatialDistanceCandidate candidate in globalTopN)
            sorted[resultIndex++] = new ScoreDoc(candidate.GlobalDocId, candidate.Score);
        partial |= options.CancellationToken.IsCancellationRequested
            || (deadlineTicks.HasValue && stopwatch.ElapsedTicks > deadlineTicks.Value);
        result = new TopDocs(totalHits, sorted, isPartial: partial);
        return true;
    }

    private TopDocs SearchTermQueryUnscored(TermQuery query)
    {
        var qt = query.CachedQualifiedTerm ??= string.Concat(query.Field, "\x00", query.Term);
        int readerCount = _readers.Count;
        if (t_postingsBuffer is null || t_postingsBuffer.Length < readerCount)
            t_postingsBuffer = new PostingsEnum[readerCount];

        var postingsArr = t_postingsBuffer;
        var collector = new TopNCollector(_totalDocCount);
        try
        {
            for (int i = 0; i < readerCount; i++)
            {
                postingsArr[i] = _readers[i].GetPostingsEnum(qt);
                if (postingsArr[i].IsExhausted)
                    continue;

                var reader = _readers[i];
                using var queryLease = reader.AcquireQueryLease();
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                while (postingsArr[i].MoveNext())
                {
                    int docId = postingsArr[i].DocId;
                    if (hasDeletions && !reader.IsLive(docId))
                        continue;
                    collector.Collect(docBase + docId, 1.0f);
                }
            }

            return collector.ToTopDocs();
        }
        finally
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
        }
    }

    private static ScoreDoc[] SelectTopByDocId(ScoreDoc[] docs, int topN, bool descending)
    {
        // Sort key is docId; reuse the numeric heap-select with double keys.
        var keys = new double[docs.Length];
        for (int i = 0; i < docs.Length; i++) keys[i] = docs[i].DocId;
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByNumericField(
        ScoreDoc[] docs,
        int topN,
        string fieldName,
        bool descending,
        SortValueSelector selector)
    {
        var keys = new double[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveNumeric(docs[i].DocId, fieldName, selector);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByStringField(
        ScoreDoc[] docs,
        int topN,
        string fieldName,
        bool descending,
        SortValueSelector selector)
    {
        var keys = new string[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveString(docs[i].DocId, fieldName, selector);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByInt64Field(
        ScoreDoc[] docs,
        int topN,
        string fieldName,
        bool descending,
        SortValueSelector selector)
    {
        var keys = new long[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveInt64(docs[i].DocId, fieldName, selector);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
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
                || !TryParseIndexSortField(fields[0], out var readerSort))
            {
                return false;
            }

            if (commonSort is not null && !MatchesSort(commonSort, readerSort))
                return false;

            commonSort = readerSort;
        }

        indexSortField = commonSort!;
        return true;
    }

    private static bool TryParseIndexSortField(string metadata, out SortField sortField)
    {
        sortField = default!;
        var parts = metadata.Split(':');
        if (parts.Length is < 3 or > 4) return false;
        if (!Enum.TryParse<SortFieldType>(parts[0], out var type)) return false;
        if (type is SortFieldType.GeoDistance or SortFieldType.XYDistance) return false;
        if (!bool.TryParse(parts[2], out bool descending)) return false;
        var selector = SortValueSelector.Min;
        if (parts.Length == 4 && !Enum.TryParse(parts[3], out selector)) return false;
        sortField = new SortField(type, parts[1], descending, selector);
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
            Rowles.LeanCorpus.Search.Geo.GeoPoint origin = sort.GeoOrigin
                ?? throw new InvalidOperationException("A geographic distance sort has no origin.");
            string pointValuesField = GeoPointDocValues.GetFieldName(sort.FieldName);
            if (reader.TryGetBinaryDocValues(pointValuesField, localDocId, out var values))
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
        private readonly CancellationToken _cancellationToken;
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private readonly long? _deadlineTicks;
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
            CancellationToken cancellationToken,
            System.Diagnostics.Stopwatch stopwatch,
            long? deadlineTicks)
        {
            _searcher = searcher;
            _reader = reader;
            _sort = sort;
            _capacity = capacity;
            _score = score;
            _scoreField = scoreField;
            _filterBitmap = filterBitmap;
            _cancellationToken = cancellationToken;
            _stopwatch = stopwatch;
            _deadlineTicks = deadlineTicks;
        }

        internal int Count => _ordered.Count;

        public int CurrentCandidateCount => _ordered.Count;

        public int PeakCandidateCount => _peakCandidateCount;

        public long ExactDistanceCalculations => _exactDistanceCalculations;

        public long FilterCandidatesRejected => _filterCandidatesRejected;

        public long CandidateUpdates => _candidateUpdates;

        public bool ShouldStop => _cancellationToken.IsCancellationRequested
            || (_deadlineTicks.HasValue && _stopwatch.ElapsedTicks > _deadlineTicks.Value);

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
            if (!_reader.TryGetNumericDocValues(latitudeField, out _, out var latitudePresence)
                || !_reader.TryGetNumericDocValues(longitudeField, out _, out var longitudePresence))
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

            byte[][][]? exactGeoValues = _reader.GetBinaryDocValues(
                GeoPointDocValues.GetFieldName(_sort.FieldName));
            if (legacyCandidates is null)
            {
                for (int localDocId = 0; localDocId < _reader.MaxDoc && !ShouldStop; localDocId++)
                    CollectLegacyGeoDocument(localDocId, exactGeoValues);
                return;
            }

            foreach (int localDocId in legacyCandidates)
            {
                if (ShouldStop)
                    break;

                CollectLegacyGeoDocument(localDocId, exactGeoValues);
            }
        }

        private void CollectLegacyGeoDocument(int localDocId, byte[][][]? exactGeoValues)
        {
            if (!_reader.IsLive(localDocId))
                return;
            if (_filterBitmap is not null && !_filterBitmap.Contains(localDocId))
            {
                _filterCandidatesRejected++;
                return;
            }
            if (exactGeoValues is not null
                && (uint)localDocId < (uint)exactGeoValues.Length
                && exactGeoValues[localDocId].Length > 0)
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
        {
            if (_field.Type is SortFieldType.GeoDistance or SortFieldType.XYDistance)
            {
                bool leftMissing = MissingValues![left];
                bool rightMissing = MissingValues[right];
                if (leftMissing != rightMissing)
                    return leftMissing ? 1 : -1;
                if (leftMissing)
                    return 0;
            }

            int comparison = _field.Type switch
            {
                SortFieldType.Score or SortFieldType.Numeric or SortFieldType.GeoDistance or SortFieldType.XYDistance =>
                    NumericValues![left].CompareTo(NumericValues[right]),
                SortFieldType.DocId or SortFieldType.Int64 =>
                    Int64Values![left].CompareTo(Int64Values[right]),
                SortFieldType.String =>
                    string.CompareOrdinal(StringValues![left], StringValues[right]),
                _ => 0
            };
            return _field.Descending ? -comparison : comparison;
        }
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
        {
            for (int i = 0; i < _sorts.Length; i++)
            {
                var sort = _sorts[i];
                destination[i] = sort.Type switch
                {
                    SortFieldType.Score => SortValue.FromNumeric(scoreDoc.Score),
                    SortFieldType.DocId => SortValue.FromInt64(scoreDoc.DocId),
                    SortFieldType.Numeric => SortValue.FromNumeric(
                        _searcher.ResolveNumeric(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                    SortFieldType.Int64 => SortValue.FromInt64(
                        _searcher.ResolveInt64(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                    SortFieldType.String => SortValue.FromString(
                        _searcher.ResolveString(scoreDoc.DocId, sort.FieldName, sort.Selector)),
                    SortFieldType.GeoDistance or SortFieldType.XYDistance =>
                        _searcher.ResolveSpatialSortValue(scoreDoc.DocId, sort),
                    _ => default
                };
            }
        }

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
        {
            for (int i = 0; i < _sorts.Length; i++)
            {
                if (_sorts[i].Type is SortFieldType.GeoDistance or SortFieldType.XYDistance
                    && left[i].Missing != right[i].Missing)
                    return left[i].Missing ? 1 : -1;

                int comparison = left[i].CompareTo(right[i], _sorts[i].Type);
                if (comparison == 0)
                    continue;
                return _sorts[i].Descending ? -comparison : comparison;
            }
            return includeDocumentIdTieBreak ? leftDocId.CompareTo(rightDocId) : 0;
        }

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

    private TopDocs SearchWithIndexSortEarlyTermination(TermQuery tq, int topN, SortField sort)
    {
        // Every segment is independently sorted. Its first topN live matches are
        // sufficient candidates for the global topN, but stopping after the first
        // full segment is not: a later segment may contain better sort keys.
        var candidates = new List<ScoreDoc>();
        int observedHits = 0;
        var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
        foreach (var reader in _readers)
        {
            using var pe = reader.GetPostingsEnum(qt);
            if (pe.IsExhausted) continue;
            int docBase = reader.DocBase;
            bool hasDeletions = reader.HasDeletions;
            int segmentHits = 0;
            while (pe.MoveNext() && segmentHits < topN)
            {
                int docId = pe.DocId;
                if (hasDeletions && !reader.IsLive(docId)) continue;
                candidates.Add(new ScoreDoc(docBase + docId, 1.0f));
                segmentHits++;
                observedHits++;
            }
        }

        var docs = candidates.ToArray();
        int effectiveN = Math.Min(topN, docs.Length);
        var sorted = sort.Type switch
        {
            SortFieldType.DocId => SelectTopByDocId(docs, effectiveN, sort.Descending),
            SortFieldType.Numeric => SelectTopByNumericField(
                docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
            SortFieldType.Int64 => SelectTopByInt64Field(
                docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
            SortFieldType.String => SelectTopByStringField(
                docs, effectiveN, sort.FieldName, sort.Descending, sort.Selector),
            _ => docs.Length > effectiveN ? docs[..effectiveN] : docs
        };

        // The sorted index lets us stop once the requested page is full, so the
        // hit count is intentionally bounded to the documents observed per segment.
        // Advertise that contract to callers rather than presenting the page
        // count as the complete query hit count.
        return new TopDocs(observedHits, sorted, isPartial: true);
    }

    internal interface IParallelTopNCollectorStrategy
    {
        ITopNCollectorStrategy CreateWorker();
        void MergeWorker(ITopNCollectorStrategy worker);
    }
}
