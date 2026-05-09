using System.Buffers;
using System.Runtime.CompilerServices;
namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Partial class containing query execution dispatcher and core Boolean query logic.
/// </summary>
public sealed partial class IndexSearcher
{
    // Per-thread scratch buffers for ExecuteBooleanFallback to avoid ArrayPool rent/return per query.
    // Falls back to ArrayPool when nested (recursive BooleanQuery sub-queries).
    [ThreadStatic] private static float[]? t_fallbackScores;
    [ThreadStatic] private static bool[]? t_fallbackInCandidate;
    [ThreadStatic] private static int[]? t_fallbackCandidateIds;
    [ThreadStatic] private static bool[]? t_fallbackInClause;
    [ThreadStatic] private static bool t_fallbackInUse;

    private static T[] EnsureScratch<T>(ref T[]? buffer, int minSize)
    {
        if (buffer is null || buffer.Length < minSize)
            buffer = new T[minSize];
        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAllTermQueryBoolean(BooleanQuery bq)
    {
        foreach (var clause in bq.Clauses)
        {
            if (clause.Query is not TermQuery)
                return false;
        }
        return bq.Clauses.Count > 0;
    }

    /// <summary>
    /// Fast path for BooleanQuery where all clauses are TermQuery.
    /// Computes global DFs inline without the generic PrecomputeGlobalDocFreqs tree walk.
    /// </summary>
    private TopDocs SearchBooleanTermQueryFast(BooleanQuery bq, int topN)
    {
        var clauses = bq.Clauses;

        // Compute global DFs inline — one pass over readers per unique term
        var globalDFs = new Dictionary<(string Field, string Term), int>(clauses.Count);
        foreach (var clause in clauses)
        {
            var tq = (TermQuery)clause.Query;
            var key = (tq.Field, tq.Term);
            if (globalDFs.ContainsKey(key)) continue;
            var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
            int total = 0;
            for (int r = 0; r < _readers.Count; r++)
                total += _readers[r].GetDocFreqByQualified(qt);
            globalDFs[key] = total;
        }

        var collector = new TopNCollector(topN);

        if (_readers.Count == 1)
        {
            ExecuteBooleanQuery(bq, _readers[0], globalDFs, ref collector);
        }
        else
        {
            var lockObj = new Lock();
            int maxDop = _config.MaxConcurrency > 0 ? _config.MaxConcurrency : Environment.ProcessorCount;
            Parallel.ForEach(_readers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, reader =>
            {
                var localCollector = new TopNCollector(topN);
                ExecuteBooleanQuery(bq, reader, globalDFs, ref localCollector);
                var localDocs = localCollector.ToTopDocs();
                lock (lockObj)
                {
                    foreach (var sd in localDocs.ScoreDocs)
                        collector.Collect(sd.DocId, sd.Score);
                }
            });
        }

        return collector.ToTopDocs();
    }

    private void ExecuteQuery(Query query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        switch (query)
        {
            case TermQuery tq:
                ExecuteTermQuery(tq, reader, globalDFs, ref collector);
                break;
            case BooleanQuery bq:
                ExecuteBooleanQuery(bq, reader, globalDFs, ref collector);
                break;
            case RangeQuery rq:
                ExecuteRangeQuery(rq, reader, ref collector);
                break;
            case PhraseQuery pq:
                ExecutePhraseQuery(pq, reader, ref collector);
                break;
            case VectorQuery vq:
                ExecuteVectorQuery(vq, reader, globalDFs, ref collector);
                break;
            case PrefixQuery pfq:
                ExecutePrefixQuery(pfq, reader, globalDFs, ref collector);
                break;
            case WildcardQuery wq:
                ExecuteWildcardQuery(wq, reader, globalDFs, ref collector);
                break;
            case FuzzyQuery fq:
                ExecuteFuzzyQuery(fq, reader, globalDFs, ref collector);
                break;
            case TermRangeQuery trq:
                ExecuteTermRangeQuery(trq, reader, globalDFs, ref collector);
                break;
            case ConstantScoreQuery csq:
                ExecuteConstantScoreQuery(csq, reader, globalDFs, ref collector);
                break;
            case DisjunctionMaxQuery dmq:
                ExecuteDisjunctionMaxQuery(dmq, reader, globalDFs, ref collector);
                break;
            case RegexpQuery rxq:
                ExecuteRegexpQuery(rxq, reader, globalDFs, ref collector);
                break;
            case FunctionScoreQuery fsq:
                ExecuteFunctionScoreQuery(fsq, reader, globalDFs, ref collector);
                break;
            case SpanNearQuery snq:
                ExecuteSpanNearQuery(snq, reader, globalDFs, ref collector);
                break;
            case SpanOrQuery soq:
                ExecuteSpanOrQuery(soq, reader, globalDFs, ref collector);
                break;
            case SpanNotQuery snotq:
                ExecuteSpanNotQuery(snotq, reader, globalDFs, ref collector);
                break;
            case GeoBoundingBoxQuery gbbq:
                ExecuteGeoBoundingBoxQuery(gbbq, reader, ref collector);
                break;
            case GeoDistanceQuery gdq:
                ExecuteGeoDistanceQuery(gdq, reader, ref collector);
                break;
        }
    }

    private void ExecuteTermQuery(TermQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var qt = query.CachedQualifiedTerm ??= string.Concat(query.Field, "\x00", query.Term);
        using var postings = reader.GetPostingsEnum(qt);
        if (postings.IsExhausted) return;

        int docFreq = globalDFs.GetValueOrDefault((query.Field, query.Term), postings.DocFreq);
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        var factors = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);
        int docBase = reader.DocBase;
        float boost = query.Boost;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);

