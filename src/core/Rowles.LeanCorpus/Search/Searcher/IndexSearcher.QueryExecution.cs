using System.Buffers;
using System.Runtime.CompilerServices;
namespace Rowles.LeanCorpus.Search.Searcher;

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
        // Pin the heavy state for the complete segment operation. Nested query
        // execution takes additional value-type leases on the same cache entry.
        using var segmentLease = reader.AcquireQueryLease();
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
            case Int64RangeQuery irq:
                ExecuteInt64RangeQuery(irq, reader, ref collector);
                break;
            case PhraseQuery pq:
                ExecutePhraseQuery(pq, reader, globalDFs, ref collector);
                break;
            case MultiPhraseQuery mpq:
                ExecuteMultiPhraseQuery(mpq, reader, ref collector);
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
            case MatchAllDocsQuery madq:
                ExecuteMatchAllDocsQuery(madq, reader, ref collector);
                break;
            case MatchNoDocsQuery mndq:
                ExecuteMatchNoDocsQuery(mndq, reader, ref collector);
                break;
            case FieldExistsQuery feq:
                ExecuteFieldExistsQuery(feq, reader, ref collector);
                break;
            case TermInSetQuery tisq:
                ExecuteTermInSetQuery(tisq, reader, ref collector);
                break;
            case PointInSetQuery pisq:
                ExecutePointInSetQuery(pisq, reader, ref collector);
                break;
            case Int64PointInSetQuery ipisq:
                ExecuteInt64PointInSetQuery(ipisq, reader, ref collector);
                break;
            case CombinedFieldsQuery cfq:
                ExecuteCombinedFieldsQuery(cfq, reader, globalDFs, ref collector);
                break;
            case IntervalsQuery iq:
                ExecuteIntervalsQuery(iq, reader, ref collector);
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
        long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);
        int docBase = reader.DocBase;
        float boost = query.Boost;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        bool hasDeletions = reader.HasDeletions;
        bool hasQueryBoost = boost != 1.0f;

        while (postings.MoveNext())
        {
            int docId = postings.DocId;
            if (hasDeletions && !reader.IsLive(docId)) continue;

            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = ScoreTerm(f1, f2, f3, postings.Freq, docLength);
            if (hasQueryBoost) score *= boost;
            score = ApplyFieldBoost(fieldBoosts, docId, score);
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
        var mustFactors = mustCount > 0 ? new (float Idf, float K1BOverAvgDL, float CollectionProb)[mustCount] : null;
        var mustFields = mustCount > 0 ? new string[mustCount] : null;
        var shouldEnums = shouldCount > 0 ? new PostingsEnum[shouldCount] : null;
        var shouldFactors = shouldCount > 0 ? new (float Idf, float K1BOverAvgDL, float CollectionProb)[shouldCount] : null;
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

                // Absent from this segment: behaviour depends on Occur.
                if (postings.DocFreq == 0)
                {
                    postings.Dispose();

                    if (clause.Occur == Occur.Must)
                        return;

                    continue;
                }

                // Only calculate scoring factors for clauses that survived.
                int docFreq = globalDFs.GetValueOrDefault((tq.Field, tq.Term), postings.DocFreq);
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;
                float avgDocLength = Stats.GetAvgFieldLength(tq.Field);
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, tq.Field);

                switch (clause.Occur)
                {
                    case Occur.Must:
                        mustEnums![mi] = postings;
                        mustFactors![mi] = (f1, f2, f3);
                        mustFields![mi] = tq.Field;
                        mi++;
                        break;
                    case Occur.Should:
                        shouldEnums![si] = postings;
                        shouldFactors![si] = (f1, f2, f3);
                        shouldFields![si] = tq.Field;
                        si++;
                        break;
                    case Occur.MustNot:
                        mustNotEnums![mni] = postings;
                        mni++;
                        break;
                }
            }

            mustCount = mi;
            shouldCount = si;
            mustNotCount = mni;

            if (mustCount == 0 && shouldCount == 0)
                return;

            int docBase = reader.DocBase;
            bool hasDeletions = reader.HasDeletions;

            // Resolve field-length arrays once per clause to avoid per-doc dictionary lookups
            var mustFieldLens = mustCount > 0 ? new int[]?[mustCount] : null;
            var mustFieldBoosts = mustCount > 0 ? new float[]?[mustCount] : null;
            for (int i = 0; i < mustCount; i++)
            {
                reader.TryGetFieldLengths(mustFields![i], out mustFieldLens![i]);
                reader.TryGetFieldBoosts(mustFields[i], out mustFieldBoosts![i]);
            }
            var shouldFieldLens = shouldCount > 0 ? new int[]?[shouldCount] : null;
            var shouldFieldBoosts = shouldCount > 0 ? new float[]?[shouldCount] : null;
            for (int i = 0; i < shouldCount; i++)
            {
                reader.TryGetFieldLengths(shouldFields![i], out shouldFieldLens![i]);
                reader.TryGetFieldBoosts(shouldFields[i], out shouldFieldBoosts![i]);
            }

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
                    (mustFieldLens![0], mustFieldLens[leaderIdx]) = (mustFieldLens[leaderIdx], mustFieldLens[0]);
                    (mustFieldBoosts![0], mustFieldBoosts[leaderIdx]) = (mustFieldBoosts[leaderIdx], mustFieldBoosts[0]);
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
                        score += ApplyFieldBoost(mustFieldBoosts![i], docId, ScoreTerm(
                            mustFactors![i].Idf, mustFactors[i].K1BOverAvgDL, mustFactors[i].CollectionProb,
                            mustEnums[i].Freq, docLength));
                    }

                    // Add Should bonus
                    for (int i = 0; i < shouldCount; i++)
                    {
                        if (shouldEnums![i].Advance(docId) && shouldEnums[i].DocId == docId)
                        {
                            int docLength = shouldFieldLens![i] is { } sfl && (uint)docId < (uint)sfl.Length ? sfl[docId] : 1;
                            score += ApplyFieldBoost(shouldFieldBoosts![i], docId, ScoreTerm(
                                shouldFactors![i].Idf, shouldFactors[i].K1BOverAvgDL, shouldFactors[i].CollectionProb,
                                shouldEnums[i].Freq, docLength));
                        }
                    }

                    collector.Collect(docBase + docId, score);
                }
            }
            else
            {
                // Should-only: streaming OR merge across all Should PostingsEnums.
                const int HeapThreshold = 64;
                var localShouldEnums = shouldEnums!;

                // WAND path: check capability, then use block-max scoring.
                bool useWand = _config.EnableBlockMaxWand && mustNotCount == 0;
                if (useWand)
                {
                    for (int i = 0; i < shouldCount; i++)
                    {
                        if (!localShouldEnums[i].HasBlockMetadata)
                        {
                            useWand = false;
                            break;
                        }
                    }
                }

                if (useWand)
                {
                    ExecuteShouldOnlyWand(localShouldEnums, shouldFieldLens!, shouldFieldBoosts!,
                        shouldFactors!, shouldFields!, reader, hasDeletions, ref collector,
                        shouldCount);
                }
                else if (shouldCount <= HeapThreshold)
                {
                    Span<int> currentDocs = stackalloc int[shouldCount];
                    for (int i = 0; i < shouldCount; i++)
                        currentDocs[i] = localShouldEnums[i].MoveNext() ? localShouldEnums[i].DocId : int.MaxValue;

                    while (true)
                    {
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
                                    currentDocs[i] = localShouldEnums[i].MoveNext() ? localShouldEnums[i].DocId : int.MaxValue;
                            }
                            continue;
                        }

                        float score = 0f;
                        for (int i = 0; i < shouldCount; i++)
                        {
                            if (currentDocs[i] == minDoc)
                            {
                                int docLength = shouldFieldLens![i] is { } fl && (uint)minDoc < (uint)fl.Length ? fl[minDoc] : 1;
                                score += ApplyFieldBoost(shouldFieldBoosts![i], minDoc, ScoreTerm(
                                    shouldFactors![i].Idf, shouldFactors[i].K1BOverAvgDL, shouldFactors[i].CollectionProb,
                                    localShouldEnums[i].Freq, docLength));
                                currentDocs[i] = localShouldEnums[i].MoveNext() ? localShouldEnums[i].DocId : int.MaxValue;
                            }
                        }

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
                else
                {
                    ExecuteShouldOnlyHeap(localShouldEnums, shouldFieldLens!, shouldFieldBoosts!, shouldFactors!,
                        mustNotEnums, reader, ref collector, docBase, hasDeletions, shouldCount, mustNotCount);
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
                collector.Collect(docBase + d, scores[d]);
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
                    long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;
                    float avgDocLength = Stats.GetAvgFieldLength(tq.Field);
                    var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, tq.Field);
                    reader.TryGetFieldLengths(tq.Field, out var fieldLengths);
                    // For selective queries (fewer than 2 batches), use an inline loop to avoid
                    // stackalloc + two-pass batch overhead. The batch+SIMD path only pays off
                    // when there are many matches per term (high docFreq).
                    const int batchSize = 128;
                    if (docFreq < batchSize * 2)
                    {
                        while (postings.MoveNext())
                        {
                            int docId = postings.DocId;
                            if (!reader.IsLive(docId)) continue;
                            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                                ? fieldLengths[docId] : 1;
                            float score = ScoreTerm(f1, f2, f3, postings.Freq, docLength);
                            score = ApplyFieldBoost(reader, docId, tq.Field, score);
                            results.Add(new ScoreDoc(docId, score));
                        }
                    }
                    else
                    {
                        bool useBm25Batch = _similarity is Bm25Similarity;
                        Span<int> docIds = stackalloc int[batchSize];
                        Span<int> termFreqs = stackalloc int[batchSize];
                        Span<int> docLengths = stackalloc int[batchSize];
                        Span<float> batchScores = stackalloc float[batchSize];
                        int batchCount = 0;
                        while (postings.MoveNext())
                        {
                            int docId = postings.DocId;
                            if (!reader.IsLive(docId)) continue;
                            docIds[batchCount] = docId;
                            termFreqs[batchCount] = postings.Freq;
                            docLengths[batchCount] = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                                ? fieldLengths[docId] : 1;
                            batchCount++;
                            if (batchCount == batchSize)
                            {
                                if (useBm25Batch)
                                {
                                    Bm25Scorer.ScorePrecomputedBatch(f1, f2,
                                        termFreqs, docLengths, batchScores);
                                }
                                else
                                {
                                    for (int j = 0; j < batchSize; j++)
                                        batchScores[j] = ScoreTerm(f1, f2, f3, termFreqs[j], docLengths[j]);
                                }
                                for (int j = 0; j < batchSize; j++)
                                {
                                    float score = ApplyFieldBoost(reader, docIds[j], tq.Field, batchScores[j]);
                                    results.Add(new ScoreDoc(docIds[j], score));
                                }
                                batchCount = 0;
                            }
                        }
                        if (batchCount > 0)
                        {
                            if (useBm25Batch)
                            {
                                Bm25Scorer.ScorePrecomputedBatch(f1, f2,
                                    termFreqs.Slice(0, batchCount), docLengths.Slice(0, batchCount),
                                    batchScores.Slice(0, batchCount));
                            }
                            else
                            {
                                for (int j = 0; j < batchCount; j++)
                                    batchScores[j] = ScoreTerm(f1, f2, f3, termFreqs[j], docLengths[j]);
                            }
                            for (int j = 0; j < batchCount; j++)
                            {
                                float score = ApplyFieldBoost(reader, docIds[j], tq.Field, batchScores[j]);
                                results.Add(new ScoreDoc(docIds[j], score));
                            }
                        }
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
                        results.Add(new ScoreDoc(r.DocId, ApplyFieldBoost(reader, r.DocId, rq.Field, rqScore)));
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

    // --- Should-only WAND for block-max skipping ---

    private void ExecuteShouldOnlyWand(
        PostingsEnum[] shouldEnums,
        int[]?[] shouldFieldLens,
        float[]?[] shouldFieldBoosts,
        (float Idf, float K1BOverAvgDL, float CollectionProb)[] shouldFactors,
        string[] shouldFields,
        SegmentReader reader,
        bool hasDeletions,
        ref TopNCollector collector,
        int shouldCount)
    {
        var scorers = new BlockMaxWandScorer.TermScorer[shouldCount];

        for (int i = 0; i < shouldCount; i++)
        {
            var blockEnum = shouldEnums[i].BlockEnum;
            var (f1, f2, f3) = shouldFactors[i];
            float avgDl = Stats.GetAvgFieldLength(shouldFields[i]);

            scorers[i] = new BlockMaxWandScorer.TermScorer(
                blockEnum, f1, f2, f3,
                _similarity.ScoreLmPrecomputed,
                avgDl,
                shouldFieldLens[i], shouldFieldBoosts[i]);
        }

        var wand = new BlockMaxWandScorer(scorers);
        wand.ScoreInto(ref collector, hasDeletions ? reader.IsLive : null);
    }

    // --- Should-only heap merge for large clause counts (MoreLikeThis, etc.) ---

    private void ExecuteShouldOnlyHeap(
        PostingsEnum[] se, int[]?[] sfl, float[]?[] sfb,
        (float Idf, float K1BOverAvgDL, float CollectionProb)[] shouldFactors,
        PostingsEnum[]? mustNotEnums,
        SegmentReader reader, ref TopNCollector collector,
        int docBase, bool hasDeletions, int shouldCount, int mustNotCount)
    {
        Span<int> heapDocs = stackalloc int[shouldCount];
        Span<int> heapIdx = stackalloc int[shouldCount];
        int heapSize = 0;

        // Build initial heap
        for (int i = 0; i < shouldCount; i++)
        {
            if (se[i].MoveNext())
            {
                heapDocs[heapSize] = se[i].DocId;
                heapIdx[heapSize] = i;
                heapSize++;
                SiftUp(heapDocs, heapIdx, heapSize - 1);
            }
        }

        while (heapSize > 0)
        {
            int minDoc = heapDocs[0];
            float score = 0f;
            bool anyLive = false;

            // Extract all enums at minDoc from the heap root.
            while (heapSize > 0 && heapDocs[0] == minDoc)
            {
                int idx = PopRoot(heapDocs, heapIdx, ref heapSize);

                if (!hasDeletions || reader.IsLive(minDoc))
                {
                    anyLive = true;
                    int docLength = sfl[idx] is { } fl && (uint)minDoc < (uint)fl.Length ? fl[minDoc] : 1;
                    score += ApplyFieldBoost(sfb[idx], minDoc, ScoreTerm(
                        shouldFactors[idx].Idf, shouldFactors[idx].K1BOverAvgDL,
                        shouldFactors[idx].CollectionProb, se[idx].Freq, docLength));
                }

                // Advance and re-insert if not exhausted.
                if (se[idx].MoveNext())
                    InsertHeap(heapDocs, heapIdx, ref heapSize, se[idx].DocId, idx);
            }

            // Check MustNot.
            if (anyLive)
            {
                bool excluded = false;
                for (int i = 0; i < mustNotCount; i++)
                {
                    if (mustNotEnums![i].Advance(minDoc) && mustNotEnums[i].DocId == minDoc)
                    { excluded = true; break; }
                }
                if (!excluded)
                    collector.Collect(docBase + minDoc, score);
            }
        }
    }

    // --- Binary min-heap helpers ---

    private static void SiftUp(Span<int> heapDocs, Span<int> heapIdx, int idx)
    {
        int doc = heapDocs[idx];
        int enumIdx = heapIdx[idx];
        while (idx > 0)
        {
            int parent = (idx - 1) >> 1;
            if (heapDocs[parent] <= doc) break;
            heapDocs[idx] = heapDocs[parent];
            heapIdx[idx] = heapIdx[parent];
            idx = parent;
        }
        heapDocs[idx] = doc;
        heapIdx[idx] = enumIdx;
    }

    private static void SiftDown(Span<int> heapDocs, Span<int> heapIdx, int idx, int heapSize)
    {
        int doc = heapDocs[idx];
        int enumIdx = heapIdx[idx];
        while (true)
        {
            int child = (idx << 1) + 1;
            if (child >= heapSize) break;
            if (child + 1 < heapSize && heapDocs[child + 1] < heapDocs[child])
                child++;
            if (doc <= heapDocs[child]) break;
            heapDocs[idx] = heapDocs[child];
            heapIdx[idx] = heapIdx[child];
            idx = child;
        }
        heapDocs[idx] = doc;
        heapIdx[idx] = enumIdx;
    }

    /// <summary>Removes and returns the enum index at the heap root, then restores the heap invariant.</summary>
    private static int PopRoot(Span<int> heapDocs, Span<int> heapIdx, ref int heapSize)
    {
        int result = heapIdx[0];
        heapSize--;
        if (heapSize > 0)
        {
            heapDocs[0] = heapDocs[heapSize];
            heapIdx[0] = heapIdx[heapSize];
            SiftDown(heapDocs, heapIdx, 0, heapSize);
        }
        return result;
    }

    /// <summary>Inserts a doc ID and enum index into the heap.</summary>
    private static void InsertHeap(Span<int> heapDocs, Span<int> heapIdx, ref int heapSize, int docId, int enumIdx)
    {
        heapDocs[heapSize] = docId;
        heapIdx[heapSize] = enumIdx;
        SiftUp(heapDocs, heapIdx, heapSize);
        heapSize++;
    }
}
