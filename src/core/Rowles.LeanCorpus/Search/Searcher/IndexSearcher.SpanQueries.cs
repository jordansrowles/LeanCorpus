namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing span query execution logic (SpanNear, SpanOr, SpanNot).
/// </summary>
public sealed partial class IndexSearcher
{
    private void ExecuteIntervalsQuery(IntervalsQuery query, SegmentReader reader, ref TopNCollector collector)
    {
        var intervals = CollectIntervals(query.Source, reader);
        if (intervals.Count == 0)
            return;

        int docBase = reader.DocBase;
        float score = query.Boost != 1.0f ? query.Boost : 1.0f;
        var seen = new HashSet<int>();
        foreach (var interval in intervals)
        {
            if (seen.Add(interval.DocId))
                collector.Collect(docBase + interval.DocId, ApplyFieldBoost(reader, interval.DocId, query.Field, score));
        }
    }

    private void ExecuteSpanNearQuery(SpanNearQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (TryExecuteSpanNearTermQuery(query, reader, excludedDocs: null, query.Boost, globalDFs, ref collector))
            return;

        // Collect spans per clause
        var clauseSpans = new List<List<Span>>(query.Clauses.Count);
        foreach (var clause in query.Clauses)
        {
            var spans = CollectSpans(clause, reader);
            if (spans.Count == 0) return; // AND semantics: all clauses must match
            clauseSpans.Add(spans);
        }

        // Find documents present in all clause spans
        var docSets = new List<HashSet<int>>(clauseSpans.Count);
        foreach (var spans in clauseSpans)
        {
            var set = new HashSet<int>();
            foreach (var sp in spans) set.Add(sp.DocId);
            docSets.Add(set);
        }
        var commonDocs = docSets[0];
        for (int i = 1; i < docSets.Count; i++)
            commonDocs.IntersectWith(docSets[i]);

        int docBase = reader.DocBase;
        foreach (int docId in commonDocs)
        {
            // Check positional constraints
            var clausePositions = new List<List<int>>(clauseSpans.Count);
            foreach (var spans in clauseSpans)
            {
                var positions = new List<int>();
                foreach (var sp in spans)
                    if (sp.DocId == docId) positions.Add(sp.Start);
                positions.Sort();
                clausePositions.Add(positions);
            }

            if (CheckNearConstraint(clausePositions, query.Slop, query.InOrder))
            {
                float score = 1.0f * query.Boost;
                collector.Collect(docBase + docId, ApplyFieldBoost(reader, docId, query.Field, score));
            }
        }
    }

    private static bool CheckNearConstraint(List<List<int>> clausePositions, int slop, bool inOrder)
    {
        // Simple check: for each position in clause[0], find matching positions in other clauses
        foreach (int pos0 in clausePositions[0])
        {
            bool allMatch = true;
            int prevPos = pos0;
            for (int c = 1; c < clausePositions.Count; c++)
            {
                bool found = false;
                foreach (int posC in clausePositions[c])
                {
                    int distance = Math.Abs(posC - prevPos);
                    if (distance <= slop + 1)
                    {
                        if (inOrder && posC <= prevPos) continue;
                        prevPos = posC;
                        found = true;
                        break;
                    }
                }
                if (!found) { allMatch = false; break; }
            }
            if (allMatch) return true;
        }
        return false;
    }

    private void ExecuteSpanOrQuery(SpanOrQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (TryExecuteSpanOrTermQuery(query, reader, globalDFs, ref collector))
            return;

        var seen = new HashSet<int>();
        int docBase = reader.DocBase;
        foreach (var clause in query.Clauses)
        {
            var spans = CollectSpans(clause, reader);
            foreach (var span in spans)
            {
                if (seen.Add(span.DocId))
                    collector.Collect(docBase + span.DocId, ApplyFieldBoost(reader, span.DocId, query.Field, 1.0f * query.Boost));
            }
        }
    }

    private void ExecuteSpanNotQuery(SpanNotQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (TryExecuteSpanNotTermQuery(query, reader, globalDFs, ref collector))
            return;

        var includeSpans = CollectSpans(query.Include, reader);
        var excludeSpans = CollectSpans(query.Exclude, reader);

        // Exclude documents that have any exclude span
        var excludedDocs = new HashSet<int>();
        foreach (var s in excludeSpans)
            excludedDocs.Add(s.DocId);

        int docBase = reader.DocBase;
        var seen = new HashSet<int>();
        foreach (var span in includeSpans)
        {
            if (!excludedDocs.Contains(span.DocId) && seen.Add(span.DocId))
                collector.Collect(docBase + span.DocId, ApplyFieldBoost(reader, span.DocId, query.Field, 1.0f * query.Boost));
        }
    }

