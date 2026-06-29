namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Partial class containing utility methods (GetStoredFields, Explain, Suggest, SearchWithFacets, etc.).
/// </summary>
public sealed partial class IndexSearcher
{
    /// <summary>Retrieves stored fields for a global document ID.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int globalDocId)
    {
        return GetStoredFields(globalDocId, null);
    }

    /// <summary>
    /// Retrieves stored fields for a global document ID, optionally filtering to the given set of field names.
    /// When <paramref name="fieldsToLoad"/> is null, all fields are returned.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int globalDocId, ISet<string>? fieldsToLoad)
    {
        for (int i = 0; i < _readers.Count; i++)
        {
            int nextBase = i + 1 < _docBases.Length ? _docBases[i + 1] : _totalDocCount;
            if (globalDocId >= _docBases[i] && globalDocId < nextBase)
                return _readers[i].GetStoredFields(globalDocId - _docBases[i], fieldsToLoad);
        }
        return new Dictionary<string, IReadOnlyList<string>>();
    }

    /// <summary>Retrieves stored binary fields for a global document ID.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int globalDocId)
    {
        return GetStoredBinaryFields(globalDocId, null);
    }

    /// <summary>
    /// Retrieves stored binary fields for a global document ID, optionally filtering to the given set of field names.
    /// When <paramref name="fieldsToLoad"/> is null, all fields are returned.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int globalDocId, ISet<string>? fieldsToLoad)
    {
        for (int i = 0; i < _readers.Count; i++)
        {
            int nextBase = i + 1 < _docBases.Length ? _docBases[i + 1] : _totalDocCount;
            if (globalDocId >= _docBases[i] && globalDocId < nextBase)
                return _readers[i].GetStoredBinaryFields(globalDocId - _docBases[i], fieldsToLoad);
        }

        return new Dictionary<string, IReadOnlyList<byte[]>>();
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
        long collectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;
        var (f1, f2, f3) = ComputeTermFactors(globalDF, avgDocLength, collectionFreq, query.Field);
        float score = ScoreTerm(f1, f2, f3, tf, docLength);
        if (query.Boost != 1.0f) score *= query.Boost;
        float indexBoost = reader.GetFieldBoost(localDocId, query.Field);
        if (indexBoost != 1.0f) score *= indexBoost;

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
                new Explanation { Score = query.Boost, Description = $"queryBoost={query.Boost}" },
                new Explanation { Score = indexBoost, Description = $"indexBoost={indexBoost}" }
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
        float indexBoost = reader.GetFieldBoost(localDocId, query.Field);
        if (indexBoost != 1.0f)
            similarity *= indexBoost;

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
        details.Add(new Explanation { Score = indexBoost, Description = $"indexBoost={indexBoost}" });

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
                if (reader.TryGetSortedSetDocValues(facetField, localDocId, out var setValues))
                {
                    foreach (var value in setValues)
                    {
                        if (!string.IsNullOrEmpty(value))
                            facetsCollector.Collect(facetField, value);
                    }
                }
                else if (reader.TryGetSortedDocValue(facetField, localDocId, out string val) && !string.IsNullOrEmpty(val))
                {
                    facetsCollector.Collect(facetField, val);
                }
                else if (reader.TryGetBinaryDocValues(facetField, localDocId, out var binaryValues))
                {
                    foreach (var value in binaryValues)
                    {
                        var decoded = System.Text.Encoding.UTF8.GetString(value);
                        if (!string.IsNullOrEmpty(decoded))
                            facetsCollector.Collect(facetField, decoded);
                    }
                }
                else
                {
                    var stored = reader.GetStoredFields(localDocId, new HashSet<string> { facetField });
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

        var aggs = NumericAggregator.Aggregate(
            matches.ScoreDocs.AsSpan(), aggregations, _readers, _docBases, _totalDocCount);

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

            string groupValue = ResolveCollapseValue(reader, collapse.FieldName, localDocId);

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

    private static string ResolveCollapseValue(Index.Segment.SegmentReader reader, string fieldName, int localDocId)
    {
        if (reader.TryGetSortedDocValue(fieldName, localDocId, out string value))
            return value;

        if (reader.TryGetSortedSetDocValues(fieldName, localDocId, out var setValues) && setValues.Count > 0)
            return setValues[0];

        if (reader.TryGetBinaryDocValues(fieldName, localDocId, out var binaryValues) && binaryValues.Count > 0)
            return System.Text.Encoding.UTF8.GetString(binaryValues[0]);

        return "__null__";
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
        int segmentCount = _readers.Count;

        // Bounded min-heap (priority = score). We keep the smallest score at the
        // top so we can evict the weakest candidate when the heap exceeds MaxQueryTerms.
        int capacity = p.MaxQueryTerms;
        var heap = new PriorityQueue<(float Score, string Field, string Term), float>(capacity);

        // Reusable buffer for stack-like qualified term construction (avoids per-term string alloc).
        char[]? qtRented = null;
        int qtBufCap = 256;
        try
        {
            foreach (var field in mlt.Fields)
            {
                var tv = reader.GetTermVectors(localDocId);
                if (tv is null || !tv.TryGetValue(field, out var entries)) continue;

                int fieldLen = field.Length;
                foreach (var entry in entries)
                {
                    if (entry.Term.Length < p.MinWordLength) continue;
                    if (entry.Freq < p.MinTermFreq) continue;

                    float tf = entry.Freq;

                    // Build qualified term "field\0term" into reusable buffer.
                    int qtLen = fieldLen + 1 + entry.Term.Length;
                    if (qtLen > qtBufCap)
                    {
                        if (qtRented is not null) System.Buffers.ArrayPool<char>.Shared.Return(qtRented);
                        qtBufCap = qtLen;
                        qtRented = System.Buffers.ArrayPool<char>.Shared.Rent(qtBufCap);
                    }
                    char[] buf = qtRented ??= System.Buffers.ArrayPool<char>.Shared.Rent(qtBufCap);
                    field.AsSpan().CopyTo(buf);
                    buf[fieldLen] = '\0';
                    entry.Term.AsSpan().CopyTo(buf.AsSpan(fieldLen + 1));
                    ReadOnlySpan<char> qt = buf.AsSpan(0, qtLen);

                    // Fast path: MinDocFreq <= 1 with multiple segments — use local
                    // segment's docFreq scaled by segment count as an IDF approximation.
                    if (p.MinDocFreq <= 1 && segmentCount > 1)
                    {
                        int currDocFreq = reader.GetDocFreqByQualified(qt);
                        if (currDocFreq < 1) continue;
                        float estimatedGlobal = (float)currDocFreq * segmentCount;
                        float idf = MathF.Log((float)_totalDocCount / (estimatedGlobal + 1));
                        float score = tf * idf;
                        EnqueueCandidate(heap, capacity, score, field, entry.Term);
                        continue;
                    }

                    // Full cross-segment scan for MinDocFreq > 1 or single-segment index.
                    {
                        int docFreq = 0;
                        foreach (var r in _readers)
                        {
                            docFreq += r.GetDocFreqByQualified(qt);
                            if (docFreq > p.MaxDocFreq)
                                goto nextTerm;
                        }

                        if (docFreq < p.MinDocFreq) continue;

                        float idf = MathF.Log((float)_totalDocCount / (docFreq + 1));
                        float score = tf * idf;
                        EnqueueCandidate(heap, capacity, score, field, entry.Term);
                    }
                nextTerm: ;
                }
            }
        }
        finally
        {
            if (qtRented is not null) System.Buffers.ArrayPool<char>.Shared.Return(qtRented);
        }

        if (heap.Count == 0)
            return TopDocs.Empty;

        // Dequeue into a list (ascending score order; we'll iterate in reverse).
        int termCount = heap.Count;
        var candidates = new List<(float Score, string Field, string Term)>(termCount);
        // Ensure we have capacity to hold all entries temporarily when dequeuing.
        while (heap.TryDequeue(out var c, out _))
            candidates.Add(c);

        // Build a BooleanQuery with Should clauses (highest score first).
        var boolQBuilder = new BooleanQuery.Builder();
        float maxScore = candidates[termCount - 1].Score;

        for (int i = termCount - 1; i >= 0; i--)
        {
            var (score, field, term) = candidates[i];
            var tq = new TermQuery(field, term);
            if (p.BoostByScore && maxScore > 0)
                tq.Boost = score / maxScore;
            boolQBuilder.Add(tq, Occur.Should);
        }

        var boolQ = boolQBuilder.Build();
        return SearchCore(boolQ, topN + 1);
    }

    /// <summary>Enqueues a candidate into a bounded min-heap, evicting the
    /// lowest-scoring entry when capacity is exceeded.</summary>
    private static void EnqueueCandidate(
        PriorityQueue<(float Score, string Field, string Term), float> heap,
        int capacity, float score, string field, string term)
    {
        if (heap.Count < capacity)
        {
            heap.Enqueue((score, field, term), score);
        }
        else if (score > heap.Peek().Score)
        {
            heap.Dequeue();
            heap.Enqueue((score, field, term), score);
        }
    }
}
