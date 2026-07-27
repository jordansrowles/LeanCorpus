using System.Buffers;
using Rowles.LeanCorpus.Search.Scoring;

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
        if (topN <= 0)
            return TopDocs.Empty;
        if (sorts.Length == 0)
            sorts = [SortField.Score];

        var allDocs = Search(query, _totalDocCount, sorts);
        int afterIndex = Array.FindIndex(
            allDocs.ScoreDocs,
            scoreDoc => scoreDoc.DocId == after.DocId);
        if (afterIndex < 0)
            throw new ArgumentException(
                "The search-after document is not a result in this searcher snapshot.",
                nameof(after));

        int available = allDocs.ScoreDocs.Length - afterIndex - 1;
        int resultCount = Math.Min(topN, available);
        if (resultCount <= 0)
            return new TopDocs(allDocs.TotalHits, []);

        var page = new ScoreDoc[resultCount];
        Array.Copy(allDocs.ScoreDocs, afterIndex + 1, page, 0, resultCount);
        return new TopDocs(allDocs.TotalHits, page);
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

        // Fast path: if the sort matches the index sort, iterate postings in doc-ID
        // order (which is sort-key order) and stop after collecting topN live docs.
        if (_readers.Count > 0 && query is TermQuery tq
            && TryGetIndexSort(out var indexSort) && MatchesSort(sort, indexSort))
        {
            return SearchWithIndexSortEarlyTermination(tq, topN);
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

        sw.Stop();
        return partial
            ? new TopDocs(allDocs.TotalHits, sorted, isPartial: true)
            : new TopDocs(allDocs.TotalHits, sorted);
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

    private double ResolveNumeric(
        int globalId,
        string fieldName,
        SortValueSelector selector)
    {
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetNumericValue(fieldName, globalId - _docBases[r], out double val))
                    return val;
                if (_readers[r].TryGetSortedNumericDocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return selector == SortValueSelector.Max ? values[^1] : values[0];
                break;
            }
        }
        var stored = GetStoredFields(globalId, new HashSet<string> { fieldName });
        if (stored.TryGetValue(fieldName, out var sv) && sv.Count > 0
            && double.TryParse(sv[0], System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private long ResolveInt64(int globalId, string fieldName)
        => ResolveInt64(globalId, fieldName, SortValueSelector.Min);

    private long ResolveInt64(
        int globalId,
        string fieldName,
        SortValueSelector selector)
    {
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetInt64Value(fieldName, globalId - _docBases[r], out long val))
                    return val;
                if (_readers[r].TryGetSortedInt64DocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return selector == SortValueSelector.Max ? values[^1] : values[0];
                break;
            }
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
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetSortedDocValue(fieldName, globalId - _docBases[r], out string val))
                    return val;
                if (_readers[r].TryGetSortedSetDocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return selector == SortValueSelector.Max ? values[^1] : values[0];
                if (_readers[r].TryGetBinaryDocValues(fieldName, globalId - _docBases[r], out var binaryValues) && binaryValues.Count > 0)
                    return System.Text.Encoding.UTF8.GetString(binaryValues[0]);
                break;
            }
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
        var fields = _readers[0].Info.IndexSortFields;
        if (fields is not { Count: 1 }) return false;
        var parts = fields[0].Split(':');
        if (parts.Length != 3) return false;
        if (!Enum.TryParse<SortFieldType>(parts[0], out var type)) return false;
        indexSortField = new SortField(type, parts[1], bool.Parse(parts[2]));
        return true;
    }

    private static bool MatchesSort(SortField a, SortField b)
        => a.Type == b.Type && a.FieldName == b.FieldName
            && a.Descending == b.Descending && a.Selector == b.Selector;

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
            }
        }
        return column;
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

        internal SortColumn(SortField field, int count)
        {
            _field = field;
            if (field.Type is SortFieldType.Score or SortFieldType.Numeric)
                NumericValues = new double[count];
            else if (field.Type is SortFieldType.DocId or SortFieldType.Int64)
                Int64Values = new long[count];
            else
                StringValues = new string[count];
        }

        internal int Compare(int left, int right)
        {
            int comparison = _field.Type switch
            {
                SortFieldType.Score or SortFieldType.Numeric =>
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

    private TopDocs SearchWithIndexSortEarlyTermination(TermQuery tq, int topN)
    {
        var collector = new TopNCollector(topN);
        var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
        foreach (var reader in _readers)
        {
            if (collector.IsFull) break;
            using var pe = reader.GetPostingsEnum(qt);
            if (pe.IsExhausted) continue;
            int docBase = reader.DocBase;
            bool hasDeletions = reader.HasDeletions;
            while (pe.MoveNext() && !collector.IsFull)
            {
                int docId = pe.DocId;
                if (hasDeletions && !reader.IsLive(docId)) continue;
                collector.Collect(docBase + docId, 1.0f);
            }
        }
        return collector.ToTopDocs();
    }
}