    private bool TryExecuteSpanNearTermQuery(SpanNearQuery query, SegmentReader reader, bool[]? excludedDocs,
        float boost, Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        int termCount = query.Clauses.Count;
        if (termCount == 0)
            return true;

        var postings = new PostingsEnum[termCount];
        try
        {
            for (int i = 0; i < termCount; i++)
            {
                if (query.Clauses[i] is not SpanTermQuery termQuery)
                    return false;

                var qualifiedTerm = termQuery.CachedQualifiedTerm ??= string.Concat(termQuery.Field, "\x00", termQuery.Term);
                postings[i] = reader.GetPostingsEnumWithPositions(qualifiedTerm);
                if (postings[i].IsExhausted)
                    return true;
            }

            int leaderIdx = 0;
            for (int i = 1; i < termCount; i++)
            {
                if (postings[i].DocFreq < postings[leaderIdx].DocFreq)
                    leaderIdx = i;
            }
            int docBase = reader.DocBase;
            reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
            bool hasDeletions = reader.HasDeletions;
            reader.TryGetFieldLengths(query.Field, out var fieldLengths);
            float avgDocLength = Stats.GetAvgFieldLength(query.Field);

            // Compute scoring factors for every clause, not just the leader.
            // Each clause contributes its own IDF weight to the span score.
            var termFactors = new (float F1, float F2, float F3)[termCount];
            for (int i = 0; i < termCount; i++)
            {
                var clause = (SpanTermQuery)query.Clauses[i];
                var qt = clause.CachedQualifiedTerm!;
                int docFreq = globalDFs.GetValueOrDefault((query.Field, clause.Term), postings[i].DocFreq);
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;
                termFactors[i] = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);
            }

            while (postings[leaderIdx].MoveNext())
            {
                int docId = postings[leaderIdx].DocId;
                if ((hasDeletions && !reader.IsLive(docId)) ||
                    (excludedDocs is not null && excludedDocs[docId]))
                {
                    continue;
                }

                bool allMatch = true;
                for (int i = 0; i < termCount; i++)
                {
                    if (i == leaderIdx) continue;
                    if (!postings[i].Advance(docId) || postings[i].DocId != docId)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch && HasSpanNearPositions(postings, termCount, query.Slop, query.InOrder))
                {
                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    // Sum scores across all clauses using the leader's term frequency
                    // as an estimate of the span frequency.
                    float score = 0;
                    int leaderTf = postings[leaderIdx].Freq;
                    for (int i = 0; i < termCount; i++)
                    {
                        var (f1, f2, f3) = termFactors[i];
                        score += ScoreTerm(f1, f2, f3, leaderTf, docLength);
                    }
                    if (Math.Abs(boost - 1.0f) > 1e-6f) score *= boost;
                    collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
                }
            }

            return true;
        }
        finally
        {
            for (int i = 0; i < postings.Length; i++)
                postings[i].Dispose();
        }
    }

    private bool TryExecuteSpanOrTermQuery(SpanOrQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        foreach (var clause in query.Clauses)
        {
            if (clause is not SpanTermQuery)
                return false;
        }

        var seen = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var docIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int docCount = 0;
        int docBase = reader.DocBase;
        reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);
        bool hasDeletions = reader.HasDeletions;
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
        reader.TryGetFieldLengths(query.Field, out var fieldLengths);
        float boost = query.Boost;

        try
        {
            foreach (var clause in query.Clauses)
            {
                var termQuery = (SpanTermQuery)clause;
                var qualifiedTerm = termQuery.CachedQualifiedTerm ??= string.Concat(termQuery.Field, "\x00", termQuery.Term);
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                int docFreq = globalDFs.GetValueOrDefault((termQuery.Field, termQuery.Term), postings.DocFreq);
                long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qualifiedTerm) : 0;
                var (f1, f2, f3) = ComputeTermFactors(docFreq, avgDocLength, collectionFreq, query.Field);

                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if ((hasDeletions && !reader.IsLive(docId)) || seen[docId])
                        continue;

                    seen[docId] = true;
                    docIds[docCount++] = docId;
                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, postings.Freq, docLength);
                    if (Math.Abs(boost - 1.0f) > 1e-6f) score *= boost;
                    collector.Collect(docBase + docId, ApplyFieldBoost(fieldBoosts, docId, score));
                }
            }

            return true;
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

    private bool TryExecuteSpanNotTermQuery(SpanNotQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
        if (query.Include is not SpanNearQuery includeNear || !IsSpanTermDocSetQuery(query.Exclude))
            return false;

        var excluded = EnsureScratch(ref t_patternSeen, reader.MaxDoc);
        var excludedDocIds = EnsureScratch(ref t_patternDocIds, reader.MaxDoc);
        int excludedCount = 0;

        try
        {
            MarkExcludedSpanTermDocs(query.Exclude, reader, excluded, excludedDocIds, ref excludedCount);
            return TryExecuteSpanNearTermQuery(includeNear, reader, excluded, query.Boost, globalDFs, ref collector);
        }
        finally
        {
            for (int i = 0; i < excludedCount; i++)
            {
                excluded[excludedDocIds[i]] = false;
                excludedDocIds[i] = 0;
            }
        }
    }

    private static bool IsSpanTermDocSetQuery(SpanQuery query)
    {
        if (query is SpanTermQuery)
            return true;
        if (query is not SpanOrQuery spanOr)
            return false;

        foreach (var clause in spanOr.Clauses)
        {
            if (clause is not SpanTermQuery)
                return false;
        }

        return true;
    }

    private static void MarkExcludedSpanTermDocs(SpanQuery query, SegmentReader reader,
        bool[] excluded, int[] excludedDocIds, ref int excludedCount)
    {
        if (query is SpanTermQuery termQuery)
        {
            MarkExcludedSpanTermDocs(termQuery, reader, excluded, excludedDocIds, ref excludedCount);
            return;
        }

        var spanOr = (SpanOrQuery)query;
        foreach (var clause in spanOr.Clauses)
            MarkExcludedSpanTermDocs((SpanTermQuery)clause, reader, excluded, excludedDocIds, ref excludedCount);
    }

    private static void MarkExcludedSpanTermDocs(SpanTermQuery query, SegmentReader reader,
        bool[] excluded, int[] excludedDocIds, ref int excludedCount)
    {
        var qualifiedTerm = query.CachedQualifiedTerm ??= string.Concat(query.Field, "\x00", query.Term);
        using var postings = reader.GetPostingsEnum(qualifiedTerm);
        bool hasDeletions = reader.HasDeletions;
        while (postings.MoveNext())
        {
            int docId = postings.DocId;
            if ((hasDeletions && !reader.IsLive(docId)) || excluded[docId])
                continue;

            excluded[docId] = true;
            excludedDocIds[excludedCount++] = docId;
        }
    }

    private static bool HasSpanNearPositions(PostingsEnum[] postings, int termCount, int slop, bool inOrder)
    {
        if (termCount == 1)
            return !postings[0].GetCurrentPositions().IsEmpty;

        var firstPositions = postings[0].GetCurrentPositions();
        for (int i = 0; i < firstPositions.Length; i++)
        {
            if (HasSpanNearPositions(postings, termCount, slop, inOrder, clauseIndex: 1, firstPositions[i]))
                return true;
        }

        return false;
    }

    private static bool HasSpanNearPositions(PostingsEnum[] postings, int termCount, int slop, bool inOrder,
        int clauseIndex, int previousPosition)
    {
        var positions = postings[clauseIndex].GetCurrentPositions();
        for (int i = 0; i < positions.Length; i++)
        {
            int position = positions[i];
            int distance = Math.Abs(position - previousPosition);
            if (distance > slop + 1 || (inOrder && position <= previousPosition))
                continue;

            if (clauseIndex + 1 == termCount ||
                HasSpanNearPositions(postings, termCount, slop, inOrder, clauseIndex + 1, position))
            {
                return true;
            }
        }

        return false;
    }

    private List<Span> CollectSpans(SpanQuery query, SegmentReader reader)
    {
        var spans = new List<Span>();
        switch (query)
        {
            case SpanTermQuery stq:
                {
                    var qt = stq.CachedQualifiedTerm ??= string.Concat(stq.Field, "\x00", stq.Term);
                    using var pe = reader.GetPostingsEnumWithPositions(qt);
                    while (pe.MoveNext())
                    {
                        var positions = pe.GetCurrentPositions();
                        foreach (int pos in positions)
                            spans.Add(new Span(pe.DocId, pos, pos + 1));
                    }
                    break;
                }
            case SpanNearQuery snq:
                {
                    // Recursive: collect matching spans
                    var clauseSpans = new List<List<Span>>(snq.Clauses.Count);
                    foreach (var clause in snq.Clauses)
                        clauseSpans.Add(CollectSpans(clause, reader));

                    var commonDocs = new HashSet<int>();
                    foreach (var span in clauseSpans[0])
                        commonDocs.Add(span.DocId);
                    for (int i = 1; i < clauseSpans.Count; i++)
                    {
                        var docIds = new HashSet<int>();
                        foreach (var span in clauseSpans[i])
                            docIds.Add(span.DocId);
                        commonDocs.IntersectWith(docIds);
                    }

                    foreach (int docId in commonDocs)
                    {
                        var clausePositions = new List<List<int>>(clauseSpans.Count);
                        foreach (var clauseSpanList in clauseSpans)
                        {
                            var positions = new List<int>();
                            foreach (var sp in clauseSpanList)
                                if (sp.DocId == docId) positions.Add(sp.Start);
                            positions.Sort();
                            clausePositions.Add(positions);
                        }
                        if (CheckNearConstraint(clausePositions, snq.Slop, snq.InOrder))
                        {
                            int minPos = int.MaxValue;
                            int maxPos = int.MinValue;
                            foreach (var positions in clausePositions)
                            {
                                foreach (int p in positions)
                                {
                                    if (p < minPos) minPos = p;
                                    if (p > maxPos) maxPos = p;
                                }
                            }
                            spans.Add(new Span(docId, minPos, maxPos + 1));
                        }
                    }
                    break;
                }
            case SpanOrQuery soq:
                foreach (var clause in soq.Clauses)
                    spans.AddRange(CollectSpans(clause, reader));
                break;
            case SpanNotQuery snotq:
                {
                    var includeSpans = CollectSpans(snotq.Include, reader);
                    var excludeSpans = CollectSpans(snotq.Exclude, reader);
                    var excludedDocs = new HashSet<int>();
                    foreach (var s in excludeSpans)
                        excludedDocs.Add(s.DocId);
                    foreach (var span in includeSpans)
                    {
                        if (!excludedDocs.Contains(span.DocId))
                            spans.Add(span);
                    }
                    break;
                }
        }
        return spans;
    }

    private List<Span> CollectIntervals(IntervalsSource source, SegmentReader reader)
    {
        switch (source)
        {
            case IntervalsTermSource termSource:
                return CollectTermIntervals(termSource, reader);
            case IntervalsPhraseSource phraseSource:
                return CollectPhraseIntervals(phraseSource, reader);
            case IntervalsOrSource orSource:
                {
                    var spans = new List<Span>();
                    foreach (var child in orSource.Sources)
                        spans.AddRange(CollectIntervals(child, reader));
                    return spans;
                }
            case IntervalsOrderedSource orderedSource:
                return CollectOrderedIntervals(orderedSource.Sources, reader, orderedSource.MaxGaps);
            case IntervalsUnorderedSource unorderedSource:
                return CollectUnorderedIntervals(unorderedSource.Sources, reader, unorderedSource.MaxGaps);
            case IntervalsContainingSource containingSource:
                return FilterContainingIntervals(CollectIntervals(containingSource.Outer, reader), CollectIntervals(containingSource.Inner, reader));
            case IntervalsContainedBySource containedBySource:
                return FilterContainedIntervals(CollectIntervals(containedBySource.Inner, reader), CollectIntervals(containedBySource.Outer, reader));
            case IntervalsNotContainingSource notContainingSource:
                return FilterNotContainingIntervals(CollectIntervals(notContainingSource.Outer, reader), CollectIntervals(notContainingSource.Inner, reader));
            default:
                return [];
        }
    }

    private static List<Span> FilterContainingIntervals(List<Span> outer, List<Span> inner)
    {
        if (outer.Count == 0 || inner.Count == 0)
            return [];

        var innerByDoc = GroupSpansByDoc(inner);
        var matches = new List<Span>();
        foreach (var span in outer)
        {
            if (!innerByDoc.TryGetValue(span.DocId, out var candidates))
                continue;

            bool contains = candidates.Any(candidate => candidate.Start >= span.Start && candidate.End <= span.End);
            if (contains)
                matches.Add(span);
        }

        return matches;
    }

    private static List<Span> FilterContainedIntervals(List<Span> inner, List<Span> outer)
    {
        if (inner.Count == 0 || outer.Count == 0)
            return [];

        var outerByDoc = GroupSpansByDoc(outer);
        var contained = new List<Span>();
        foreach (var span in inner)
        {
            if (!outerByDoc.TryGetValue(span.DocId, out var candidates))
                continue;

            if (candidates.Any(candidate => span.Start >= candidate.Start && span.End <= candidate.End))
                contained.Add(span);
        }

        return contained;
    }

    private static List<Span> FilterNotContainingIntervals(List<Span> outer, List<Span> inner)
    {
        if (outer.Count == 0)
            return [];
        if (inner.Count == 0)
            return outer;

        var innerByDoc = GroupSpansByDoc(inner);
        var matches = new List<Span>();
        foreach (var span in outer)
        {
            if (!innerByDoc.TryGetValue(span.DocId, out var candidates) ||
                !candidates.Any(candidate => candidate.Start >= span.Start && candidate.End <= span.End))
            {
                matches.Add(span);
            }
        }

        return matches;
    }

    private List<Span> CollectTermIntervals(IntervalsTermSource source, SegmentReader reader)
    {
        var spans = new List<Span>();
        var qualifiedTerm = source.CachedQualifiedTerm ??= string.Concat(source.Field, "\x00", source.Term);
        using var postings = reader.GetPostingsEnumWithPositions(qualifiedTerm);
        while (postings.MoveNext())
        {
            if (!reader.IsLive(postings.DocId))
                continue;

            var positions = postings.GetCurrentPositions();
            for (int i = 0; i < positions.Length; i++)
                spans.Add(new Span(postings.DocId, positions[i], positions[i] + 1));
        }

        return spans;
    }

    private List<Span> CollectPhraseIntervals(IntervalsPhraseSource source, SegmentReader reader)
    {
        if (source.Terms.Count == 0)
            return [];

        var spansByDoc = new Dictionary<int, List<int[]>>();
        HashSet<int>? commonDocs = null;

        foreach (var term in source.Terms)
        {
            var postingsByDoc = new Dictionary<int, int[]>();
            using var postings = reader.GetPostingsEnumWithPositions(string.Concat(source.Field, "\x00", term));
            while (postings.MoveNext())
            {
                if (!reader.IsLive(postings.DocId))
                    continue;

                var positions = postings.GetCurrentPositions();
                if (!positions.IsEmpty)
                    postingsByDoc[postings.DocId] = positions.ToArray();
            }

            if (postingsByDoc.Count == 0)
                return [];

            commonDocs ??= new HashSet<int>(postingsByDoc.Keys);
            commonDocs.IntersectWith(postingsByDoc.Keys);
            if (commonDocs.Count == 0)
                return [];

            foreach (var (docId, positions) in postingsByDoc)
            {
                if (!spansByDoc.TryGetValue(docId, out var docLists))
                {
                    docLists = [];
                    spansByDoc[docId] = docLists;
                }

                docLists.Add(positions);
            }
        }

        var results = new List<Span>();
        foreach (int docId in commonDocs!)
        {
            var positions = spansByDoc[docId];
            CollectPhraseIntervalsForDoc(docId, positions, 0, 0, 0, results);
        }

        return results;
    }

    private static void CollectPhraseIntervalsForDoc(int docId, List<int[]> positions, int clauseIndex, int startPosition, int previousPosition, List<Span> results)
    {
        foreach (int position in positions[clauseIndex])
        {
            if (clauseIndex > 0 && position != previousPosition + 1)
                continue;

            if (clauseIndex + 1 == positions.Count)
            {
                results.Add(new Span(docId, startPosition, position + 1));
                continue;
            }

            CollectPhraseIntervalsForDoc(
                docId,
                positions,
                clauseIndex + 1,
                clauseIndex == 0 ? position : startPosition,
                position,
                results);
        }
    }

    private List<Span> CollectOrderedIntervals(IReadOnlyList<IntervalsSource> sources, SegmentReader reader, int maxGaps)
    {
        if (sources.Count == 0)
            return [];

        var childSpans = new List<List<Span>>(sources.Count);
        foreach (var source in sources)
        {
            var spans = CollectIntervals(source, reader);
            if (spans.Count == 0)
                return [];
            childSpans.Add(spans);
        }

        return CombineOrderedSpans(childSpans, maxGaps);
    }

    private static List<Span> CombineOrderedSpans(IReadOnlyList<List<Span>> childSpans, int maxGaps)
    {
        var grouped = childSpans.Select(GroupSpansByDoc).ToList();
        var commonDocs = new HashSet<int>(grouped[0].Keys);
        for (int i = 1; i < grouped.Count; i++)
            commonDocs.IntersectWith(grouped[i].Keys);

        var results = new List<Span>();
        foreach (int docId in commonDocs)
        {
            var perDoc = new List<List<Span>>(grouped.Count);
            for (int i = 0; i < grouped.Count; i++)
            {
                var spans = grouped[i][docId];
                spans.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
                perDoc.Add(spans);
            }

            BuildOrderedIntervals(docId, perDoc, maxGaps, 0, null, null, 0, results);
        }

        return results;
    }

    private static void BuildOrderedIntervals(
        int docId,
        IReadOnlyList<List<Span>> perDoc,
        int maxGaps,
        int index,
        Span? first,
        Span? previous,
        int accumulatedGaps,
        List<Span> results)
    {
        foreach (var candidate in perDoc[index])
        {
            if (previous is { } prior && candidate.Start < prior.End)
                continue;

            int gap = previous is { } previousSpan ? Math.Max(0, candidate.Start - previousSpan.End) : 0;
            int newGapTotal = accumulatedGaps + gap;
            if (newGapTotal > maxGaps)
                continue;

            if (index + 1 == perDoc.Count)
            {
                var firstSpan = first ?? candidate;
                results.Add(new Span(docId, firstSpan.Start, candidate.End));
                continue;
            }

            BuildOrderedIntervals(docId, perDoc, maxGaps, index + 1, first ?? candidate, candidate, newGapTotal, results);
        }
    }

    private List<Span> CollectUnorderedIntervals(IReadOnlyList<IntervalsSource> sources, SegmentReader reader, int maxGaps)
    {
        if (sources.Count == 0)
            return [];

        var childSpans = new List<List<Span>>(sources.Count);
        foreach (var source in sources)
        {
            var spans = CollectIntervals(source, reader);
            if (spans.Count == 0)
                return [];
            childSpans.Add(spans);
        }

        var grouped = childSpans.Select(GroupSpansByDoc).ToList();
        var commonDocs = new HashSet<int>(grouped[0].Keys);
        for (int i = 1; i < grouped.Count; i++)
            commonDocs.IntersectWith(grouped[i].Keys);

        var results = new List<Span>();
        foreach (int docId in commonDocs)
        {
            var perDoc = new List<List<Span>>(grouped.Count);
            for (int i = 0; i < grouped.Count; i++)
                perDoc.Add(grouped[i][docId]);

            BuildUnorderedIntervals(docId, perDoc, maxGaps, 0, [], results);
        }

        return results;
    }

    private static void BuildUnorderedIntervals(
        int docId,
        IReadOnlyList<List<Span>> perDoc,
        int maxGaps,
        int index,
        List<Span> selected,
        List<Span> results)
    {
        foreach (var candidate in perDoc[index])
        {
            selected.Add(candidate);
            if (index + 1 == perDoc.Count)
            {
                int minStart = selected.Min(static span => span.Start);
                int maxEnd = selected.Max(static span => span.End);
                int coveredLength = selected.Sum(static span => span.End - span.Start);
                int totalGaps = (maxEnd - minStart) - coveredLength;
                if (totalGaps <= maxGaps)
                    results.Add(new Span(docId, minStart, maxEnd));
            }
            else
            {
                BuildUnorderedIntervals(docId, perDoc, maxGaps, index + 1, selected, results);
            }

            selected.RemoveAt(selected.Count - 1);
        }
    }

    private static Dictionary<int, List<Span>> GroupSpansByDoc(IEnumerable<Span> spans)
    {
        var grouped = new Dictionary<int, List<Span>>();
        foreach (var span in spans)
        {
            if (!grouped.TryGetValue(span.DocId, out var list))
            {
                list = [];
                grouped[span.DocId] = list;
            }

            list.Add(span);
        }

        return grouped;
    }
}
