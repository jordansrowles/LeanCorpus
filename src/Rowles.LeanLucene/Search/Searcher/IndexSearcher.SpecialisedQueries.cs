using Rowles.LeanLucene.Codecs.Postings;

namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Partial class containing specialised query execution methods (Prefix, Wildcard, Fuzzy, Range, Regex, etc.).
/// </summary>
public sealed partial class IndexSearcher
{
    [ThreadStatic] private static float[]? t_patternScores;
    [ThreadStatic] private static bool[]? t_patternSeen;
    [ThreadStatic] private static int[]? t_patternDocIds;

    private void ExecuteRangeQuery(RangeQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        var rangeResults = reader.GetNumericRange(query.Field, query.Min, query.Max);
        if (rangeResults.Count > 0)
        {
            foreach (var r in rangeResults)
                collector.Collect(docBase + r.DocId, score);
            return;
        }

        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId)) continue;

            var stored = reader.GetStoredFields(docId);
            if (stored.TryGetValue(query.Field, out var values) && values.Count > 0 && double.TryParse(values[0], out var val))
            {
                if (val >= query.Min && val <= query.Max)
                    collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecutePrefixQuery(PrefixQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var qualifiedPrefix = $"{query.Field}\x00{query.Prefix}";
        var matchingTerms = reader.GetTermsWithPrefix(qualifiedPrefix);
        if (matchingTerms.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
            {
                using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                if (postings.IsExhausted) continue;

                int docFreq = postings.DocFreq;
                if (globalDFs.Count > 0)
                {
                    var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
                    docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), docFreq);
                }
                var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

                AccumulatePostingsScores(reader, postings, f1, f2, fieldLengths, boost, scores, seen, docIds, ref docCount);
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
        }
    }

    private void ExecuteWildcardQuery(WildcardQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (TryGetSimpleTrailingWildcardPrefix(query.Pattern, out var prefix))
        {
            var prefixQuery = new PrefixQuery(query.Field, prefix) { Boost = query.Boost };
            ExecutePrefixQuery(prefixQuery, reader, globalDFs, ref collector);
            return;
        }

        var fieldPrefix = $"{query.Field}\x00";
        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            if (globalDFs.Count == 0)
            {
                var matchingOffsets = reader.GetTermOffsetsMatching(fieldPrefix, query.Pattern.AsSpan());
                if (matchingOffsets.Count == 0) return;

                foreach (var postingsOffset in matchingOffsets)
                {
                    using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                    if (postings.IsExhausted) continue;

                    var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, postings.DocFreq, avgDocLength);
                    AccumulatePostingsScores(reader, postings, f1, f2, fieldLengths, boost, scores, seen, docIds, ref docCount);
                }

                CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
                return;
            }

            var matchingTerms = reader.GetTermsMatching(fieldPrefix, query.Pattern.AsSpan());
            if (matchingTerms.Count == 0) return;

            foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
            {
                using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                if (postings.IsExhausted) continue;

                int docFreq = postings.DocFreq;
                var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
                docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), docFreq);
                var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

                AccumulatePostingsScores(reader, postings, f1, f2, fieldLengths, boost, scores, seen, docIds, ref docCount);
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
        }
    }

    private void AccumulatePostingsScores(SegmentReader reader, PostingsEnum postings,
        float f1, float f2, int[]? fieldLengths, float boost,
        float[] scores, bool[] seen, int[] docIds, ref int docCount)
    {
        while (postings.MoveNext())
        {
            int docId = postings.DocId;
            if (!reader.IsLive(docId)) continue;

            int tf = postings.Freq;
            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = _similarity.ScorePrecomputed(f1, f2, tf, docLength);
            if (boost != 1.0f) score *= boost;
            if (!seen[docId])
            {
                seen[docId] = true;
                docIds[docCount++] = docId;
            }
            scores[docId] += score;
        }
    }

    private static void CollectAccumulatedScores(float[] scores, int[] docIds, int docCount, int docBase,
        ref TopNCollector collector)
    {
        for (int i = 0; i < docCount; i++)
        {
            int docId = docIds[i];
            collector.Collect(docBase + docId, scores[docId]);
        }
    }

    private static void ClearAccumulatedScores(float[] scores, bool[] seen, int[] docIds, int docCount)
    {
        for (int i = 0; i < docCount; i++)
        {
            int docId = docIds[i];
            scores[docId] = 0;
            seen[docId] = false;
            docIds[i] = 0;
        }
    }
    private static bool TryGetSimpleTrailingWildcardPrefix(string pattern, out string prefix)
    {
        prefix = string.Empty;
        if (pattern.Length == 0 || pattern[^1] != '*')
            return false;

        for (int i = 0; i < pattern.Length - 1; i++)
        {
            if (pattern[i] is '*' or '?')
                return false;
        }

        prefix = pattern[..^1];
        return true;
    }

    private void ExecuteFuzzyQuery(FuzzyQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var fieldPrefix = $"{query.Field}\x00";
        var matchingTerms = reader.GetFuzzyMatches(fieldPrefix, query.Term.AsSpan(), query.MaxEdits, query.MaxExpansions);
        if (matchingTerms.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;

        foreach (var (qualifiedTerm, postingsOffset, distance) in matchingTerms)
        {
            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            float distanceFactor = 1.0f - ((float)distance / (query.MaxEdits + 1));
            int docFreq = postings.DocFreq;
            if (globalDFs.Count > 0)
            {
                var termStr = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
                docFreq = globalDFs.GetValueOrDefault((query.Field, termStr), docFreq);
            }
            var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

            reader.TryGetFieldLengths(query.Field, out var fieldLengths);

            while (postings.MoveNext())
            {
                int docId = postings.DocId;
                if (!reader.IsLive(docId)) continue;

                int tf = postings.Freq;
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = _similarity.ScorePrecomputed(f1, f2, tf, docLength) * distanceFactor;
                if (boost != 1.0f) score *= boost;
                collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecuteTermRangeQuery(TermRangeQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var fieldPrefix = $"{query.Field}\x00";
        var matchingTerms = reader.GetTermsInRange(fieldPrefix, query.LowerTerm, query.UpperTerm,
            query.IncludeLower, query.IncludeUpper);
        if (matchingTerms.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;

        foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
        {
            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
            int docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), postings.DocFreq);
            var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

            reader.TryGetFieldLengths(query.Field, out var fieldLengths);

            while (postings.MoveNext())
            {
                int docId = postings.DocId;
                if (!reader.IsLive(docId)) continue;

                int tf = postings.Freq;
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = _similarity.ScorePrecomputed(f1, f2, tf, docLength);
                if (boost != 1.0f) score *= boost;
                collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecuteRegexpQuery(RegexpQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var fieldPrefix = $"{query.Field}\x00";
        var matchingTerms = reader.GetTermsMatchingRegex(fieldPrefix, query.CompiledRegex);
        if (matchingTerms.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;

        foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
        {
            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
            int docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), postings.DocFreq);
            var (f1, f2) = _similarity.PrecomputeFactors(_totalDocCount, docFreq, avgDocLength);

            reader.TryGetFieldLengths(query.Field, out var fieldLengths);

            while (postings.MoveNext())
            {
                int docId = postings.DocId;
                if (!reader.IsLive(docId)) continue;

                int tf = postings.Freq;
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = _similarity.ScorePrecomputed(f1, f2, tf, docLength);
                if (boost != 1.0f) score *= boost;
                collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecuteConstantScoreQuery(ConstantScoreQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        // Execute the inner query into a temporary collector, then replace scores.
        var innerCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
        ExecuteQuery(query.Inner, reader, globalDFs, ref innerCollector);

        float constantScore = query.ConstantScore;
        if (query.Boost != 1.0f) constantScore *= query.Boost;

        foreach (var sd in innerCollector.ToTopDocs().ScoreDocs)
            collector.Collect(sd.DocId, constantScore);
    }

    private void ExecuteDisjunctionMaxQuery(DisjunctionMaxQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.Disjuncts.Count == 0) return;

        // Collect per-docId: max score + all scores for tiebreaker
        var docScores = new Dictionary<int, (float Max, float OtherSum)>();

        foreach (var disjunct in query.Disjuncts)
        {
            var subCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
            ExecuteQuery(disjunct, reader, globalDFs, ref subCollector);

            foreach (var sd in subCollector.ToTopDocs().ScoreDocs)
            {
                if (docScores.TryGetValue(sd.DocId, out var existing))
                {
                    if (sd.Score > existing.Max)
                        docScores[sd.DocId] = (sd.Score, existing.OtherSum + existing.Max);
                    else
                        docScores[sd.DocId] = (existing.Max, existing.OtherSum + sd.Score);
                }
                else
                {
                    docScores[sd.DocId] = (sd.Score, 0f);
                }
            }
        }

        float tieBreaker = query.TieBreakerMultiplier;
        float boost = query.Boost;
        foreach (var (docId, (max, otherSum)) in docScores)
        {
            float score = max + tieBreaker * otherSum;
            if (boost != 1.0f) score *= boost;
            collector.Collect(docId, score);
        }
    }

    private void ExecuteVectorQuery(VectorQuery query, SegmentReader reader, ref TopNCollector collector)
        => ExecuteVectorQuery(query, reader, new Dictionary<(string Field, string Term), int>(), ref collector);

    private void ExecuteVectorQuery(
        VectorQuery query,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector)
    {
        if (!reader.HasVectors) return;

        int docBase = reader.DocBase;

        // Resolve filter (if any) to a docId bitmap and choose a strategy.
        Util.RoaringBitmap? filterBitmap = null;
        if (query.Filter is not null)
        {
            filterBitmap = ExecuteFilterToBitmap(query.Filter, reader, globalDFs);
            if (filterBitmap.Cardinality == 0) return;
        }

        var graph = reader.GetHnswGraph(query.Field);
        bool hasGraph = graph is not null && graph.NodeCount > 0;

        // Pre-compute query vector (and normalised variant for normalised fields).
        var queryVec = query.QueryVector;
        var fieldInfo = reader.Info.VectorFields.FirstOrDefault(f => f.FieldName == query.Field);
        bool normalised = fieldInfo is not null && fieldInfo.Normalised;
        float[]? normalisedQuery = null;
        if (normalised)
        {
            normalisedQuery = (float[])queryVec.Clone();
            if (!Rowles.LeanLucene.Search.Simd.SimdVectorOps.NormaliseInPlace(normalisedQuery))
                return;
        }

        // Filter strategy selection.
        if (filterBitmap is not null && hasGraph)
        {
            int liveCount = reader.MaxDoc;
            int matched = filterBitmap.Cardinality;
            double selectivity = liveCount > 0 ? (double)matched / liveCount : 1.0;

            // Highly selective: brute-force scan only matched docs (cheaper than graph traversal).
            if (matched < 64 || selectivity < 0.005)
            {
                BruteForceFilter(query, reader, filterBitmap, queryVec, docBase, ref collector);
                return;
            }

            // Moderately selective: pre-filter via allow-list.
            // Loose: post-filter with retry.
            var bitset = new Util.RoaringBitmapBitSet(filterBitmap);
            var options = selectivity < 0.05
                ? new HnswSearchOptions
                {
                    Ef = query.EfSearch,
                    TopK = query.TopK * query.OversamplingFactor,
                    AllowList = bitset,
                }
                : new HnswSearchOptions
                {
                    Ef = query.EfSearch,
                    TopK = query.TopK * query.OversamplingFactor,
                    PostFilterMask = bitset,
                };

            var searchVec = normalisedQuery ?? queryVec;
            var hnswSw = System.Diagnostics.Stopwatch.StartNew();
            var shortlist = graph!.Search(searchVec, options, out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(hnswSw.Elapsed, stats.NodesVisited);
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                var docVector = reader.GetVector(query.Field, hit.DocId);
                if (docVector is null || docVector.Length == 0) continue;
                float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
                collector.Collect(docBase + hit.DocId, similarity);
            }
            return;
        }

        // No filter, but HNSW present: two-phase search.
        if (hasGraph)
        {
            int shortlistSize = query.TopK * query.OversamplingFactor;
            var options = new HnswSearchOptions
            {
                Ef = query.EfSearch,
                TopK = shortlistSize,
            };
            var searchVec = normalisedQuery ?? queryVec;
            var hnswSw = System.Diagnostics.Stopwatch.StartNew();
            var shortlist = graph!.Search(searchVec, options, out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(hnswSw.Elapsed, stats.NodesVisited);
            if (shortlist.Count == 0) return;
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                var docVector = reader.GetVector(query.Field, hit.DocId);
                if (docVector is null || docVector.Length == 0) continue;
                float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
                collector.Collect(docBase + hit.DocId, similarity);
            }
            return;
        }

        // Flat-scan fallback (with optional filter).
        if (filterBitmap is not null)
        {
            BruteForceFilter(query, reader, filterBitmap, queryVec, docBase, ref collector);
            return;
        }

        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId)) continue;
            var docVector = reader.GetVector(query.Field, docId);
            if (docVector is null || docVector.Length == 0) continue;
            float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
            collector.Collect(docBase + docId, similarity);
        }
    }

    private void BruteForceFilter(
        VectorQuery query,
        SegmentReader reader,
        Util.RoaringBitmap filterBitmap,
        float[] queryVec,
        int docBase,
        ref TopNCollector collector)
    {
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!filterBitmap.Contains(docId)) continue;
            if (!reader.IsLive(docId)) continue;
            var docVector = reader.GetVector(query.Field, docId);
            if (docVector is null || docVector.Length == 0) continue;
            float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
            collector.Collect(docBase + docId, similarity);
        }
    }

    private Util.RoaringBitmap ExecuteFilterToBitmap(
        Query filter,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs)
    {
        int cap = Math.Max(reader.MaxDoc, 1);
        var inner = new TopNCollector(cap);
        int savedDocBase = reader.DocBase;
        try
        {
            // Execute filter against this segment with docBase=0 so collected ids are local.
            reader.DocBase = 0;
            ExecuteQuery(filter, reader, globalDFs, ref inner);
        }
        finally
        {
            reader.DocBase = savedDocBase;
        }

        var bitmap = new Util.RoaringBitmap();
        var topDocs = inner.ToTopDocs();
        foreach (var sd in topDocs.ScoreDocs)
            bitmap.Add(sd.DocId);
        return bitmap;
    }

    private void ExecuteFunctionScoreQuery(FunctionScoreQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        // Execute inner query into temporary collector.
        var innerCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
        ExecuteQuery(query.Inner, reader, globalDFs, ref innerCollector);
        var innerDocs = innerCollector.ToTopDocs();

        int docBase = reader.DocBase;
        foreach (var sd in innerDocs.ScoreDocs)
        {
            int localDocId = sd.DocId - docBase;
            if (reader.TryGetNumericValue(query.NumericField, localDocId, out double fieldValue))
            {
                float combined = FunctionScoreQuery.Combine(sd.Score, fieldValue, query.Mode);
                collector.Collect(sd.DocId, combined * query.Boost);
            }
            else
            {
                collector.Collect(sd.DocId, sd.Score * query.Boost);
            }
        }
    }

    private void ExecuteGeoBoundingBoxQuery(GeoBoundingBoxQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        string latField = query.Field + "_lat";
        string lonField = query.Field + "_lon";

        // Use numeric range index on lat to get candidates
        var latCandidates = reader.GetNumericRange(latField, query.MinLat, query.MaxLat);
        if (latCandidates.Count == 0) return;

        foreach (var (docId, lat) in latCandidates)
        {
            if (!reader.IsLive(docId)) continue;
            if (!reader.TryGetNumericValue(lonField, docId, out double lon)) continue;
            if (lon >= query.MinLon && lon <= query.MaxLon)
                collector.Collect(docBase + docId, score);
        }
    }

    private void ExecuteGeoDistanceQuery(GeoDistanceQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        string latField = query.Field + "_lat";
        string lonField = query.Field + "_lon";

        // Compute a conservative bounding box for the distance to narrow candidates
        double latDelta = query.RadiusMetres / 111_320.0; // ~111km per degree lat
        double lonDelta = query.RadiusMetres / (111_320.0 * Math.Cos(query.CentreLat * Math.PI / 180.0));
        double minLat = query.CentreLat - latDelta;
        double maxLat = query.CentreLat + latDelta;

        var latCandidates = reader.GetNumericRange(latField, minLat, maxLat);
        if (latCandidates.Count == 0) return;

        double minLon = query.CentreLon - lonDelta;
        double maxLon = query.CentreLon + lonDelta;

        foreach (var (docId, lat) in latCandidates)
        {
            if (!reader.IsLive(docId)) continue;
            if (!reader.TryGetNumericValue(lonField, docId, out double lon)) continue;
            if (lon < minLon || lon > maxLon) continue;

            // Precise Haversine check
            double dist = GeoEncodingUtils.HaversineDistance(query.CentreLat, query.CentreLon, lat, lon);
            if (dist <= query.RadiusMetres)
                collector.Collect(docBase + docId, score);
        }
    }
}
