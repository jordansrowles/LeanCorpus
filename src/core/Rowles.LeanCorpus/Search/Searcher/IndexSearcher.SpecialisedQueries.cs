
using System.Numerics;
using Rowles.LeanCorpus.Codecs.Postings;

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
                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
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
                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
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

    private static void ExecuteSparseImpactQuery(
        SparseImpactQuery query,
        SegmentReader reader,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId) ||
                !reader.TryGetBinaryDocValues(query.Field, docId, out var values))
            {
                continue;
            }

            foreach (byte[] value in values)
            {
                if (collector.IsFull)
                {
                    float upperBound = Rowles.LeanCorpus.Document.Fields.SparseImpactPayload.UpperBound(
                        value,
                        query.MaximumImpact);
                    if (upperBound <= collector.MinScore)
                        continue;
                }
                float score = Rowles.LeanCorpus.Document.Fields.SparseImpactPayload.Score(value, query.Impacts);
                if (score <= 0f)
                    continue;
                collector.Collect(
                    docBase + docId,
                    ApplyFieldBoost(reader, docId, query.Field, score * query.Boost));
                break;
            }
        }
    }

    private static void ExecuteLateInteractionQuery(
        LateInteractionQuery query,
        SegmentReader reader,
        ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId) ||
                !reader.TryGetBinaryDocValues(query.Field, docId, out var values))
            {
                continue;
            }

            foreach (byte[] value in values)
            {
                float score = Rowles.LeanCorpus.Document.Fields.MultiVectorPayload.Score(
                    value,
                    query.QueryVectors,
                    query.Weights);
                if (score > 0f)
                    collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score * query.Boost));
                break;
            }
        }
    }

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
        if (fieldInfo is not null && fieldInfo.Dimension != queryVec.Length)
            throw new ArgumentException(
                $"Query vector dimension {queryVec.Length} does not match field '{query.Field}' dimension {fieldInfo.Dimension}.",
                nameof(query));
        bool normalised = fieldInfo is not null && fieldInfo.Normalised;
        var similarityFunction = fieldInfo?.Similarity ?? Codecs.Vectors.VectorSimilarityFunction.Cosine;
        int candidateCount = fieldInfo?.Quantisation ==
            Codecs.Vectors.VectorQuantisation.ProductQuantisation
            ? (int)Math.Min(
                int.MaxValue,
                Math.Max((long)query.CandidateCount, (long)query.TopK * 4))
            : query.CandidateCount;
        var scorer = new PreparedVectorScorer(queryVec, similarityFunction);
        var scoreVector = new Rowles.LeanCorpus.Index.Segment.VectorBlockScorer(scorer.Score);
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
            var plan = PlanFilteredVectorSearch(reader.MaxDoc, filterBitmap.Cardinality, query);
            if (plan.Strategy == VectorFilterStrategy.ExactFilterScan)
            {
                var exactSw = System.Diagnostics.Stopwatch.StartNew();
                BruteForceFilter(
                    query,
                    reader,
                    filterBitmap,
                    scorer,
                    docBase,
                    ref collector);
                exactSw.Stop();
                RecordVectorExecution(
                    Diagnostics.VectorExecutionStrategy.ExactFilterScan,
                    fieldInfo,
                    exactCandidateSet: true,
                    filterBitmap.Cardinality,
                    filterBitmap.Cardinality,
                    exactSw.Elapsed,
                    TimeSpan.Zero);
                return;
            }

            // Moderately selective: pre-filter via allow-list.
            // Loose: post-filter with retry.
            var bitset = new Util.RoaringBitmapBitSet(filterBitmap);
            var segmentSeeds = GetSegmentSeeds(query, reader);
            var options = plan.Strategy == VectorFilterStrategy.HnswAllowList
                ? new HnswSearchOptions
                {
                    Ef = query.EfSearch,
                    TopK = candidateCount,
                    AllowList = bitset,
                    MaxVisitedNodes = query.MaxVisitedNodes,
                    MaxFilterExpansion = Math.Min(64, candidateCount),
                    EntryPoints = segmentSeeds,
                }
                : new HnswSearchOptions
                {
                    Ef = query.EfSearch,
                    TopK = candidateCount,
                    PostFilterMask = bitset,
                    MaxVisitedNodes = query.MaxVisitedNodes,
                    EntryPoints = segmentSeeds,
                };

            var searchVec = normalisedQuery ?? queryVec;
            var hnswSw = System.Diagnostics.Stopwatch.StartNew();
            var shortlist = graph!.Search(searchVec, options.ToTraversalOptions(), out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(
                hnswSw.Elapsed,
                stats.NodesVisited,
                stats.RetryCount);
            RecordHnswDiagnostics(
                stats.NodesVisited,
                stats.RetryCount,
                stats.BudgetExhausted);
            var rerankSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                if (!reader.TryScoreVector(query.Field, hit.DocId, scoreVector, out float similarity)) continue;
                if (reader.TryGetVectorErrorBound(query.Field, hit.DocId, out float errorBound))
                    RecordVectorErrorBound(errorBound);
                if (!MeetsVectorSimilarityThreshold(query, similarity)) continue;
                similarity = ApplyFieldBoost(reader, hit.DocId, query.Field, similarity);
                collector.Collect(docBase + hit.DocId, similarity);
            }
            rerankSw.Stop();
            RecordVectorExecution(
                plan.Strategy == VectorFilterStrategy.HnswAllowList
                    ? Diagnostics.VectorExecutionStrategy.HnswAllowList
                    : Diagnostics.VectorExecutionStrategy.HnswPostFilter,
                fieldInfo,
                exactCandidateSet: false,
                shortlist.Count,
                filterBitmap.Cardinality,
                hnswSw.Elapsed,
                rerankSw.Elapsed);
            return;
        }

        // No filter, but HNSW present: two-phase search.
        if (hasGraph)
        {
            var segmentSeeds = GetSegmentSeeds(query, reader);
            var options = new HnswSearchOptions
            {
                Ef = query.EfSearch,
                TopK = candidateCount,
                MaxVisitedNodes = query.MaxVisitedNodes,
                EntryPoints = segmentSeeds,
            };
            var searchVec = normalisedQuery ?? queryVec;
            var hnswSw = System.Diagnostics.Stopwatch.StartNew();
            var shortlist = graph!.Search(searchVec, options.ToTraversalOptions(), out var stats);
            hnswSw.Stop();
            _config.Metrics.RecordHnswSearch(
                hnswSw.Elapsed,
                stats.NodesVisited,
                stats.RetryCount);
            RecordHnswDiagnostics(
                stats.NodesVisited,
                stats.RetryCount,
                stats.BudgetExhausted);
            if (shortlist.Count == 0) return;
            var rerankSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var hit in shortlist)
            {
                if (!reader.IsLive(hit.DocId)) continue;
                if (!reader.TryScoreVector(query.Field, hit.DocId, scoreVector, out float similarity)) continue;
                if (reader.TryGetVectorErrorBound(query.Field, hit.DocId, out float errorBound))
                    RecordVectorErrorBound(errorBound);
                if (!MeetsVectorSimilarityThreshold(query, similarity)) continue;
                similarity = ApplyFieldBoost(reader, hit.DocId, query.Field, similarity);
                collector.Collect(docBase + hit.DocId, similarity);
            }
            rerankSw.Stop();
            RecordVectorExecution(
                Diagnostics.VectorExecutionStrategy.Hnsw,
                fieldInfo,
                exactCandidateSet: false,
                shortlist.Count,
                reader.MaxDoc,
                hnswSw.Elapsed,
                rerankSw.Elapsed);
            return;
        }

        // Flat-scan fallback (with optional filter).
        if (filterBitmap is not null)
        {
            var exactSw = System.Diagnostics.Stopwatch.StartNew();
            BruteForceFilter(
                query,
                reader,
                filterBitmap,
                scorer,
                docBase,
                ref collector);
            exactSw.Stop();
            RecordVectorExecution(
                Diagnostics.VectorExecutionStrategy.ExactFilterScan,
                fieldInfo,
                exactCandidateSet: true,
                filterBitmap.Cardinality,
                filterBitmap.Cardinality,
                exactSw.Elapsed,
                TimeSpan.Zero);
            return;
        }

        var flatSw = System.Diagnostics.Stopwatch.StartNew();
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!reader.IsLive(docId)) continue;
            if (!reader.TryScoreVector(query.Field, docId, scoreVector, out float similarity)) continue;
            if (reader.TryGetVectorErrorBound(query.Field, docId, out float errorBound))
                RecordVectorErrorBound(errorBound);
            if (!MeetsVectorSimilarityThreshold(query, similarity)) continue;
            similarity = ApplyFieldBoost(reader, docId, query.Field, similarity);
            collector.Collect(docBase + docId, similarity);
        }
        flatSw.Stop();
        RecordVectorExecution(
            Diagnostics.VectorExecutionStrategy.ExactFlatScan,
            fieldInfo,
            exactCandidateSet: true,
            reader.MaxDoc,
            reader.MaxDoc,
            flatSw.Elapsed,
            TimeSpan.Zero);
    }

    private void BruteForceFilter(
        VectorQuery query,
        SegmentReader reader,
        Util.RoaringBitmap filterBitmap,
        PreparedVectorScorer scorer,
        int docBase,
        ref TopNCollector collector)
    {
        var scoreVector = new Rowles.LeanCorpus.Index.Segment.VectorBlockScorer(scorer.Score);
        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!filterBitmap.Contains(docId)) continue;
            if (!reader.IsLive(docId)) continue;
            if (!reader.TryScoreVector(query.Field, docId, scoreVector, out float similarity)) continue;
            if (reader.TryGetVectorErrorBound(query.Field, docId, out float errorBound))
                RecordVectorErrorBound(errorBound);
            if (!MeetsVectorSimilarityThreshold(query, similarity)) continue;
            similarity = ApplyFieldBoost(reader, docId, query.Field, similarity);
            collector.Collect(docBase + docId, similarity);
        }
    }

    private static bool MeetsVectorSimilarityThreshold(VectorQuery query, float score) =>
        query is not VectorSimilarityQuery threshold ||
        score >= threshold.MinimumSimilarity;

    private void RecordVectorExecution(
        Diagnostics.VectorExecutionStrategy strategy,
        Rowles.LeanCorpus.Index.Segment.VectorFieldInfo? fieldInfo,
        bool exactCandidateSet,
        int candidateCount,
        int eligibleCount,
        TimeSpan candidateGenerationElapsed,
        TimeSpan rerankingElapsed)
    {
        var precision = fieldInfo is null ||
                        fieldInfo.Quantisation == Codecs.Vectors.VectorQuantisation.None ||
                        fieldInfo.RetainsFullPrecision
            ? Diagnostics.VectorScorePrecision.ExactFloat32
            : Diagnostics.VectorScorePrecision.ReconstructedQuantised;
        _config.Metrics.RecordVectorExecution(new Diagnostics.VectorExecutionMetrics(
            strategy,
            precision,
            exactCandidateSet,
            candidateCount,
            eligibleCount,
            candidateGenerationElapsed,
            rerankingElapsed));
    }

    private enum VectorFilterStrategy
    {
        ExactFilterScan,
        HnswAllowList,
        HnswPostFilter,
    }

    private readonly record struct VectorFilterPlan(
        VectorFilterStrategy Strategy,
        long ExactCost,
        long AllowListCost,
        long PostFilterCost);

    private static VectorFilterPlan PlanFilteredVectorSearch(
        int liveDocumentCount,
        int matchedDocumentCount,
        VectorQuery query)
    {
        if (matchedDocumentCount <= 0)
            return new(VectorFilterStrategy.ExactFilterScan, 0, 0, 0);

        int live = Math.Max(1, liveDocumentCount);
        int candidatePool = Math.Max(query.EfSearch, query.CandidateCount);
        double selectivity = Math.Clamp((double)matchedDocumentCount / live, 1d / live, 1d);

        // These are calibrated work estimates, not elapsed-time predictions. Exact
        // filtering scores each matching vector from a mapped block. An HNSW visit
        // additionally pays heap, visited-set, filtering, and bridge-expansion costs.
        // The 16x traversal factor is deliberately conservative: the 100k-document
        // planner workload showed that the previous comparison-only estimate selected
        // an allow-list at 10% selectivity even though exact scanning was 5-7x faster.
        const int hnswTraversalWorkMultiplier = 16;
        long exactCost = matchedDocumentCount;
        long allowListCost = Math.Min(
            live,
            (long)candidatePool * hnswTraversalWorkMultiplier +
            Math.Min(matchedDocumentCount, candidatePool * 2L));
        int expectedPostFilterPasses = Math.Clamp(
            (int)Math.Ceiling(1d / selectivity),
            1,
            4);
        long postFilterCost = Math.Min(
            live,
            (long)candidatePool * expectedPostFilterPasses * hnswTraversalWorkMultiplier);

        VectorFilterStrategy strategy = exactCost <= allowListCost && exactCost <= postFilterCost
            ? VectorFilterStrategy.ExactFilterScan
            : allowListCost <= postFilterCost
                ? VectorFilterStrategy.HnswAllowList
                : VectorFilterStrategy.HnswPostFilter;
        return new(strategy, exactCost, allowListCost, postFilterCost);
    }

    private static IReadOnlyList<int>? GetSegmentSeeds(VectorQuery query, SegmentReader reader)
    {
        if (query is not SeededVectorQuery seeded || seeded.SeedDocumentIds.Count == 0)
            return null;

        int first = reader.DocBase;
        int lastExclusive = first + reader.MaxDoc;
        var local = new List<int>();
        foreach (int globalDocId in seeded.SeedDocumentIds)
        {
            if (globalDocId < first || globalDocId >= lastExclusive)
                continue;
            int localDocId = globalDocId - first;
            if (reader.IsLive(localDocId) && reader.GetVector(query.Field, localDocId) is not null)
                local.Add(localDocId);
        }
        return local;
    }

    private Util.RoaringBitmap ExecuteFilterToBitmap(
        Query filter,
        SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs)
    {
        var key = new FilterDocSetCacheKey(reader.Info.SegmentId, filter);
        using var cached = _filterDocSetCache.Acquire(
            key,
            () => new FilterDocSetCacheEntry(BuildFilterBitmap(filter, reader, globalDFs)));
        return cached.Value.Bitmap;
    }

    private Util.RoaringBitmap BuildFilterBitmap(
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
        int docBase = reader.DocBase;
        float score = query.Boost;
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
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }

    private void ExecuteGeoDistanceQuery(GeoDistanceQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost;
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
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }
}
