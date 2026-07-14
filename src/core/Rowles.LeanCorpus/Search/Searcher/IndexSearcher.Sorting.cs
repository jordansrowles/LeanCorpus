using System.Buffers;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing sorting functionality for search results.
/// </summary>
public sealed partial class IndexSearcher
{
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
        var allDocs = Search(query, _totalDocCount);
        if (allDocs.TotalHits == 0) return TopDocs.Empty;

        bool partial = options.CancellationToken.IsCancellationRequested
            || (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value);

        var docs = allDocs.ScoreDocs;
        int effectiveN = Math.Min(topN, docs.Length);

        var sorted = sort.Type switch
        {
            SortFieldType.DocId => SelectTopByDocId(docs, effectiveN, sort.Descending),
            SortFieldType.Numeric => SelectTopByNumericField(docs, effectiveN, sort.FieldName, sort.Descending),
            SortFieldType.Int64 => SelectTopByInt64Field(docs, effectiveN, sort.FieldName, sort.Descending),
            SortFieldType.String => SelectTopByStringField(docs, effectiveN, sort.FieldName, sort.Descending),
            _ => docs.Length > effectiveN ? docs[..effectiveN] : docs
        };

        sw.Stop();
        return partial
            ? new TopDocs(allDocs.TotalHits, sorted, isPartial: true)
            : new TopDocs(allDocs.TotalHits, sorted);
    }

    private static ScoreDoc[] SelectTopByDocId(ScoreDoc[] docs, int topN, bool descending)
    {
        // Sort key is docId; reuse the numeric heap-select with double keys.
        var keys = new double[docs.Length];
        for (int i = 0; i < docs.Length; i++) keys[i] = docs[i].DocId;
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByNumericField(ScoreDoc[] docs, int topN, string fieldName, bool descending)
    {
        var keys = new double[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveNumeric(docs[i].DocId, fieldName);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByStringField(ScoreDoc[] docs, int topN, string fieldName, bool descending)
    {
        var keys = new string[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveString(docs[i].DocId, fieldName);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private ScoreDoc[] SelectTopByInt64Field(ScoreDoc[] docs, int topN, string fieldName, bool descending)
    {
        var keys = new long[docs.Length];
        for (int i = 0; i < docs.Length; i++)
            keys[i] = ResolveInt64(docs[i].DocId, fieldName);
        return TopNSortHelper.SelectTopN(docs, keys, topN, descending);
    }

    private double ResolveNumeric(int globalId, string fieldName)
    {
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetNumericValue(fieldName, globalId - _docBases[r], out double val))
                    return val;
                if (_readers[r].TryGetSortedNumericDocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return values[0];
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
    {
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetInt64Value(fieldName, globalId - _docBases[r], out long val))
                    return val;
                if (_readers[r].TryGetSortedInt64DocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return values[0];
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
    {
        for (int r = 0; r < _readers.Count; r++)
        {
            int nextBase = r + 1 < _docBases.Length ? _docBases[r + 1] : _totalDocCount;
            if (globalId >= _docBases[r] && globalId < nextBase)
            {
                if (_readers[r].TryGetSortedDocValue(fieldName, globalId - _docBases[r], out string val))
                    return val;
                if (_readers[r].TryGetSortedSetDocValues(fieldName, globalId - _docBases[r], out var values) && values.Count > 0)
                    return values[0];
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
        => a.Type == b.Type && a.FieldName == b.FieldName && a.Descending == b.Descending;

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
