
using System.Numerics;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Searcher.Internal;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing specialised query execution methods (Prefix, Wildcard, Fuzzy, Range, Regex, etc.).
/// </summary>
public sealed partial class IndexSearcher
{
    [ThreadStatic] private static float[]? t_patternScores;
    [ThreadStatic] private static bool[]? t_patternSeen;
    [ThreadStatic] private static int[]? t_patternDocIds;
    [ThreadStatic] private static int[]? t_patternCounts;
    [ThreadStatic] private static float[]? t_patternScratchScores;
    [ThreadStatic] private static int[]? t_patternScratchDocIds;

    private void ExecuteMatchAllDocsQuery(MatchAllDocsQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        float score = query.Boost;
        int docBase = reader.DocBase;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (reader.IsLive(docId))
                collector.Collect(docBase + docId, score);
        }
    }

    private static void ExecuteMatchNoDocsQuery(MatchNoDocsQuery query, SegmentReader reader, ref TopNCollector collector)
    {
    }

    private void ExecuteFieldExistsQuery(FieldExistsQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        float score = query.Boost;
        int docBase = reader.DocBase;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (reader.IsLive(docId) && reader.HasFieldValue(query.Field, docId))
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }

    private void ExecuteTermInSetQuery(TermInSetQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Terms.Count == 0)
            return;

        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        int docCount = 0;

        try
        {
            foreach (var qualifiedTerm in query.QualifiedTerms)
            {
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                while (postings.MoveNextUnchecked(out int docId, out _))
                {
                    if (!reader.IsLive(docId) || seen[docId])
                        continue;

                    seen[docId] = true;
                    docIds[docCount++] = docId;
                }
            }

            int docBase = reader.DocBase;
            float score = query.Boost;
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }
        }
        finally
        {
            for (int i = 0; i < docCount; i++)
            {
                seen[docIds[i]] = false;
                docIds[i] = 0;
            }
        }
    }

    private void ExecuteTermsQuery(TermsQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        int docCount = 0;

        try
        {
            foreach (var qualifiedTerm in query.QualifiedTerms)
            {
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                while (postings.MoveNextUnchecked(out int docId, out _))
                {
                    if (!reader.IsLive(docId) || seen[docId])
                        continue;

                    seen[docId] = true;
                    docIds[docCount++] = docId;
                }
            }

            int docBase = reader.DocBase;
            float score = query.Boost;
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }
        }
        finally
        {
            for (int i = 0; i < docCount; i++)
            {
                seen[docIds[i]] = false;
                docIds[i] = 0;
            }
        }
    }

    private void ExecuteSynonymQuery(
        SynonymQuery query,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector)
    {
        var frequencies = EnsureScratch(ref t_patternCounts, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;
        int blendedDocFreq = 0;
        long collectionFreq = 0;

        for (int i = 0; i < query.Terms.Count; i++)
        {
            string term = query.Terms[i];
            int docFreq = globalDFs.GetValueOrDefault((query.Field, term));
            if (docFreq > blendedDocFreq)
                blendedDocFreq = docFreq;
            if (RequiresCollectionStatistics(query.Field))
                collectionFreq += GetGlobalCollectionFreq(query.QualifiedTerms[i]);
        }

        if (blendedDocFreq == 0)
            return;

        try
        {
            foreach (var qualifiedTerm in query.QualifiedTerms)
            {
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                while (postings.MoveNextUnchecked(out int docId, out int frequency))
                {
                    if (!reader.IsLive(docId))
                        continue;
                    if (!seen[docId])
                    {
                        seen[docId] = true;
                        docIds[docCount++] = docId;
                    }
                    frequencies[docId] += frequency;
                }
            }

            float avgDocLength = Stats.GetAvgFieldLength(query.Field);
            var (f1, f2, f3) = ComputeTermFactors(
                blendedDocFreq, avgDocLength, collectionFreq, query.Field);
            reader.TryGetFieldLengths(query.Field, out var fieldLengths);
            reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
            int docBase = reader.DocBase;

            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId]
                    : 1;
                float score = ScoreTerm(
                    f1, f2, f3, frequencies[docId], docLength, query.Field) * query.Boost;
                collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }
        }
        finally
        {
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                frequencies[docId] = 0;
                seen[docId] = false;
                docIds[i] = 0;
            }
        }
    }

    private void ExecutePointInSetQuery(PointInSetQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Points.Count == 0)
            return;

        var pointSet = query.Points.ToHashSet();
        var matches = reader.GetNumericPointsInSet(query.Field, pointSet);
        if (matches.Count == 0)
            return;

        int docBase = reader.DocBase;
        float score = query.Boost;
        foreach (var match in matches)
            collector.Collect(docBase + match.DocId, ApplyFieldBoost(reader, match.DocId, query.Field, score));
    }

    private void ExecuteInt64PointInSetQuery(Int64PointInSetQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Points.Count == 0)
            return;

        var pointSet = query.Points.ToHashSet();
        var matches = reader.GetInt64PointsInSet(query.Field, pointSet);
        if (matches.Count == 0)
            return;

        int docBase = reader.DocBase;
        float score = query.Boost;
        foreach (var match in matches)
            collector.Collect(docBase + match.DocId, ApplyFieldBoost(reader, match.DocId, query.Field, score));
    }

    private static bool IsWithinBinaryRange(ReadOnlySpan<byte> value, BinaryRangeQuery query)
    {
        if (query.Lower is { } lower)
        {
            int comparison = value.SequenceCompareTo(lower.Span);
            if (comparison < 0 || (comparison == 0 && !query.IncludeLower))
                return false;
        }
        if (query.Upper is { } upper)
        {
            int comparison = value.SequenceCompareTo(upper.Span);
            if (comparison > 0 || (comparison == 0 && !query.IncludeUpper))
                return false;
        }
        return true;
    }

    private void ExecuteBinaryRangeQuery(
        BinaryRangeQuery query,
        SegmentReader reader,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId) ||
                !reader.TryGetBinaryDocValues(query.Field, docId, out var values))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (!IsWithinBinaryRange(value, query))
                    continue;
                collector.Collect(
                    docBase + docId,
                    ApplyFieldBoost(reader, docId, query.Field, score));
                break;
            }
        }
    }

    private void ExecuteBinaryPointInSetQuery(
        BinaryPointInSetQuery query,
        SegmentReader reader,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId) ||
                !reader.TryGetBinaryDocValues(query.Field, docId, out var values))
            {
                continue;
            }

            foreach (var value in values)
            {
                if (!query.Contains(value))
                    continue;
                collector.Collect(
                    docBase + docId,
                    ApplyFieldBoost(reader, docId, query.Field, score));
                break;
            }
        }
    }

    private void ExecuteCombinedFieldsQuery(
        CombinedFieldsQuery query,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector)
    {
        if (query.Fields.Count == 0 || query.Terms.Count == 0)
            return;

        var totalScores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seenDocs = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var matchedTermCounts = EnsureScratch(ref t_patternCounts, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        var termPseudoFrequencies = EnsureScratch(ref t_patternScratchScores, reader.MaxDoc);
        var termDocIds = EnsureScratch(ref t_patternScratchDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            foreach (var term in query.Terms)
            {
                int termDocCount = 0;
                foreach (var field in query.Fields)
                {
                    float avgFieldLength = Stats.GetAvgFieldLength(field);
                    using var postings = reader.GetPostingsEnum(string.Concat(field, "\x00", term));
                    while (postings.MoveNext())
                    {
                        int docId = postings.DocId;
                        if (!reader.IsLive(docId))
                            continue;

                        if (termPseudoFrequencies[docId] == 0f)
                            termDocIds[termDocCount++] = docId;

                        float fieldWeight = query.GetFieldWeight(field) * reader.GetFieldBoost(docId, field);
                        int docLength = reader.GetFieldLength(docId, field);
                        termPseudoFrequencies[docId] += Bm25Scorer.NormaliseFieldTermFrequency(
                            postings.Freq,
                            docLength,
                            avgFieldLength,
                            fieldWeight);
                    }
                }

                if (termDocCount == 0)
                    continue;

                int unionDocFreq = globalDFs.TryGetValue((CombinedFieldsDocFreqKey, term), out int precomputedDocFreq)
                    ? precomputedDocFreq
                    : ComputeCombinedFieldUnionDocFreq(query, term);
                float idf = Bm25Scorer.Idf(_totalDocCount, unionDocFreq);
                for (int i = 0; i < termDocCount; i++)
                {
                    int docId = termDocIds[i];
                    float score = Bm25Scorer.ScoreCombinedWithIdf(idf, termPseudoFrequencies[docId]);
                    if (score <= 0f)
                    {
                        termPseudoFrequencies[docId] = 0f;
                        termDocIds[i] = 0;
                        continue;
                    }

                    totalScores[docId] += score;
                    matchedTermCounts[docId]++;
                    if (!seenDocs[docId])
                    {
                        seenDocs[docId] = true;
                        docIds[docCount++] = docId;
                    }

                    termPseudoFrequencies[docId] = 0f;
                    termDocIds[i] = 0;
                }
            }

            int docBase = reader.DocBase;
            float queryBoost = query.Boost;
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                if (matchedTermCounts[docId] >= query.MinimumShouldMatch)
                    collector.Collect(docBase + docId, totalScores[docId] * queryBoost);
            }
        }
        finally
        {
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                totalScores[docId] = 0f;
                seenDocs[docId] = false;
                matchedTermCounts[docId] = 0;
                docIds[i] = 0;
            }
        }
    }

    private int ComputeCombinedFieldUnionDocFreq(CombinedFieldsQuery query, string term)
    {
        int total = 0;
        foreach (var reader in _readers)
        {
            var docs = new HashSet<int>();
            foreach (var field in query.Fields)
            {
                using var postings = reader.GetPostingsEnum(string.Concat(field, "\x00", term));
                while (postings.MoveNext())
                {
                    if (reader.IsLive(postings.DocId))
                        docs.Add(postings.DocId);
                }
            }

            total += docs.Count;
        }

        return total;
    }

    private void ExecuteRangeQuery(RangeQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost;
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        var localCollector = collector;
        if (reader.VisitNumericRange(query.Field, query.Min, query.Max, (docId, value) =>
        {
            if (IsWithinRange(value, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                localCollector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
        }))
        {
            collector = localCollector;
            return;
        }

        var fieldSet = new HashSet<string> { query.Field };
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId))
                continue;

            if (reader.TryGetNumericValue(query.Field, docId, out var numericValue))
            {
                if (IsWithinRange(
                        numericValue, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                    collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
                continue;
            }

            var stored = reader.GetStoredFields(docId, fieldSet);
            if (stored.TryGetValue(query.Field, out var values) && values.Count > 0 && double.TryParse(values[0], out var val))
            {
                if (IsWithinRange(val, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                    localCollector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
            }
        }

        collector = localCollector;
    }

    private void ExecuteInt64RangeQuery(Int64RangeQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = Math.Abs(query.Boost - 1.0f) > 1e-6f ? query.Boost : 1.0f;
        var localCollector = collector;
        if (reader.VisitInt64Range(query.Field, query.Min, query.Max, (docId, value) =>
        {
            if (IsWithinRange(value, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }))
        {
            collector = localCollector;
            return;
        }

        var intFieldSet = new HashSet<string> { query.Field };
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId))
                continue;

            if (reader.TryGetInt64Value(query.Field, docId, out var int64Value))
            {
                if (IsWithinRange(
                        int64Value, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                    collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
                continue;
            }

            var stored = reader.GetStoredFields(docId, intFieldSet);
            if (stored.TryGetValue(query.Field, out var values) && values.Count > 0 && long.TryParse(values[0], out var val))
            {
                if (IsWithinRange(val, query.Min, query.Max, query.IncludeMin, query.IncludeMax))
                    localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
            }
        }

        collector = localCollector;
    }

    private static bool IsWithinRange<T>(
        T value,
        T min,
        T max,
        bool includeMin,
        bool includeMax)
        where T : IComparisonOperators<T, T, bool> =>
        (includeMin ? value >= min : value > min) &&
        (includeMax ? value <= max : value < max);

    private void ExecutePrefixQuery(PrefixQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var qualifiedPrefix = $"{query.Field}\x00{query.Prefix}";
        float boost = query.Boost;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            if (globalDFs.Count == 0)
            {
                var matchingOffsets = reader.GetTermOffsetsWithPrefix(qualifiedPrefix);
                if (matchingOffsets.Count == 0) return;

                foreach (var postingsOffset in matchingOffsets)
                {
                    using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                    if (postings.IsExhausted) continue;

                    var (f1, f2, f3) = ComputeTermFactors(postings.DocFreq, avgDocLength, 0, query.Field);
                    AccumulatePostingsScores(reader, postings, f1, f2, f3, query.Field,
                        fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
                }

                CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
                return;
            }

            var matchingTerms = reader.GetTermsWithPrefix(qualifiedPrefix);
            if (matchingTerms.Count == 0) return;

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
                long collectionFreq = RequiresCollectionStatistics(query.Field)
                    ? GetGlobalCollectionFreq(qualifiedTerm)
                    : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                AccumulatePostingsScores(reader, postings, f1, f2, f3, query.Field,
                    fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
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

        // Extract leading literal prefix before the first wildcard to narrow FST subtree.
        // Only use prefix narrowing when the prefix is at least 2 characters — for 1-char
        // prefixes the FST subtree is too broad and the filtering overhead dominates.
        var leadingPrefix = GetLeadingLiteralPrefix(query.Pattern);
        bool usePrefixNarrowing = leadingPrefix.Length >= 2;
        var fieldPrefix = usePrefixNarrowing
            ? $"{query.Field}\x00{leadingPrefix}"
            : $"{query.Field}\x00";
        float boost = query.Boost;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            if (globalDFs.Count == 0)
            {
                List<long> matchingOffsets;
                if (!usePrefixNarrowing)
                {
                    matchingOffsets = reader.GetTermOffsetsMatching(fieldPrefix, query.Pattern.AsSpan());
                }
                else
                {
                    matchingOffsets = reader.GetTermOffsetsMatchingWithPrefix(
                        query.Field, leadingPrefix, query.Pattern.AsSpan());
                }
                if (matchingOffsets.Count == 0) return;

                foreach (var postingsOffset in matchingOffsets)
                {
                    using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                    if (postings.IsExhausted) continue;
                    var (f1, f2, f3) = ComputeTermFactors(postings.DocFreq, avgDocLength, 0, query.Field);
                    AccumulatePostingsScores(reader, postings, f1, f2, f3, query.Field,
                        fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
                }

                CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
                return;
            }

            List<(string Term, long Offset)> matchingTerms;
            if (!usePrefixNarrowing)
            {
                matchingTerms = reader.GetTermsMatching(fieldPrefix, query.Pattern.AsSpan());
            }
            else
            {
                matchingTerms = reader.GetTermsMatchingWithPrefix(
                    query.Field, leadingPrefix, query.Pattern.AsSpan());
            }
            if (matchingTerms.Count == 0) return;

            foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
            {
                using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                if (postings.IsExhausted) continue;

                int docFreq = postings.DocFreq;
                var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
                docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), docFreq);
                long collectionFreq = RequiresCollectionStatistics(query.Field)
                    ? GetGlobalCollectionFreq(qualifiedTerm)
                    : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                AccumulatePostingsScores(reader, postings, f1, f2, f3, query.Field,
                    fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
        }
    }

    private void AccumulatePostingsScores(SegmentReader reader, PostingsEnum postings,
        float f1, float f2, float f3, string field,
        int[]? fieldLengths, float[]? fieldBoosts, float boost,
        float[] scores, bool[] seen, int[] docIds, ref int docCount)
    {
        while (postings.MoveNextUnchecked(out int docId, out int tf))
        {
            if (!reader.IsLive(docId)) continue;

            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = ScoreTerm(f1, f2, f3, tf, docLength, field);
            score *= boost;
            score = ApplyFieldBoost(fieldBoosts, docId, score);
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

    /// <summary>
    /// Extracts the leading literal prefix of a wildcard pattern up to (but not including)
    /// the first <c>*</c> or <c>?</c> wildcard. Returns an empty string if the pattern
    /// starts with a wildcard. Used to narrow FST subtree walks.
    /// </summary>
    private static string GetLeadingLiteralPrefix(string pattern)
    {
        int end = 0;
        while (end < pattern.Length && pattern[end] is not '*' and not '?')
            end++;
        return end == 0 ? string.Empty : pattern[..end];
    }

    /// <summary>
    /// Extracts a literal prefix from a regex pattern. Walks the pattern from the start,
    /// collecting literal characters until a regex metacharacter is encountered. Handles
    /// escape sequences (\X) and the anchor (^) by skipping them. Stops at ., *, +, ?, [, (, {, |, $.
    /// </summary>
    private static bool TryGetRegexLiteralPrefix(string pattern, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrEmpty(pattern))
            return false;

        int start = 0;
        if (pattern[0] == '^')
            start = 1;

        int end = start;
        while (end < pattern.Length)
        {
            char c = pattern[end];
            if (c == '\\')
            {
                // Escaped character — consume the backslash and add the next char literally.
                end++;
                if (end < pattern.Length)
                    end++;
                continue;
            }
            if (c is '.' or '*' or '+' or '?' or '[' or '(' or '{' or '|' or '$')
                break;
            end++;
        }

        int length = end - start;
        if (length == 0)
            return false;

        // Unescape the literal prefix to get the actual bytes.
        var sb = new System.Text.StringBuilder(length);
        for (int i = start; i < end; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < end)
            {
                i++;
                sb.Append(pattern[i]);
            }
            else
            {
                sb.Append(c);
            }
        }

        prefix = sb.ToString();
        return prefix.Length > 0;
    }

    private static bool TryGetSimpleContainsRegexLiteral(string pattern, out string literal)
    {
        literal = string.Empty;
        if (pattern.Length <= 4 || !pattern.StartsWith(".*", StringComparison.Ordinal) ||
            !pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = pattern.AsSpan(2, pattern.Length - 4);
        if (candidate.Length == 0)
            return false;

        for (int i = 0; i < candidate.Length; i++)
        {
            char c = candidate[i];
            if (c > 0x7F || c is '.' or '*' or '+' or '?' or '[' or '(' or '{' or '|' or '\\' or '^' or '$')
                return false;
        }

        literal = candidate.ToString();
        return true;
    }

    private void ExecuteFuzzyQuery(FuzzyQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var fieldPrefix = $"{query.Field}\x00";
        var matchingTerms = reader.GetFuzzyMatches(fieldPrefix, query.Term.AsSpan(), query.MaxEdits, query.MaxExpansions);
        if (matchingTerms.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
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
                long collectionFreq = RequiresCollectionStatistics(query.Field)
                    ? GetGlobalCollectionFreq(qualifiedTerm)
                    : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                while (postings.MoveNextUnchecked(out int docId, out int tf))
                {
                    if (!reader.IsLive(docId)) continue;

                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(
                        f1, f2, f3, tf, docLength, query.Field) * distanceFactor;
                    score *= boost;
                    score = ApplyFieldBoost(fieldBoosts, docId, score);
                    if (!seen[docId])
                    {
                        seen[docId] = true;
                        docIds[docCount++] = docId;
                    }
                    scores[docId] += score;
                }
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
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
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);

        foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
        {
            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
            int docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), postings.DocFreq);
            long collectionFreq = RequiresCollectionStatistics(query.Field)
                ? GetGlobalCollectionFreq(qualifiedTerm)
                : 0;
            var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

            while (postings.MoveNextUnchecked(out int docId, out int tf))
            {
                if (!reader.IsLive(docId)) continue;

                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = ScoreTerm(f1, f2, f3, tf, docLength, query.Field);
                score *= boost;
                score = ApplyFieldBoost(fieldBoosts, docId, score);
                collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecuteRegexpQuery(RegexpQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var fieldPrefix = $"{query.Field}\x00";
        var regex = query.CompiledRegex;

        // Fast path: literal-contains pattern (e.g. .*nation.*)
        if (globalDFs.Count == 0 &&
            (regex.Options & System.Text.RegularExpressions.RegexOptions.IgnoreCase) == 0 &&
            TryGetSimpleContainsRegexLiteral(query.Pattern, out var literal))
        {
            ExecuteRegexpContainsQuery(query, reader, fieldPrefix, literal, ref collector);
            return;
        }

        // Fast path: prefix-literal extraction (e.g. gov.*ment → prefix "gov", mark.* → prefix "mark")
        // Enumerate only the FST subtree matching the literal prefix, then regex-verify each term.
        if (TryGetRegexLiteralPrefix(query.Pattern, out var prefix) && prefix.Length >= 1)
        {
            var qualifiedPrefix = string.Concat(fieldPrefix, prefix);
            var candidateTerms = reader.GetTermsWithPrefix(qualifiedPrefix);
            ExecuteRegexpFromCandidates(query, reader, ref collector, candidateTerms, fieldPrefix, regex);
            return;
        }

        // Fallback: full FST enumeration with regex filter (expensive — only for complex patterns).
        var matchingTerms = reader.GetTermsMatchingRegex(fieldPrefix, regex);
        if (matchingTerms.Count == 0) return;
        ExecuteRegexpFromCandidates(query, reader, ref collector, matchingTerms, fieldPrefix, regex);
    }

    private void ExecuteRegexpFromCandidates(RegexpQuery query, SegmentReader reader,
        ref TopNCollector collector,
        List<(string Term, long Offset)> candidates,
        string fieldPrefix,
        System.Text.RegularExpressions.Regex regex)
    {
        float boost = query.Boost;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);

        foreach (var (qualifiedTerm, postingsOffset) in candidates)
        {
            // Verify the term matches the full regex pattern.
            var bareTerm = qualifiedTerm.AsSpan(fieldPrefix.Length);
            if (!regex.IsMatch(bareTerm))
                continue;

            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
            int docFreq = postings.DocFreq;
            long collectionFreq = RequiresCollectionStatistics(query.Field)
                ? GetGlobalCollectionFreq(qualifiedTerm)
                : 0;
            var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

            while (postings.MoveNextUnchecked(out int docId, out int tf))
            {
                if (!reader.IsLive(docId)) continue;

                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = ScoreTerm(f1, f2, f3, tf, docLength, query.Field);
                score *= boost;
                score = ApplyFieldBoost(fieldBoosts, docId, score);
                collector.Collect(docBase + docId, score);
            }
        }
    }

    private void ExecuteRegexpContainsQuery(RegexpQuery query, SegmentReader reader, string fieldPrefix,
        string literal, ref TopNCollector collector)
    {
        var matchingOffsets = reader.GetTermOffsetsContaining(fieldPrefix, literal.AsSpan());
        if (matchingOffsets.Count == 0) return;

        float boost = query.Boost;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        var scores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            foreach (var postingsOffset in matchingOffsets)
            {
                using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
                if (postings.IsExhausted) continue;

                var (f1, f2, f3) = ComputeTermFactors(postings.DocFreq, avgDocLength, 0, query.Field);
                AccumulatePostingsScores(reader, postings, f1, f2, f3, query.Field,
                    fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
        }
    }

    private void ExecuteConstantScoreQuery(ConstantScoreQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        // Execute the inner query into a temporary collector, then replace scores.
        var innerCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
        ExecuteQuery(query.Inner, reader, globalDFs, ref innerCollector);

        float constantScore = query.ConstantScore;
        constantScore *= query.Boost;

        foreach (var sd in innerCollector.ToTopDocs().ScoreDocs)
            collector.Collect(sd.DocId, ApplyFieldBoost(reader, sd.DocId - reader.DocBase, query.Field, constantScore));
    }

    private void ExecuteDisjunctionMaxQuery(DisjunctionMaxQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.Disjuncts.Count == 0) return;
        if (TryExecuteDisjunctionMaxTermQuery(query, reader, globalDFs, ref collector))
            return;

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
            score *= boost;
            collector.Collect(docId, score);
        }
    }

    private bool TryExecuteDisjunctionMaxTermQuery(DisjunctionMaxQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        foreach (var disjunct in query.Disjuncts)
        {
            if (disjunct is not TermQuery)
                return false;
        }

        var maxScores = EnsureScratch(ref t_patternScores, reader.MaxDoc);
        var otherScores = EnsureScratch(ref t_patternScratchScores, reader.MaxDoc);
        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;

        try
        {
            foreach (var disjunct in query.Disjuncts)
            {
                var termQuery = (TermQuery)disjunct;
                var qualifiedTerm = termQuery.CachedQualifiedTerm ??= string.Concat(termQuery.Field, "\x00", termQuery.Term);
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                if (postings.IsExhausted)
                    continue;

                int docFreq = globalDFs.GetValueOrDefault((termQuery.Field, termQuery.Term), postings.DocFreq);
                float avgDocLength = Stats.GetAvgFieldLength(termQuery.Field);
                long collectionFreq = RequiresCollectionStatistics(termQuery.Field)
                    ? GetGlobalCollectionFreq(qualifiedTerm)
                    : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, termQuery.Field);
                reader.TryGetFieldLengths(termQuery.Field, out var fieldLengths);
                reader.TryGetFieldBoosts(termQuery.Field, out var fieldBoosts);
                float termBoost = termQuery.Boost;

                while (postings.MoveNextUnchecked(out int docId, out int termFrequency))
                {
                    if (!reader.IsLive(docId))
                        continue;

                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(
                        f1, f2, f3, termFrequency, docLength, termQuery.Field);
                    score *= termBoost;
                    score = ApplyFieldBoost(fieldBoosts, docId, score);

                    if (!seen[docId])
                    {
                        seen[docId] = true;
                        docIds[docCount++] = docId;
                        maxScores[docId] = score;
                        continue;
                    }

                    if (score > maxScores[docId])
                    {
                        otherScores[docId] += maxScores[docId];
                        maxScores[docId] = score;
                    }
                    else
                    {
                        otherScores[docId] += score;
                    }
                }
            }

            float tieBreaker = query.TieBreakerMultiplier;
            float queryBoost = query.Boost;
            int docBase = reader.DocBase;
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                float score = maxScores[docId] + tieBreaker * otherScores[docId];
                score *= queryBoost;
                collector.Collect(docBase + docId, score);
            }

            return true;
        }
        finally
        {
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                maxScores[docId] = 0f;
                otherScores[docId] = 0f;
                seen[docId] = false;
                docIds[i] = 0;
            }
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
            if (!Rowles.LeanCorpus.Search.Simd.SimdVectorOps.NormaliseInPlace(normalisedQuery))
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
            var shortlist = graph!.Search(searchVec, options.ToTraversalOptions(), out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(hnswSw.Elapsed, stats.NodesVisited);
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                var docVector = reader.GetVector(query.Field, hit.DocId);
                if (docVector is null || docVector.Length == 0) continue;
                float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
                similarity = ApplyFieldBoost(reader, hit.DocId, query.Field, similarity);
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
            var shortlist = graph!.Search(searchVec, options.ToTraversalOptions(), out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(hnswSw.Elapsed, stats.NodesVisited);
            if (shortlist.Count == 0) return;
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                var docVector = reader.GetVector(query.Field, hit.DocId);
                if (docVector is null || docVector.Length == 0) continue;
                float similarity = VectorQuery.CosineSimilarity(queryVec, docVector);
                similarity = ApplyFieldBoost(reader, hit.DocId, query.Field, similarity);
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
            similarity = ApplyFieldBoost(reader, docId, query.Field, similarity);
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
            similarity = ApplyFieldBoost(reader, docId, query.Field, similarity);
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

        // Execute the filter query. ExecuteQuery adds reader.DocBase to produce
        // global doc IDs; subtract it back to recover segment-local IDs for the bitmap.
        int docBase = reader.DocBase;
        ExecuteQuery(filter, reader, globalDFs, ref inner);

        var bitmap = new Util.RoaringBitmap();
        var topDocs = inner.ToTopDocs();
        foreach (var sd in topDocs.ScoreDocs)
            bitmap.Add(sd.DocId - docBase);
        return bitmap;
    }

    private void ExecuteFunctionScoreQuery(FunctionScoreQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.Inner is TermQuery tq)
        {
            ExecuteFunctionScoreForTermQuery(tq, query, reader, globalDFs, ref collector);
            return;
        }

        // Non-TermQuery inner: existing path
        var innerCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
        ExecuteQuery(query.Inner, reader, globalDFs, ref innerCollector);
        var innerDocs = innerCollector.ToTopDocs();

        int docBase = reader.DocBase;
        foreach (var sd in innerDocs.ScoreDocs)
        {
            if (query.ValuesSource.TryGetValue(this, sd.DocId, sd.Score, out double value))
            {
                float combined = FunctionScoreQuery.Combine(sd.Score, value, query.Mode);
                collector.Collect(sd.DocId, combined * query.Boost);
            }
            else
            {
                collector.Collect(sd.DocId, sd.Score * query.Boost);
            }
        }
    }

    /// <summary>Single-pass FunctionScore execution for TermQuery inners.
    /// Ingests BM25, combines with the numeric field value, then feeds the top-N collector.</summary>
    private void ExecuteFunctionScoreForTermQuery(TermQuery tq, FunctionScoreQuery fsq,
        SegmentReader reader, Dictionary<(string Field, string Term), int> globalDFs,
        ref TopNCollector collector)
    {
        var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
        using var postings = reader.GetPostingsEnum(qt);

        // Use global DF for correct IDF in multi-segment indexes.
        int docFreq = globalDFs.GetValueOrDefault((tq.Field, tq.Term), postings.DocFreq);
        long globalCollectionFreq = RequiresCollectionStatistics(tq.Field)
            ? GetGlobalCollectionFreq(qt)
            : 0;
        float avgDocLength = Stats.GetAvgFieldLength(tq.Field);
        var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, globalCollectionFreq, tq.Field);
        float boost = tq.Boost;

        int docBase = reader.DocBase;
        bool hasDeletions = reader.HasDeletions;
        reader.TryGetFieldLengths(tq.Field, out var fieldLengths);
        double[]? numericValues = null;
        Util.RoaringBitmap? numericPresence = null;
        bool hasNumericDocValues = fsq.IsSimpleNumericField
            && reader.TryGetNumericDocValues(
                fsq.NumericField,
                out numericValues,
                out numericPresence);

        while (postings.MoveNextUnchecked(out int docId, out int tf))
        {
            if (hasDeletions && !reader.IsLive(docId)) continue;

            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = ScoreTerm(f1, f2, f3, tf, docLength, tq.Field);
            if (boost != 1.0f) score *= boost;
            score = ApplyFieldBoost(reader, docId, tq.Field, score);

            // Modify the field-boosted BM25 score using the numeric doc value.
            if (hasNumericDocValues)
            {
                if ((uint)docId < (uint)numericValues!.Length
                    && (numericPresence is null || numericPresence.Contains(docId)))
                {
                    score = FunctionScoreQuery.Combine(score, numericValues[docId], fsq.Mode);
                }
            }
            else if (fsq.IsSimpleNumericField
                && reader.TryGetNumericValue(fsq.NumericField, docId, out double fieldValue))
            {
                score = FunctionScoreQuery.Combine(score, fieldValue, fsq.Mode);
            }
            else if (!fsq.IsSimpleNumericField
                && fsq.ValuesSource.TryGetValue(
                    this,
                    docBase + docId,
                    score,
                    out double sourceValue))
            {
                score = FunctionScoreQuery.Combine(score, sourceValue, fsq.Mode);
            }

            collector.Collect(docBase + docId, score * fsq.Boost);
        }
    }

    private void ExecuteGeoBoundingBoxQuery(GeoBoundingBoxQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (!TryCreateGeoBounds(query.MinLat, query.MaxLat, query.MinLon, query.MaxLon, out GeoQueryBounds bounds))
            return;

        bool hasPackedField = HasCompatiblePackedPointField(reader, query.Field);
        var matched = new RoaringBitmap();
        var documentIds = new List<int>();

        if (hasPackedField)
            CollectPackedGeoBounds(reader, query.Field, bounds, matched, documentIds);

        // A merge may place pre-packed and packed documents in one segment. Scan the
        // legacy fields only when the exact point DocValues do not cover every document.
        if (!hasPackedField
            || !reader.HasBinaryDocValuesForEveryDocument(GeoPointDocValues.GetFieldName(query.Field)))
            CollectLegacyGeoBounds(reader, query.Field, bounds, matched, documentIds, hasPackedField);
        CollectGeoMatches(reader, query.Field, bounds, documentIds, query.Boost, ref collector);
    }

    private void ExecuteGeoDistanceQuery(GeoDistanceQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (!TryCreateGeoDistanceBounds(query, out GeoQueryBounds bounds))
            return;

        string latField = query.Field + "_lat";
        string lonField = query.Field + "_lon";
        if (HasSingleValuedPackedGeoField(reader, query.Field)
            && reader.HasNumericField(latField)
            && reader.HasNumericField(lonField))
        {
            int fastDocBase = reader.DocBase;
            float fastScore = query.Boost;
            foreach (var (docId, latitude) in reader.GetNumericRange(
                         latField, bounds.MinimumLatitude, bounds.MaximumLatitude))
            {
                if (!reader.IsLive(docId)
                    || !reader.TryGetNumericValue(lonField, docId, out double longitude)
                    || !bounds.ContainsLongitude(longitude)
                    || GeoEncodingUtils.HaversineDistance(
                        query.CentreLat, query.CentreLon, latitude, longitude) > query.RadiusMetres)
                    continue;

                collector.Collect(
                    fastDocBase + docId,
                    ApplyFieldBoost(reader, docId, query.Field, fastScore));
            }

            return;
        }

        bool hasPackedField = HasCompatiblePackedPointField(reader, query.Field);
        var matched = new RoaringBitmap();
        var documentIds = new List<int>();

        if (hasPackedField)
            CollectPackedGeoBounds(reader, query.Field, bounds, matched, documentIds);

        bool hasLegacyGeoDocuments = !hasPackedField
            || !reader.HasBinaryDocValuesForEveryDocument(GeoPointDocValues.GetFieldName(query.Field));
        if (hasLegacyGeoDocuments)
            CollectLegacyGeoBounds(reader, query.Field, bounds, matched, documentIds, hasPackedField);

        int docBase = reader.DocBase;
        float score = query.Boost;
        string exactField = GeoPointDocValues.GetFieldName(query.Field);
        bool singleValuedPackedField = hasPackedField
            && reader.TryGetPackedBkdFieldMetadata(query.Field, out PackedBkdFieldMetadata metadata)
            && metadata.PointCount == metadata.DocumentCount;
        bool useNumericDocValues = singleValuedPackedField && !hasLegacyGeoDocuments;
        foreach (int docId in documentIds)
        {
            if (!reader.IsLive(docId))
                continue;

            double minimumDistance = double.PositiveInfinity;
            if (useNumericDocValues
                && reader.TryGetNumericValue(latField, docId, out double numericLatitude)
                && reader.TryGetNumericValue(lonField, docId, out double numericLongitude))
            {
                minimumDistance = GeoEncodingUtils.HaversineDistance(
                    query.CentreLat, query.CentreLon, numericLatitude, numericLongitude);
            }
            else if (reader.TryGetBinaryDocValues(exactField, docId, out var exactValues))
            {
                foreach (byte[] value in exactValues)
                {
                    if (!GeoPointDocValues.TryDecode(value, out double latitude, out double longitude))
                        throw new InvalidDataException($"Geo point DocValues for field '{query.Field}' are malformed.");

                    double distance = GeoEncodingUtils.HaversineDistance(
                        query.CentreLat, query.CentreLon, latitude, longitude);
                    if (distance < minimumDistance)
                        minimumDistance = distance;
                }
            }
            else if (reader.TryGetNumericValue(latField, docId, out double latitude)
                && reader.TryGetNumericValue(lonField, docId, out double longitude))
            {
                minimumDistance = GeoEncodingUtils.HaversineDistance(
                    query.CentreLat, query.CentreLon, latitude, longitude);
            }

            if (minimumDistance <= query.RadiusMetres)
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }

    private void ExecuteXYBoundingBoxQuery(XYBoundingBoxQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (!HasCompatiblePackedPointField(reader, query.Field))
            return;

        XYRectangle bounds = query.Bounds;
        Span<byte> minimum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> maximum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        XYEncodingUtils.Encode(bounds.MinX, minimum);
        XYEncodingUtils.Encode(bounds.MinY, minimum[PackedBkdConfig.FixedBytesPerDimension..]);
        XYEncodingUtils.Encode(bounds.MaxX, maximum);
        XYEncodingUtils.Encode(bounds.MaxY, maximum[PackedBkdConfig.FixedBytesPerDimension..]);

        var matched = new RoaringBitmap();
        var documentIds = new List<int>();
        var visitor = new PackedBkdBoundsVisitor(minimum, maximum, matched, documentIds);
        if (!reader.IntersectPackedBkd(query.Field, ref visitor))
            return;

        CollectXYMatches(reader, query.Field, documentIds, query.Boost, ref collector);
    }

    private void ExecuteXYDistanceQuery(XYDistanceQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (!HasCompatiblePackedPointField(reader, query.Field))
            return;

        double rawMinimumX = (double)query.Centre.X - query.Radius;
        double rawMinimumY = (double)query.Centre.Y - query.Radius;
        double rawMaximumX = (double)query.Centre.X + query.Radius;
        double rawMaximumY = (double)query.Centre.Y + query.Radius;
        var bounds = new XYRectangle(
            RoundMinimumCoordinate(rawMinimumX),
            RoundMinimumCoordinate(rawMinimumY),
            RoundMaximumCoordinate(rawMaximumX),
            RoundMaximumCoordinate(rawMaximumY));

        Span<byte> minimum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> maximum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        XYEncodingUtils.Encode(bounds.MinX, minimum);
        XYEncodingUtils.Encode(bounds.MinY, minimum[PackedBkdConfig.FixedBytesPerDimension..]);
        XYEncodingUtils.Encode(bounds.MaxX, maximum);
        XYEncodingUtils.Encode(bounds.MaxY, maximum[PackedBkdConfig.FixedBytesPerDimension..]);

        var matched = new RoaringBitmap();
        var documentIds = new List<int>();
        var visitor = new PackedBkdBoundsVisitor(minimum, maximum, matched, documentIds);
        if (!reader.IntersectPackedBkd(query.Field, ref visitor))
            return;

        double radiusSquared = (double)query.Radius * query.Radius;
        int docBase = reader.DocBase;
        float score = query.Boost;
        foreach (int docId in documentIds)
        {
            if (!reader.IsLive(docId)
                || !reader.TryGetBinaryDocValues(query.Field, docId, out var pointValues))
                continue;

            bool withinRadius = false;
            foreach (byte[] packedPoint in pointValues)
            {
                if (packedPoint.Length != 2 * PackedBkdConfig.FixedBytesPerDimension)
                    throw new InvalidDataException($"XY point DocValues for field '{query.Field}' are malformed.");

                double x = XYEncodingUtils.Decode(packedPoint);
                double y = XYEncodingUtils.Decode(packedPoint.AsSpan(PackedBkdConfig.FixedBytesPerDimension));
                double dx = x - query.Centre.X;
                double dy = y - query.Centre.Y;
                if (dx * dx + dy * dy <= radiusSquared)
                {
                    withinRadius = true;
                    break;
                }
            }

            if (withinRadius)
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }

    private static void CollectXYMatches(
        SegmentReader reader,
        string field,
        List<int> documentIds,
        float boost,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        foreach (int docId in documentIds)
        {
            if (reader.IsLive(docId))
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, field, boost));
        }
    }

    private static float RoundMinimumCoordinate(double value)
    {
        if (value <= -float.MaxValue)
            return -float.MaxValue;
        if (value >= float.MaxValue)
            return float.MaxValue;

        float rounded = (float)value;
        return rounded > value ? MathF.BitDecrement(rounded) : rounded;
    }

    private static float RoundMaximumCoordinate(double value)
    {
        if (value >= float.MaxValue)
            return float.MaxValue;
        if (value <= -float.MaxValue)
            return -float.MaxValue;

        float rounded = (float)value;
        return rounded < value ? MathF.BitIncrement(rounded) : rounded;
    }

    private static bool HasCompatiblePackedPointField(SegmentReader reader, string field)
        => reader.TryGetPackedBkdFieldMetadata(field, out PackedBkdFieldMetadata metadata)
            && metadata.Config.Dimensions == 2
            && metadata.Config.IndexedDimensions == 2
            && metadata.Config.BytesPerDimension == PackedBkdConfig.FixedBytesPerDimension;

    private static bool HasSingleValuedPackedGeoField(SegmentReader reader, string field)
        => HasCompatiblePackedPointField(reader, field)
            && reader.TryGetPackedBkdFieldMetadata(field, out PackedBkdFieldMetadata metadata)
            && metadata.PointCount == metadata.DocumentCount
            && reader.HasBinaryDocValuesForEveryDocument(GeoPointDocValues.GetFieldName(field));

    private static void CollectPackedGeoBounds(
        SegmentReader reader,
        string field,
        GeoQueryBounds bounds,
        RoaringBitmap matched,
        List<int> documentIds)
    {
        Span<byte> minimum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> maximum = stackalloc byte[2 * PackedBkdConfig.FixedBytesPerDimension];
        for (int rangeIndex = 0; rangeIndex < bounds.LongitudeRangeCount; rangeIndex++)
        {
            GeoLongitudeRange longitude = bounds.GetLongitudeRange(rangeIndex);
            GeoEncodingUtils.WriteLonSortable(longitude.Minimum, minimum);
            GeoEncodingUtils.WriteLatSortable(bounds.MinimumLatitude, minimum[PackedBkdConfig.FixedBytesPerDimension..]);
            GeoEncodingUtils.WriteLonSortable(longitude.Maximum, maximum);
            GeoEncodingUtils.WriteLatSortable(bounds.MaximumLatitude, maximum[PackedBkdConfig.FixedBytesPerDimension..]);

            var visitor = new PackedBkdBoundsVisitor(minimum, maximum, matched, documentIds);
            reader.IntersectPackedBkd(field, ref visitor);
        }
    }

    private static void CollectLegacyGeoBounds(
        SegmentReader reader,
        string field,
        GeoQueryBounds bounds,
        RoaringBitmap matched,
        List<int> documentIds,
        bool packedFieldPresent)
    {
        string latField = field + "_lat";
        string lonField = field + "_lon";
        var latitudeCandidates = reader.GetNumericRange(latField, bounds.MinimumLatitude, bounds.MaximumLatitude);
        if (latitudeCandidates.Count == 0)
            return;

        string exactField = GeoPointDocValues.GetFieldName(field);
        foreach (var (docId, _) in latitudeCandidates)
        {
            if (!reader.IsLive(docId))
                continue;
            if (packedFieldPresent && reader.TryGetBinaryDocValues(exactField, docId, out _))
                continue;
            if (!reader.TryGetNumericValue(lonField, docId, out double longitude)
                || !bounds.ContainsLongitude(longitude)
                || matched.Contains(docId))
                continue;

            matched.Add(docId);
            documentIds.Add(docId);
        }
    }

    private static void CollectGeoMatches(
        SegmentReader reader,
        string field,
        GeoQueryBounds bounds,
        List<int> documentIds,
        float boost,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        string exactField = GeoPointDocValues.GetFieldName(field);
        foreach (int docId in documentIds)
        {
            if (!reader.IsLive(docId))
                continue;

            bool matches = false;
            if (reader.TryGetBinaryDocValues(exactField, docId, out var exactValues))
            {
                foreach (byte[] value in exactValues)
                {
                    if (!GeoPointDocValues.TryDecode(value, out double latitude, out double longitude))
                        throw new InvalidDataException($"Geo point DocValues for field '{field}' are malformed.");
                    if (latitude >= bounds.MinimumLatitude && latitude <= bounds.MaximumLatitude
                        && bounds.ContainsLongitude(longitude))
                    {
                        matches = true;
                        break;
                    }
                }
            }
            else
            {
                matches = true;
            }

            if (matches)
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, field, boost));
        }
    }

    private static bool TryCreateGeoBounds(
        double minimumLatitude,
        double maximumLatitude,
        double minimumLongitude,
        double maximumLongitude,
        out GeoQueryBounds bounds)
    {
        bounds = default;
        if (!double.IsFinite(minimumLatitude) || !double.IsFinite(maximumLatitude)
            || !double.IsFinite(minimumLongitude) || !double.IsFinite(maximumLongitude)
            || minimumLatitude < -90 || maximumLatitude > 90 || minimumLatitude > maximumLatitude
            || minimumLongitude < -180 || minimumLongitude > 180
            || maximumLongitude < -180 || maximumLongitude > 180)
            return false;

        if (minimumLongitude <= maximumLongitude)
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(minimumLongitude, maximumLongitude), default, 1);
        }
        else
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(minimumLongitude, 180),
                new GeoLongitudeRange(-180, maximumLongitude), 2);
        }

        return true;
    }

    private static bool TryCreateGeoDistanceBounds(GeoDistanceQuery query, out GeoQueryBounds bounds)
    {
        bounds = default;
        if (!double.IsFinite(query.RadiusMetres) && !double.IsPositiveInfinity(query.RadiusMetres)
            || query.RadiusMetres < 0 || double.IsNaN(query.CentreLat) || double.IsNaN(query.CentreLon)
            || double.IsInfinity(query.CentreLat) || double.IsInfinity(query.CentreLon))
            return false;

        const double earthRadiusMetres = 6_371_000.0;
        double angularRadius = Math.Min(query.RadiusMetres / earthRadiusMetres, Math.PI);
        if (angularRadius >= Math.PI || query.CentreLat < -90 || query.CentreLat > 90
            || query.CentreLon < -180 || query.CentreLon > 180)
        {
            bounds = new GeoQueryBounds(-90, 90,
                new GeoLongitudeRange(-180, 180), default, 1);
            return true;
        }

        double centreLatitudeRadians = DegreesToRadians(query.CentreLat);
        double minimumLatitude = Math.Max(-90, query.CentreLat - RadiansToDegrees(angularRadius));
        double maximumLatitude = Math.Min(90, query.CentreLat + RadiansToDegrees(angularRadius));
        bool reachesPole = query.CentreLat + RadiansToDegrees(angularRadius) >= 90
            || query.CentreLat - RadiansToDegrees(angularRadius) <= -90;
        if (reachesPole)
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(-180, 180), default, 1);
            return true;
        }

        double longitudeRatio = Math.Sin(angularRadius) / Math.Cos(centreLatitudeRadians);
        double longitudeDelta = RadiansToDegrees(Math.Asin(Math.Clamp(longitudeRatio, -1, 1)));
        double west = query.CentreLon - longitudeDelta;
        double east = query.CentreLon + longitudeDelta;
        if (west < -180)
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(-180, east),
                new GeoLongitudeRange(west + 360, 180), 2);
        }
        else if (east > 180)
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(west, 180),
                new GeoLongitudeRange(-180, east - 360), 2);
        }
        else
        {
            bounds = new GeoQueryBounds(minimumLatitude, maximumLatitude,
                new GeoLongitudeRange(west, east), default, 1);
        }

        return true;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
    private static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);

    private readonly record struct GeoLongitudeRange(double Minimum, double Maximum);

    private readonly record struct GeoQueryBounds(
        double MinimumLatitude,
        double MaximumLatitude,
        GeoLongitudeRange FirstLongitudeRange,
        GeoLongitudeRange SecondLongitudeRange,
        int LongitudeRangeCount)
    {
        internal GeoLongitudeRange GetLongitudeRange(int index)
            => index switch
            {
                0 => FirstLongitudeRange,
                1 when LongitudeRangeCount > 1 => SecondLongitudeRange,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

        internal bool ContainsLongitude(double longitude)
        {
            for (int i = 0; i < LongitudeRangeCount; i++)
            {
                GeoLongitudeRange range = GetLongitudeRange(i);
                if (longitude >= range.Minimum && longitude <= range.Maximum)
                    return true;
            }

            return false;
        }
    }
}
