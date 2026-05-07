namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Partial class containing span query execution logic (SpanNear, SpanOr, SpanNot).
/// </summary>
public sealed partial class IndexSearcher
{
    private void ExecuteSpanNearQuery(SpanNearQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
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
                collector.Collect(docBase + docId, score);
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
        var seen = new HashSet<int>();
        int docBase = reader.DocBase;
        foreach (var clause in query.Clauses)
        {
            var spans = CollectSpans(clause, reader);
            foreach (var span in spans)
            {
                if (seen.Add(span.DocId))
                    collector.Collect(docBase + span.DocId, 1.0f * query.Boost);
            }
        }
    }

    private void ExecuteSpanNotQuery(SpanNotQuery query, SegmentReader reader,
        Dictionary<(string Field, string Term), int> globalDFs, ref TopNCollector collector)
    {
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
                collector.Collect(docBase + span.DocId, 1.0f * query.Boost);
        }
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
}