        while (postings.MoveNext())
        {
            int docId = postings.DocId;
            if (!reader.IsLive(docId)) continue;

            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = _similarity.ScorePrecomputed(factors.Factor1, factors.Factor2, postings.Freq, docLength);
            if (boost != 1.0f) score *= boost;
            collector.Collect(docBase + docId, score);
        }
    }

    private void ExecuteBooleanQuery(BooleanQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var clauses = query.Clauses;
        if (clauses.Count == 0) return;

        // Single-pass clause counting + TermQuery check (no List<Query> allocs)
        int mustCount = 0, shouldCount = 0, mustNotCount = 0;
        bool allTermQueries = true;
        foreach (var clause in clauses)
        {
            switch (clause.Occur)
            {
                case Occur.Must: mustCount++; break;
                case Occur.Should: shouldCount++; break;
                case Occur.MustNot: mustNotCount++; break;
            }
            if (clause.Query is not TermQuery) allTermQueries = false;
        }

        if (mustCount == 0 && shouldCount == 0) return;

        // Fast path: all clauses are TermQuery → streaming PostingsEnum merge
        if (allTermQueries)
        {
            ExecuteBooleanStreaming(clauses, reader, globalDFs, ref collector,
                mustCount, shouldCount, mustNotCount);
            return;
        }

        // Fallback for complex sub-queries (nested BooleanQuery, RangeQuery, etc.)
        ExecuteBooleanFallback(query, reader, globalDFs, ref collector);
    }

    /// <summary>
    /// Streaming BooleanQuery execution for all-TermQuery clauses.
    /// Uses PostingsEnum merge instead of materialising HashSets and score maps.
    /// </summary>
    private void ExecuteBooleanStreaming(IReadOnlyList<BooleanClause> clauses,
        SegmentReader reader, Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector, int mustCount, int shouldCount, int mustNotCount)
    {
        var mustEnums = mustCount > 0 ? new PostingsEnum[mustCount] : null;
        var mustFactors = mustCount > 0 ? new (float Idf, float K1BOverAvgDL)[mustCount] : null;
        var mustFields = mustCount > 0 ? new string[mustCount] : null;
        var shouldEnums = shouldCount > 0 ? new PostingsEnum[shouldCount] : null;
        var shouldFactors = shouldCount > 0 ? new (float Idf, float K1BOverAvgDL)[shouldCount] : null;
        var shouldFields = shouldCount > 0 ? new string[shouldCount] : null;
        var mustNotEnums = mustNotCount > 0 ? new PostingsEnum[mustNotCount] : null;

        int mi = 0, si = 0, mni = 0;

        try
        {
            foreach (var clause in clauses)
            {
                var tq = (TermQuery)clause.Query;
                var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
                var postings = reader.GetPostingsEnum(qt);

                int docFreq = globalDFs.GetValueOrDefault((tq.Field, tq.Term), postings.DocFreq);
                float avgDocLength = _stats.GetAvgFieldLength(tq.Field);
                var factors = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

                switch (clause.Occur)
                {
                    case Occur.Must:
                        mustEnums![mi] = postings;
                        mustFactors![mi] = factors;
                        mustFields![mi] = tq.Field;
                        mi++;
                        break;
                    case Occur.Should:
                        shouldEnums![si] = postings;
                        shouldFactors![si] = factors;
                        shouldFields![si] = tq.Field;
                        si++;
                        break;
                    case Occur.MustNot:
                        mustNotEnums![mni] = postings;
                        mni++;
                        break;
                }
            }

            int docBase = reader.DocBase;
            bool hasDeletions = reader.HasDeletions;

            // Resolve field-length arrays once per clause to avoid per-doc dictionary lookups
            var mustFieldLens = mustCount > 0 ? new int[]?[mustCount] : null;
            for (int i = 0; i < mustCount; i++)
                reader.TryGetFieldLengths(mustFields![i], out mustFieldLens![i]);
            var shouldFieldLens = shouldCount > 0 ? new int[]?[shouldCount] : null;
            for (int i = 0; i < shouldCount; i++)
                reader.TryGetFieldLengths(shouldFields![i], out shouldFieldLens![i]);

            if (mustCount > 0)
            {
                // Sort Must enums by DocFreq ascending — rarest term leads
                int leaderIdx = 0;
                for (int i = 1; i < mustCount; i++)
                {
                    if (mustEnums![i].DocFreq < mustEnums[leaderIdx].DocFreq)
                        leaderIdx = i;
                }
                if (leaderIdx != 0)
                {
                    (mustEnums![0], mustEnums[leaderIdx]) = (mustEnums[leaderIdx], mustEnums[0]);
                    (mustFactors![0], mustFactors[leaderIdx]) = (mustFactors[leaderIdx], mustFactors[0]);
                    (mustFields![0], mustFields[leaderIdx]) = (mustFields[leaderIdx], mustFields[0]);
                }

                // Stream through leader, advance followers
                while (mustEnums![0].MoveNext())
                {
                    int docId = mustEnums[0].DocId;
                    if (hasDeletions && !reader.IsLive(docId)) continue;

                    // Check all followers match this doc
                    bool allMatch = true;
                    for (int i = 1; i < mustCount; i++)
                    {
                        if (!mustEnums[i].Advance(docId) || mustEnums[i].DocId != docId)
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (!allMatch) continue;

                    // Check MustNot exclusions
                    bool excluded = false;
                    for (int i = 0; i < mustNotCount; i++)
                    {
                        if (mustNotEnums![i].Advance(docId) && mustNotEnums[i].DocId == docId)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    // Compute BM25 score summed across Must terms (using per-field lengths)
                    float score = 0f;
                    for (int i = 0; i < mustCount; i++)
                    {
                        int docLength = mustFieldLens![i] is { } mfl && (uint)docId < (uint)mfl.Length ? mfl[docId] : 1;
                        score += _similarity.ScorePrecomputed(
                            mustFactors![i].Idf, mustFactors[i].K1BOverAvgDL,
                            mustEnums[i].Freq, docLength);
                    }

                    // Add Should bonus
                    for (int i = 0; i < shouldCount; i++)
                    {
                        if (shouldEnums![i].Advance(docId) && shouldEnums[i].DocId == docId)
                        {
                            int docLength = shouldFieldLens![i] is { } sfl && (uint)docId < (uint)sfl.Length ? sfl[docId] : 1;
                            score += _similarity.ScorePrecomputed(
                                shouldFactors![i].Idf, shouldFactors[i].K1BOverAvgDL,
                                shouldEnums[i].Freq, docLength);
                        }
                    }

                    collector.Collect(docBase + docId, score);
                }
            }
            else
            {
                // Should-only: streaming OR merge across all Should PostingsEnums.
                // Uses stackalloc to track current docId per enum — no heap allocation.
                Span<int> currentDocs = stackalloc int[shouldCount];
                for (int i = 0; i < shouldCount; i++)
                    currentDocs[i] = shouldEnums![i].MoveNext() ? shouldEnums[i].DocId : int.MaxValue;

                while (true)
                {
                    // Find minimum docId across all enums
                    int minDoc = int.MaxValue;
                    for (int i = 0; i < shouldCount; i++)
                    {
                        if (currentDocs[i] < minDoc)
                            minDoc = currentDocs[i];
                    }
                    if (minDoc == int.MaxValue) break;

                    if (hasDeletions && !reader.IsLive(minDoc))
                    {
                        for (int i = 0; i < shouldCount; i++)
                        {
                            if (currentDocs[i] == minDoc)
                                currentDocs[i] = shouldEnums![i].MoveNext() ? shouldEnums[i].DocId : int.MaxValue;
                        }
                        continue;
                    }

                    // Sum scores for all enums positioned at minDoc (using per-field lengths)
                    float score = 0f;
                    for (int i = 0; i < shouldCount; i++)
                    {
                        if (currentDocs[i] == minDoc)
                        {
                            int docLength = shouldFieldLens![i] is { } sfl && (uint)minDoc < (uint)sfl.Length ? sfl[minDoc] : 1;
                            score += _similarity.ScorePrecomputed(
                                shouldFactors![i].Idf, shouldFactors[i].K1BOverAvgDL,
                                shouldEnums![i].Freq, docLength);
                            currentDocs[i] = shouldEnums[i].MoveNext() ? shouldEnums[i].DocId : int.MaxValue;
                        }
                    }

                    // Check MustNot exclusion (Advance is forward-only; minDoc is monotonic)
                    bool excluded = false;
                    for (int i = 0; i < mustNotCount; i++)
                    {
                        if (mustNotEnums![i].Advance(minDoc) && mustNotEnums[i].DocId == minDoc)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (!excluded)
                        collector.Collect(docBase + minDoc, score);
                }
            }
        }
        finally
        {
            if (mustEnums != null)
                for (int i = 0; i < mi; i++) mustEnums[i].Dispose();
            if (shouldEnums != null)
                for (int i = 0; i < si; i++) shouldEnums[i].Dispose();
            if (mustNotEnums != null)
                for (int i = 0; i < mni; i++) mustNotEnums[i].Dispose();
        }
    }

    /// <summary>
    /// Fallback BooleanQuery execution for mixed clause types (nested BooleanQuery, RangeQuery, etc.).
    /// Uses [ThreadStatic] scratch arrays (zero allocation steady-state). Falls back to ArrayPool
    /// when called recursively (nested BooleanQuery sub-queries).
    /// </summary>
    private void ExecuteBooleanFallback(BooleanQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        int maxDoc = reader.MaxDoc;

        // Use per-thread scratch if not already in use (handles recursive calls via ArrayPool fallback)
        bool useScratch = !t_fallbackInUse;
        float[] scores;
        bool[] inCandidate;
        int[] candidateIds;

        if (useScratch)
        {
            t_fallbackInUse = true;
            scores = EnsureScratch(ref t_fallbackScores, maxDoc);
            inCandidate = EnsureScratch(ref t_fallbackInCandidate, maxDoc);
            candidateIds = EnsureScratch(ref t_fallbackCandidateIds, maxDoc);
        }
        else
        {
            scores = ArrayPool<float>.Shared.Rent(maxDoc);
            inCandidate = ArrayPool<bool>.Shared.Rent(maxDoc);
            candidateIds = ArrayPool<int>.Shared.Rent(maxDoc);
        }

        Array.Clear(scores, 0, maxDoc);
        Array.Clear(inCandidate, 0, maxDoc);
        int candidateCount = 0;
        int candidateIdCount = 0;

        try
        {
            bool hasMust = false;
            foreach (var clause in query.Clauses)
            {
                if (clause.Occur == Occur.Must) { hasMust = true; break; }
            }

            if (hasMust)
            {
                // Execute Must clauses smallest-result-first for tighter intersection
                bool first = true;
                foreach (var clause in query.Clauses)
                {
                    if (clause.Occur != Occur.Must) continue;
                    var results = ExecuteSubQuery(clause.Query, reader, globalDFs);

                    if (first)
                    {
                        foreach (var sr in results)
                        {
                            inCandidate[sr.DocId] = true;
                            scores[sr.DocId] += sr.Score;
                            candidateIds[candidateIdCount++] = sr.DocId;
                        }
                        candidateCount = candidateIdCount;
                        first = false;
                    }
                    else
                    {
                        // Early termination: no candidates left to intersect
                        if (candidateCount == 0) break;

                        // Use per-thread scratch for inClause too
                        bool[] inClause;
                        bool inClauseFromPool;
                        if (useScratch)
                        {
                            inClause = EnsureScratch(ref t_fallbackInClause, maxDoc);
                            inClauseFromPool = false;
                        }
                        else
                        {
                            inClause = ArrayPool<bool>.Shared.Rent(maxDoc);
                            inClauseFromPool = true;
                        }
                        Array.Clear(inClause, 0, maxDoc);

                        foreach (var sr in results)
                        {
                            inClause[sr.DocId] = true;
                            if (inCandidate[sr.DocId])
                                scores[sr.DocId] += sr.Score;
                        }

                        // Intersect using compact candidate list instead of O(maxDoc) scan
                        int writeIdx = 0;
                        for (int c = 0; c < candidateIdCount; c++)
                        {
                            int docId = candidateIds[c];
                            if (inClause[docId])
                            {
                                candidateIds[writeIdx++] = docId;
                            }
                            else
                            {
                                inCandidate[docId] = false;
                                scores[docId] = 0;
                            }
                        }
                        candidateIdCount = writeIdx;
                        candidateCount = writeIdx;

                        if (inClauseFromPool)
                            ArrayPool<bool>.Shared.Return(inClause, clearArray: false);
                    }
                }

                // SHOULD clauses boost matching candidates
                if (candidateCount > 0)
                {
                    foreach (var clause in query.Clauses)
                    {
                        if (clause.Occur != Occur.Should) continue;
                        var results = ExecuteSubQuery(clause.Query, reader, globalDFs);
                        foreach (var sr in results)
                        {
                            if (inCandidate[sr.DocId])
                                scores[sr.DocId] += sr.Score;
                        }
                    }
                }
            }
            else
            {
                // SHOULD-only: all matching docs are candidates
                foreach (var clause in query.Clauses)
                {
                    if (clause.Occur != Occur.Should) continue;
                    var results = ExecuteSubQuery(clause.Query, reader, globalDFs);
                    foreach (var sr in results)
                    {
                        if (!inCandidate[sr.DocId])
                        {
                            inCandidate[sr.DocId] = true;
                            candidateIds[candidateIdCount++] = sr.DocId;
                        }
                        scores[sr.DocId] += sr.Score;
                    }
                }
                candidateCount = candidateIdCount;

                if (candidateCount == 0) return;
            }

            // MUST_NOT: remove candidates using compact list
            foreach (var clause in query.Clauses)
            {
                if (clause.Occur != Occur.MustNot) continue;
                var results = ExecuteSubQuery(clause.Query, reader, globalDFs);
                foreach (var sr in results)
                {
                    if (inCandidate[sr.DocId])
                    {
                        inCandidate[sr.DocId] = false;
                        candidateCount--;
                    }
                }
            }

            int docBase = reader.DocBase;
            for (int c = 0; c < candidateIdCount; c++)
            {
                int d = candidateIds[c];
                if (!inCandidate[d] || !reader.IsLive(d)) continue;
                collector.Collect(docBase + d, scores[d] != 0 ? scores[d] : 1.0f);
            }
        }
        finally
        {
            if (useScratch)
                t_fallbackInUse = false;
            else
            {
                ArrayPool<float>.Shared.Return(scores, clearArray: false);
                ArrayPool<bool>.Shared.Return(inCandidate, clearArray: false);
                ArrayPool<int>.Shared.Return(candidateIds, clearArray: false);
            }
        }
    }

    /// <summary>Collects sub-query results into a list (used by BooleanQuery for set operations).</summary>
    private List<ScoreDoc> ExecuteSubQuery(Query query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs)
    {
        var results = new List<ScoreDoc>();
        switch (query)
        {
            case TermQuery tq:
                {
                    var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
                    using var postings = reader.GetPostingsEnum(qt);
                    if (postings.IsExhausted) break;
                    int docFreq = globalDFs.GetValueOrDefault((tq.Field, tq.Term), postings.DocFreq);
                    float avgDocLength = _stats.GetAvgFieldLength(tq.Field);
                    var factors = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);
                    reader.TryGetFieldLengths(tq.Field, out var fieldLengths);
                    while (postings.MoveNext())
                    {
                        int docId = postings.DocId;
                        if (!reader.IsLive(docId)) continue;
                        int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                            ? fieldLengths[docId] : 1;
                        float score = _similarity.ScorePrecomputed(factors.Factor1, factors.Factor2, postings.Freq, docLength);
                        results.Add(new ScoreDoc(docId, score));
                    }
                    break;
                }
            case BooleanQuery bq:
                {
                    // Nested boolean: use a sub-collector and extract results
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteBooleanQuery(bq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case RangeQuery rq:
                {
                    var rangeResults = reader.GetNumericRange(rq.Field, rq.Min, rq.Max);
                    float rqScore = rq.Boost != 1.0f ? rq.Boost : 1.0f;
                    foreach (var r in rangeResults)
                        results.Add(new ScoreDoc(r.DocId, rqScore));
                    break;
                }
            case PrefixQuery pfq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecutePrefixQuery(pfq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case WildcardQuery wq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteWildcardQuery(wq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case TermRangeQuery trq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteTermRangeQuery(trq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case ConstantScoreQuery csq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteConstantScoreQuery(csq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case DisjunctionMaxQuery dmq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteDisjunctionMaxQuery(dmq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case RegexpQuery rxq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteRegexpQuery(rxq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case FunctionScoreQuery fsq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteFunctionScoreQuery(fsq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case SpanNearQuery snq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteSpanNearQuery(snq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case SpanOrQuery soq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteSpanOrQuery(soq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
            case SpanNotQuery snotq:
                {
                    var subCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteSpanNotQuery(snotq, reader, globalDFs, ref subCollector);
                    var subDocs = subCollector.ToTopDocs();
                    foreach (var sd in subDocs.ScoreDocs)
                        results.Add(new ScoreDoc(sd.DocId - reader.DocBase, sd.Score));
                    break;
                }
        }
        return results;
    }
}
