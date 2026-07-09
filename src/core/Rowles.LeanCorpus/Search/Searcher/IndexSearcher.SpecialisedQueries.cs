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
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
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
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
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
            float score = query.Boost != 1.0f ? query.Boost : 1.0f;
            for (int i = 0; i < docCount; i++)
            {
                int docId = docIds[i];
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
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

    private void ExecutePointInSetQuery(PointInSetQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        if (query.Points.Count == 0)
            return;

        var pointSet = query.Points.ToHashSet();
        var matches = reader.GetNumericPointsInSet(query.Field, pointSet);
        if (matches.Count == 0)
            return;

        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
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
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        foreach (var match in matches)
            collector.Collect(docBase + match.DocId, ApplyFieldBoost(reader, match.DocId, query.Field, score));
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
                    float avgFieldLength = _stats.GetAvgFieldLength(field);
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
            float queryBoost = query.Boost != 1.0f ? query.Boost : 1.0f;
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
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        var localCollector = collector;
        if (reader.VisitNumericRange(query.Field, query.Min, query.Max, (docId, _) =>
            localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score))))
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
                if (numericValue >= query.Min && numericValue <= query.Max)
                    collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
                continue;
            }

            var stored = reader.GetStoredFields(docId, fieldSet);
            if (stored.TryGetValue(query.Field, out var values) && values.Count > 0 && double.TryParse(values[0], out var val))
            {
                if (val >= query.Min && val <= query.Max)
                    localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
            }
        }

        collector = localCollector;
    }

    private void ExecuteInt64RangeQuery(Int64RangeQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        var localCollector = collector;
        if (reader.VisitInt64Range(query.Field, query.Min, query.Max, (docId, _) =>
            localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score))))
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
                if (int64Value >= query.Min && int64Value <= query.Max)
                    collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
                continue;
            }

            var stored = reader.GetStoredFields(docId, intFieldSet);
            if (stored.TryGetValue(query.Field, out var values) && values.Count > 0 && long.TryParse(values[0], out var val))
            {
                if (val >= query.Min && val <= query.Max)
                    localCollector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
            }
        }

        collector = localCollector;
    }

    private void ExecutePrefixQuery(PrefixQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        var qualifiedPrefix = $"{query.Field}\x00{query.Prefix}";
        float boost = query.Boost;
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
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
                    AccumulatePostingsScores(reader, postings, f1, f2, f3, fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
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
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                AccumulatePostingsScores(reader, postings, f1, f2, f3, fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
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
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
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
                    AccumulatePostingsScores(reader, postings, f1, f2, f3, fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
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
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                AccumulatePostingsScores(reader, postings, f1, f2, f3, fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
            }

            CollectAccumulatedScores(scores, docIds, docCount, docBase, ref collector);
        }
        finally
        {
            ClearAccumulatedScores(scores, seen, docIds, docCount);
        }
    }

    private void AccumulatePostingsScores(SegmentReader reader, PostingsEnum postings,
        float f1, float f2, float f3, int[]? fieldLengths, float[]? fieldBoosts, float boost,
        float[] scores, bool[] seen, int[] docIds, ref int docCount)
    {
        while (postings.MoveNext())
        {
            int docId = postings.DocId;
            if (!reader.IsLive(docId)) continue;

            int tf = postings.Freq;
            int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                ? fieldLengths[docId] : 1;
            float score = ScoreTerm(f1, f2, f3, tf, docLength);
            if (boost != 1.0f) score *= boost;
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
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
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
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if (!reader.IsLive(docId)) continue;

                    int tf = postings.Freq;
                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, tf, docLength) * distanceFactor;
                    if (boost != 1.0f) score *= boost;
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
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;

        foreach (var (qualifiedTerm, postingsOffset) in matchingTerms)
        {
            using var postings = reader.GetPostingsEnumAtOffset(postingsOffset);
            if (postings.IsExhausted) continue;

            var termPart = qualifiedTerm.AsSpan(query.Field.Length + 1).ToString();
            int docFreq = globalDFs.GetValueOrDefault((query.Field, termPart), postings.DocFreq);
            long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
            var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

            reader.TryGetFieldLengths(query.Field, out var fieldLengths);

            while (postings.MoveNext())
            {
                int docId = postings.DocId;
                if (!reader.IsLive(docId)) continue;

                int tf = postings.Freq;
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = ScoreTerm(f1, f2, f3, tf, docLength);
                if (boost != 1.0f) score *= boost;
                score = ApplyFieldBoost(reader, docId, query.Field, score);
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
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        int docBase = reader.DocBase;

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
            long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
            var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

            reader.TryGetFieldLengths(query.Field, out var fieldLengths);

            while (postings.MoveNext())
            {
                int docId = postings.DocId;
                if (!reader.IsLive(docId)) continue;

                int tf = postings.Freq;
                int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                    ? fieldLengths[docId] : 1;
                float score = ScoreTerm(f1, f2, f3, tf, docLength);
                if (boost != 1.0f) score *= boost;
                score = ApplyFieldBoost(reader, docId, query.Field, score);
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
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
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
                AccumulatePostingsScores(reader, postings, f1, f2, f3, fieldLengths, fieldBoosts, boost, scores, seen, docIds, ref docCount);
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
        if (query.Boost != 1.0f) constantScore *= query.Boost;

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
            if (boost != 1.0f) score *= boost;
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
                float avgDocLength = _stats.GetAvgFieldLength(termQuery.Field);
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, termQuery.Field);
                reader.TryGetFieldLengths(termQuery.Field, out var fieldLengths);
                reader.TryGetFieldBoosts(termQuery.Field, out var fieldBoosts);
                float termBoost = termQuery.Boost;

                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if (!reader.IsLive(docId))
                        continue;

                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, postings.Freq, docLength);
                    if (termBoost != 1.0f)
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
                if (queryBoost != 1.0f)
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
            var shortlist = graph!.Search(searchVec, options, out var stats);
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
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
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
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
        }
    }
}
