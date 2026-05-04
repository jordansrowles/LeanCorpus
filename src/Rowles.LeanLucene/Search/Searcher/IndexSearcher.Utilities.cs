namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Partial class containing utility methods (GetStoredFields, Explain, Suggest, SearchWithFacets, etc.).
/// </summary>
public sealed partial class IndexSearcher
{
    /// <summary>Retrieves stored fields for a global document ID.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int globalDocId)
    {
        for (int i = 0; i < _readers.Count; i++)
        {
            int nextBase = i + 1 < _docBases.Length ? _docBases[i + 1] : _totalDocCount;
            if (globalDocId >= _docBases[i] && globalDocId < nextBase)
                return _readers[i].GetStoredFields(globalDocId - _docBases[i]);
        }
        return new Dictionary<string, IReadOnlyList<string>>();
    }

    /// <summary>
    /// Explains the score computation for a specific document and query.
    /// Returns null if the document does not match the query.
    /// </summary>
    public Explanation? Explain(TermQuery query, int globalDocId)
    {
        // Find the segment containing this doc
        int readerIndex = -1;
        for (int i = 0; i < _docBases.Length; i++)
        {
            int nextBase = i + 1 < _docBases.Length ? _docBases[i + 1] : _totalDocCount;
            if (globalDocId >= _docBases[i] && globalDocId < nextBase)
            {
                readerIndex = i;
                break;
            }
        }
        if (readerIndex < 0) return null;

        var reader = _readers[readerIndex];
        int localDocId = globalDocId - _docBases[readerIndex];

        if (!reader.IsLive(localDocId)) return null;

        var qt = query.CachedQualifiedTerm ??= string.Concat(query.Field, "\x00", query.Term);
        using var postings = reader.GetPostingsEnum(qt);
        if (postings.IsExhausted) return null;

        // Find the doc in the postings
        if (!postings.Advance(localDocId) || postings.DocId != localDocId)
            return null;

        int tf = postings.Freq;
        int docLength = reader.GetFieldLength(localDocId, query.Field);
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);

        // Compute global DF
        int globalDF = 0;
        foreach (var r in _readers)
        {
            using var p = r.GetPostingsEnum(qt);
            globalDF += p.DocFreq;
        }

        float idf = Bm25Scorer.Idf(_totalDocCount, globalDF);
        float score = _similarity.Score(tf, docLength, avgDocLength, _totalDocCount, globalDF);
        if (query.Boost != 1.0f) score *= query.Boost;

        return new Explanation
        {
            Score = score,
            Description = $"BM25 score for term '{query.Term}' in field '{query.Field}'",
            Details =
            [
                new Explanation { Score = idf, Description = $"idf(docFreq={globalDF}, docCount={_totalDocCount})" },
                new Explanation { Score = tf, Description = $"termFreq={tf}" },
                new Explanation { Score = docLength, Description = $"fieldLength={docLength}" },
                new Explanation { Score = avgDocLength, Description = $"avgFieldLength={avgDocLength:F2}" },
                new Explanation { Score = query.Boost, Description = $"boost={query.Boost}" }
            ]
        };
    }

    /// <summary>
    /// Explains the score and execution strategy for a <see cref="VectorQuery"/> against a specific document.
    /// Surfaces the chosen ANN strategy (flat scan, HNSW two-phase, brute-force filter,
    /// HNSW pre-filter, HNSW post-filter), the configured <c>ef</c>, and shortlist size.
    /// Returns null if the document does not exist or has no vector for the query field.
    /// </summary>
    public Explanation? Explain(VectorQuery query, int globalDocId)
    {
        ArgumentNullException.ThrowIfNull(query);

        int readerIndex = ResolveReaderIndex(globalDocId);
        if (readerIndex < 0 || readerIndex >= _readers.Count) return null;

        var reader = _readers[readerIndex];
        int localDocId = globalDocId - _docBases[readerIndex];

        if (!reader.IsLive(localDocId)) return null;
        if (!reader.HasVectors) return null;

        var docVector = reader.GetVector(query.Field, localDocId);
        if (docVector is null || docVector.Length == 0) return null;

        float similarity = VectorQuery.CosineSimilarity(query.QueryVector, docVector);

        var graph = reader.GetHnswGraph(query.Field);
        bool hasGraph = graph is not null && graph.NodeCount > 0;
        int shortlistSize = query.TopK * query.OversamplingFactor;

        string strategy;
        var details = new List<Explanation>();

        if (query.Filter is not null)
        {
            // Mirror ExecuteVectorQuery's selectivity branching to report the chosen strategy.
            var filterBitmap = ExecuteFilterToBitmap(query.Filter, reader, []);
            int matched = filterBitmap.Cardinality;
            int liveCount = reader.MaxDoc;
            double selectivity = liveCount > 0 ? (double)matched / liveCount : 1.0;

            if (!hasGraph)
                strategy = "flat-scan + filter";
            else if (matched < 64 || selectivity < 0.005)
                strategy = "brute-force on filter (highly selective)";
            else if (selectivity < 0.05)
                strategy = "HNSW pre-filter (allow-list)";
            else
                strategy = "HNSW post-filter with retry";

            details.Add(new Explanation
            {
                Score = matched,
                Description = $"filter matched {matched} docs (selectivity={selectivity:P2})"
            });
        }
        else
        {
            strategy = hasGraph ? "HNSW two-phase" : "flat-scan";
        }

        details.Add(new Explanation { Score = query.EfSearch, Description = $"efSearch={query.EfSearch}" });
        details.Add(new Explanation { Score = shortlistSize, Description = $"shortlistSize={shortlistSize} (topK*oversampling)" });
        if (hasGraph)
            details.Add(new Explanation { Score = graph!.NodeCount, Description = $"hnswNodeCount={graph.NodeCount}" });

        return new Explanation
        {
            Score = similarity,
            Description = $"cosine similarity for field '{query.Field}'; strategy: {strategy}",
            Details = details.ToArray()
        };
    }

    /// <summary>
    /// Returns the top-N terms with the given prefix for auto-complete / suggest,
    /// ranked by global document frequency descending.
    /// </summary>
    /// <param name="prefix">Term prefix to complete (e.g. "hel" → "hello", "help").</param>
    /// <param name="field">Field to scan.</param>
    /// <param name="topN">Maximum number of suggestions to return.</param>
    public IReadOnlyList<(string Term, int DocFreq)> Suggest(string prefix, string field, int topN)
    {
        if (topN <= 0 || _readers.Count == 0)
            return [];

        var qualifiedPrefix = $"{field}\x00{prefix}";
        // Accumulate (term → total docFreq) across all segments
        var termFreqs = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var reader in _readers)
        {
            var matchingTerms = reader.GetTermsWithPrefix(qualifiedPrefix);
            foreach (var (qualifiedTerm, _) in matchingTerms)
            {
                using var postings = reader.GetPostingsEnum(qualifiedTerm);
                if (postings.IsExhausted) continue;
                var bare = qualifiedTerm.AsSpan(field.Length + 1).ToString();
                termFreqs.TryGetValue(bare, out int existing);
                termFreqs[bare] = existing + postings.DocFreq;
            }
        }

        if (termFreqs.Count == 0) return [];

        // Manual sort + range avoids LINQ OrderByDescending().Take() allocation
        var result = new List<(string, int)>(termFreqs.Count);
        foreach (var kv in termFreqs)
            result.Add((kv.Key, kv.Value));
        result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        if (result.Count > topN)
            result.RemoveRange(topN, result.Count - topN);
        return result;
    }

    /// <summary>Executes a query and returns both top-N results and facet counts for the specified fields.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacets(
        Query query, int topN, params string[] facetFields)
    {
        var results = Search(query, topN);
        var matches = SearchAllMatches(query, results.TotalHits);
        var facetsCollector = new FacetsCollector();
        var seenDocs = new HashSet<int>();

        foreach (var sd in matches.ScoreDocs)
        {
            int globalDocId = sd.DocId;
            if (!seenDocs.Add(globalDocId)) continue;
            int readerIndex = ResolveReaderIndex(globalDocId);
            var reader = _readers[readerIndex];
            int localDocId = globalDocId - _docBases[readerIndex];

            foreach (var facetField in facetFields)
            {
                if (reader.TryGetSortedDocValue(facetField, localDocId, out string val) && !string.IsNullOrEmpty(val))
                {
                    facetsCollector.Collect(facetField, val);
                }
                else
                {
                    var stored = reader.GetStoredFields(localDocId);
                    if (stored.TryGetValue(facetField, out var values))
                    {
                        foreach (var v in values)
                            facetsCollector.Collect(facetField, v);
                    }
                }
            }
        }

        return (results, facetsCollector.GetResults());
    }

    // --- Aggregations ---

    /// <summary>
    /// Executes a search query and computes numeric aggregations over matching documents.
    /// </summary>
    public (TopDocs Results, AggregationResult[] Aggregations) SearchWithAggregations(
        Query query, int topN, params AggregationRequest[] aggregations)
    {
        var results = Search(query, topN);
        if (aggregations.Length == 0 || results.TotalHits == 0)
            return (results, []);

        var matches = SearchAllMatches(query, results.TotalHits);
        var seenDocs = new HashSet<int>();
        foreach (var scoreDoc in matches.ScoreDocs)
            seenDocs.Add(scoreDoc.DocId);
        var matchingDocIds = seenDocs.ToArray();

        var aggs = NumericAggregator.Aggregate(
            matchingDocIds, aggregations, _readers, _docBases, _totalDocCount);

        return (results, aggs);
    }

    // --- Result Collapsing ---

    /// <summary>
    /// Executes a search and collapses results so only the best document per unique field value is returned.
    /// Uses SortedDocValues for the collapse field.
    /// </summary>
    public TopDocs SearchWithCollapse(Query query, int topN, CollapseField collapse)
    {
        var topResults = Search(query, topN);
        var allResults = SearchAllMatches(query, topResults.TotalHits);
        if (allResults.TotalHits == 0)
            return allResults;

        var bestPerGroup = new Dictionary<string, ScoreDoc>(StringComparer.Ordinal);
        bool isTopScore = collapse.Mode == CollapseMode.TopScore;

        foreach (var scoreDoc in allResults.ScoreDocs)
        {
            int readerIdx = ResolveReaderIndex(scoreDoc.DocId);
            var reader = _readers[readerIdx];
            int localDocId = scoreDoc.DocId - _docBases[readerIdx];

            if (!reader.TryGetSortedDocValue(collapse.FieldName, localDocId, out string groupValue))
                groupValue = "__null__";

            if (!bestPerGroup.TryGetValue(groupValue, out var existing))
            {
                bestPerGroup[groupValue] = scoreDoc;
            }
            else
            {
                bool replace = isTopScore
                    ? scoreDoc.Score > existing.Score
                    : scoreDoc.Score < existing.Score;
                if (replace)
                    bestPerGroup[groupValue] = scoreDoc;
            }
        }

        var collapsed = bestPerGroup.Values
            .OrderByDescending(sd => sd.Score)
            .Take(topN)
            .ToArray();

        return new TopDocs(bestPerGroup.Count, collapsed);
    }

    private TopDocs SearchAllMatches(Query query, int minimumCapacity)
    {
        if (_totalDocCount == 0 || minimumCapacity <= 0)
            return TopDocs.Empty;
        int capacity = Math.Min(_totalDocCount, minimumCapacity);
        return SearchCore(query, capacity);
    }

    private int ResolveReaderIndex(int globalDocId)
    {
        for (int i = _docBases.Length - 1; i >= 0; i--)
        {
            if (globalDocId >= _docBases[i])
                return i;
        }
        return 0;
    }

    // --- MoreLikeThis ---

    /// <summary>
    /// Convenience API: finds documents similar to the given document.
    /// Extracts significant terms from term vectors and re-queries the index.
    /// </summary>
    public TopDocs MoreLikeThis(int docId, string[] fields, int topN,
        MoreLikeThisParameters? parameters = null)
    {
        return Search(new MoreLikeThisQuery(docId, fields, parameters), topN);
    }

    internal TopDocs ExecuteMoreLikeThis(MoreLikeThisQuery mlt, int topN)
    {
        var p = mlt.Parameters;
        int readerIdx = ResolveReaderIndex(mlt.DocId);
        var reader = _readers[readerIdx];
        int localDocId = mlt.DocId - _docBases[readerIdx];

        // Collect (term, field, tfidfScore) across all requested fields
        var candidates = new List<(string Field, string Term, float Score)>();

        foreach (var field in mlt.Fields)
        {
            var tv = reader.GetTermVectors(localDocId);
            if (tv is null || !tv.TryGetValue(field, out var entries)) continue;

            foreach (var entry in entries)
            {
                if (entry.Term.Length < p.MinWordLength) continue;
                if (entry.Freq < p.MinTermFreq) continue;

                // Compute document frequency across all segments
                var qt = string.Concat(field, "\x00", entry.Term);
                int docFreq = 0;
                foreach (var r in _readers)
                    docFreq += r.GetDocFreqByQualified(qt);

                if (docFreq < p.MinDocFreq || docFreq > p.MaxDocFreq) continue;

                float tf = entry.Freq;
                float idf = MathF.Log((float)_totalDocCount / (docFreq + 1));
                candidates.Add((field, entry.Term, tf * idf));
            }
        }

        if (candidates.Count == 0)
            return TopDocs.Empty;

        // Sort by score descending, take top N terms
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        int termCount = Math.Min(candidates.Count, p.MaxQueryTerms);

        // Build a BooleanQuery with Should clauses
        var boolQBuilder = new BooleanQuery.Builder();
        float maxScore = candidates[0].Score;

        for (int i = 0; i < termCount; i++)
        {
            var (field, term, score) = candidates[i];
            var tq = new TermQuery(field, term);
            if (p.BoostByScore && maxScore > 0)
                tq.Boost = score / maxScore;
            boolQBuilder.Add(tq, Occur.Should);
        }

        var boolQ = boolQBuilder.Build();

        // Execute via the existing search path (bypasses cache to avoid recursion)
        return SearchCore(boolQ, topN + 1);
    }
}
