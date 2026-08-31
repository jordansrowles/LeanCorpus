using System.Collections.Concurrent;
using System.Threading;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
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
        using var segmentLease = reader.AcquireQueryLease();
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
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);

        // Compute global DF
        int globalDF = 0;
        foreach (var r in _readers)
        {
            using var p = r.GetPostingsEnum(qt);
            globalDF += p.DocFreq;
        }

        float idf = Bm25Scorer.Idf(_totalDocCount, globalDF);
        long collectionFreq = RequiresCollectionStatistics(query.Field)
            ? GetGlobalCollectionFreq(qt)
            : 0;
        var (f1, f2, f3) = ComputeTermFactors(globalDF, avgDocLength, collectionFreq, query.Field);
        float score = ScoreTerm(f1, f2, f3, tf, docLength, query.Field);
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
        using var segmentLease = reader.AcquireQueryLease();
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
        => SuggestCore(prefix, field, topN, allowedDocIds: null);

    /// <summary>
    /// Analyses the input and returns prefix completions, optionally restricted to
    /// documents matching a context query.
    /// </summary>
    public IReadOnlyList<(string Term, int DocFreq)> Suggest(
        string input,
        string field,
        int topN,
        IAnalyser analyser,
        Query? contextFilter = null)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        var tokens = AnalyseSuggestionInput(input, analyser);
        if (tokens.Count == 0)
            return [];

        HashSet<int>? allowedDocIds = null;
        if (contextFilter is not null)
        {
            var contextResults = Search(contextFilter, int.MaxValue);
            allowedDocIds = new HashSet<int>(contextResults.ScoreDocs.Length);
            foreach (var scoreDoc in contextResults.ScoreDocs)
                allowedDocIds.Add(scoreDoc.DocId);
            if (allowedDocIds.Count == 0)
                return [];
        }

        return SuggestCore(tokens[^1], field, topN, allowedDocIds);
    }

    /// <summary>Returns phrase-context completions for analysed free text.</summary>
    public IReadOnlyList<(string Term, int DocFreq)> SuggestNext(
        string input,
        string field,
        int topN,
        IAnalyser analyser)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        if (topN <= 0)
            return [];

        var tokens = AnalyseSuggestionInput(input, analyser);
        if (tokens.Count == 0)
            return [];

        bool startsNewTerm = input.Length > 0 && char.IsWhiteSpace(input[^1]);
        string prefix = startsNewTerm ? string.Empty : tokens[^1];
        int contextCount = startsNewTerm ? tokens.Count : tokens.Count - 1;
        var candidates = SuggestCore(prefix, field, Math.Max(64, topN * 8), allowedDocIds: null);
        if (contextCount == 0)
        {
            if (candidates.Count <= topN)
                return candidates;
            var prefixResults = new (string Term, int DocFreq)[topN];
            for (int i = 0; i < topN; i++)
                prefixResults[i] = candidates[i];
            return prefixResults;
        }

        var ranked = new List<(string Term, int DocFreq)>(candidates.Count);
        var phraseTerms = new string[contextCount + 1];
        for (int i = 0; i < contextCount; i++)
            phraseTerms[i] = tokens[i];

        foreach (var candidate in candidates)
        {
            phraseTerms[^1] = candidate.Term;
            int phraseHits = Count(new PhraseQuery(field, phraseTerms.ToArray()));
            if (phraseHits > 0)
                ranked.Add((candidate.Term, phraseHits));
        }

        ranked.Sort(static (left, right) =>
        {
            int frequency = right.DocFreq.CompareTo(left.DocFreq);
            return frequency != 0
                ? frequency
                : string.CompareOrdinal(left.Term, right.Term);
        });
        if (ranked.Count > topN)
            ranked.RemoveRange(topN, ranked.Count - topN);
        return ranked;
    }

    private IReadOnlyList<(string Term, int DocFreq)> SuggestCore(
        string prefix,
        string field,
        int topN,
        HashSet<int>? allowedDocIds)
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
                int frequency = postings.DocFreq;
                if (allowedDocIds is not null)
                {
                    frequency = 0;
                    while (postings.MoveNextUnchecked(out int docId, out _))
                    {
                        if (reader.IsLive(docId)
                            && allowedDocIds.Contains(reader.DocBase + docId))
                        {
                            frequency++;
                        }
                    }
                    if (frequency == 0)
                        continue;
                }
                termFreqs.TryGetValue(bare, out int existing);
                termFreqs[bare] = existing + frequency;
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

    private static List<string> AnalyseSuggestionInput(string input, IAnalyser analyser)
    {
        var tokens = new List<string>();
        analyser.Analyse(input.AsSpan(), new SuggestionTokenSink(tokens));
        return tokens;
    }

    private sealed class SuggestionTokenSink(List<string> tokens) : ISpanTokenSink
    {
        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
            => tokens.Add(text.ToString());
    }

    /// <summary>Executes a query and returns both top-N results and facet counts for the specified fields.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacets(
        Query query, int topN, params string[] facetFields)
        => SearchWithFacetsCore(query, topN, new FacetsSideCollector(facetFields, _readers, _config.MaxExactFacetBuckets));

    /// <summary>Executes the shared facet collection path for advanced requests.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacetRequests(
        Query query, int topN, IReadOnlyList<IFacetRequest> facetRequests)
        => SearchWithFacetsCore(query, topN, new FacetsSideCollector(facetRequests, _readers, _config.MaxExactFacetBuckets));

    /// <summary>Executes an exhaustive facet request with cooperative cancellation.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacetRequests(
        Query query,
        int topN,
        IReadOnlyList<IFacetRequest> facetRequests,
        CancellationToken cancellationToken)
        => SearchWithFacetsCore(query, topN, new FacetsSideCollector(
            facetRequests, _readers, _config.MaxExactFacetBuckets, cancellationToken));

    /// <summary>
    /// Executes a drill-down search and computes facet counts with the selected
    /// dimension's own constraint removed from its count scope.
    /// </summary>
    /// <param name="query">The drill-down query containing the base query and selections.</param>
    /// <param name="topN">The maximum number of filtered hits to return.</param>
    /// <param name="facetRequests">The facet requests to evaluate with sideways scopes.</param>
    /// <returns>Filtered hits and facet results calculated using their sideways scopes.</returns>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithDrillSideways(
        DrillDownQuery query, int topN, params IFacetRequest[] facetRequests)
    {
        ArgumentNullException.ThrowIfNull(facetRequests);
        return SearchWithDrillSideways(query, topN, (IReadOnlyList<IFacetRequest>)facetRequests);
    }

    /// <summary>
    /// Executes a drill-down search and computes facet counts with the selected
    /// dimension's own constraint removed from its count scope.
    /// </summary>
    /// <remarks>
    /// The filtered hits use all selections. A selected dimension's facet scope
    /// keeps every other dimension selected, while an unselected dimension uses
    /// the complete drill-down query. All scopes are derived from one base-query
    /// match traversal.
    /// </remarks>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithDrillSideways(
        DrillDownQuery query, int topN, IReadOnlyList<IFacetRequest> facetRequests)
        => SearchWithDrillSideways(query, topN, facetRequests, CancellationToken.None);

    /// <summary>Executes drill-sideways facet collection with cooperative cancellation.</summary>
    public (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithDrillSideways(
        DrillDownQuery query,
        int topN,
        IReadOnlyList<IFacetRequest> facetRequests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(facetRequests);
        for (int i = 0; i < facetRequests.Count; i++)
            ArgumentNullException.ThrowIfNull(facetRequests[i]);

        if (facetRequests.Count == 0)
            return (Search(query, topN), []);

        var collector = new DrillSidewaysSideCollector(query, topN, facetRequests, _readers, _config.MaxExactFacetBuckets, cancellationToken);
        SearchWithSideCollector(query.BaseQuery, topN, collector);
        return (collector.GetResults(), collector.GetFacets());
    }

    private (TopDocs Results, IReadOnlyList<FacetResult> Facets) SearchWithFacetsCore(
        Query query, int topN, FacetsSideCollector sideCollector)
    {
        var (results, _) = SearchWithSideCollector(query, topN, sideCollector);
        return (results, sideCollector.GetResults());
    }

    // --- Aggregations ---

    /// <summary>
    /// Executes a search query and computes numeric aggregations over matching documents.
    /// </summary>
    public (TopDocs Results, AggregationResult[] Aggregations) SearchWithAggregations(
        Query query, int topN, params AggregationRequest[] aggregations)
    {
        if (aggregations.Length == 0)
            return (Search(query, topN), []);

        var sideCollector = new AggregationSideCollector(aggregations, _readers);
        var (results, _) = SearchWithSideCollector(query, topN, sideCollector);
        if (results.TotalHits == 0) return (results, []);
        return (results, sideCollector.GetResults());
    }

    /// <summary>Executes exhaustive numeric aggregations with cooperative cancellation.</summary>
    public (TopDocs Results, AggregationResult[] Aggregations) SearchWithAggregations(
        Query query,
        int topN,
        AggregationRequest[] aggregations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        if (aggregations.Length == 0)
            return (Search(query, topN, cancellationToken), []);

        var sideCollector = new AggregationSideCollector(aggregations, _readers, cancellationToken);
        var (results, _) = SearchWithSideCollector(query, topN, sideCollector);
        return results.TotalHits == 0 ? (results, []) : (results, sideCollector.GetResults());
    }

    // --- Result Collapsing ---

    /// <summary>
    /// Executes a search and collapses results so only the best document per unique field value is returned.
    /// Uses SortedDocValues for the collapse field.
    /// </summary>
    public TopDocs SearchWithCollapse(Query query, int topN, CollapseField collapse)
    {
        int candidateN = Math.Min(_totalDocCount, topN * 10);
        var sideCollector = new CollapseSideCollector(collapse, topN);
        var (results, _) = SearchWithSideCollector(query, candidateN, sideCollector);
        if (results.TotalHits == 0) return TopDocs.Empty;
        return sideCollector.ToTopDocs();
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

    internal TopDocs ExecuteMoreLikeThis(MoreLikeThisQuery mlt, int topN, ISideCollector? sideCollector = null)
    {
        var p = mlt.Parameters;
        int readerIdx = ResolveReaderIndex(mlt.DocId);
        var reader = _readers[readerIdx];
        int localDocId = mlt.DocId - _docBases[readerIdx];
        int segmentCount = _readers.Count;

        // Check the MLT term cache for a previous extraction with the same parameters.
        var cacheKey = new MltCacheKey(mlt.DocId, p.MaxQueryTerms,
            p.MinTermFreq, p.MinDocFreq, p.MinWordLength);
        if (_mltCache != null && _mltCache.TryGetValue(cacheKey, out var cachedTerms))
        {
            var cachedBuilder = new BooleanQuery.Builder();
            for (int i = cachedTerms.Length - 1; i >= 0; i--)
                cachedBuilder.Add(new TermQuery(cachedTerms[i].Field, cachedTerms[i].Term), Occur.Should);
            var cachedBoolQ = cachedBuilder.Build();
            var cachedResults = SearchCore(cachedBoolQ, topN,
                sideCollector is null ? null : new ExcludingSideCollector(sideCollector, mlt.DocId));
            var cachedScoreDocs = cachedResults.ScoreDocs;
            int cachedSrcIdx = -1;
            for (int i = 0; i < cachedScoreDocs.Length; i++)
                if (cachedScoreDocs[i].DocId == mlt.DocId) { cachedSrcIdx = i; break; }
            if (cachedSrcIdx < 0) return cachedResults;
            var cachedFiltered = new ScoreDoc[cachedScoreDocs.Length - 1];
            if (cachedSrcIdx > 0) Array.Copy(cachedScoreDocs, 0, cachedFiltered, 0, cachedSrcIdx);
            if (cachedSrcIdx < cachedScoreDocs.Length - 1)
                Array.Copy(cachedScoreDocs, cachedSrcIdx + 1, cachedFiltered, cachedSrcIdx,
                    cachedScoreDocs.Length - cachedSrcIdx - 1);
            return new TopDocs(cachedResults.TotalHits - 1, cachedFiltered, cachedResults.IsPartial);
        }

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
                    string qtStr = new string(buf, 0, qtLen);
                    // Fast path: MinDocFreq <= 1 with multiple segments — use local
                    // segment's docFreq scaled by segment count as an IDF approximation.
                    if (p.MinDocFreq <= 1 && segmentCount > 1)
                    {
                        int currDocFreq = reader.GetDocFreqByQualified(qtStr);
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
                            docFreq += r.GetDocFreqByQualified(qtStr);
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

        // Cache the extracted candidate terms for reuse.
        var cacheTerms = new (string Field, string Term, float Score)[termCount];
        for (int i = 0; i < termCount; i++)
            cacheTerms[i] = (candidates[i].Field, candidates[i].Term, candidates[i].Score);
        _mltCache ??= new ConcurrentDictionary<MltCacheKey, (string, string, float)[]>();
        _mltCache[cacheKey] = cacheTerms;
        if (Interlocked.Increment(ref _mltCacheCount) >= MltCacheSoftCap)
        {
            Interlocked.Exchange(ref _mltCache,
                new ConcurrentDictionary<MltCacheKey, (string, string, float)[]>());
            Interlocked.Exchange(ref _mltCacheCount, 0);
        }

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
        var results = SearchCore(boolQ, topN,
            sideCollector is null ? null : new ExcludingSideCollector(sideCollector, mlt.DocId));

        // Exclude the source document from results.
        var scoreDocs = results.ScoreDocs;
        int sourceIdx = -1;
        for (int i = 0; i < scoreDocs.Length; i++)
        {
            if (scoreDocs[i].DocId == mlt.DocId)
            {
                sourceIdx = i;
                break;
            }
        }

        if (sourceIdx < 0)
            return results;

        // Build a new array without the source document.
        var filtered = new ScoreDoc[scoreDocs.Length - 1];
        if (sourceIdx > 0)
            Array.Copy(scoreDocs, 0, filtered, 0, sourceIdx);
        if (sourceIdx < scoreDocs.Length - 1)
            Array.Copy(scoreDocs, sourceIdx + 1, filtered, sourceIdx, scoreDocs.Length - sourceIdx - 1);

        return new TopDocs(results.TotalHits - 1, filtered, results.IsPartial);
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

    // --- Side collectors (Phase 1a) ---

    private sealed class AggregationSideCollector : ISideCollector
    {
        private readonly NumericAggregationCollector _collector;

        private readonly CancellationToken _cancellationToken;

        public AggregationSideCollector(
            AggregationRequest[] requests,
            IReadOnlyList<Index.Segment.SegmentReader> readers,
            CancellationToken cancellationToken = default)
        {
            _collector = NumericAggregator.CreateCollector(requests, readers);
            _cancellationToken = cancellationToken;
        }

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _collector.Collect(reader, localDocId);
        }

        public AggregationResult[] GetResults() => _collector.Finish(_cancellationToken);
    }

    private sealed class ExcludingSideCollector(ISideCollector inner, int excludedGlobalDocId) : ISideCollector
    {
        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            if (globalDocId != excludedGlobalDocId)
                inner.Collect(globalDocId, score, reader, localDocId);
        }
    }

    private (TopDocs Results, ISideCollector? Side) SearchWithSideCollector(
        Query query, int topN, ISideCollector? sideCollector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sideCollector);
        return (SearchCore(query, Math.Max(0, NormaliseTopN(topN)), sideCollector), sideCollector);
    }

    private sealed class CollapseSideCollector : ISideCollector
    {
        private readonly CollapseField _collapse;
        private readonly int _topN;
        private readonly Dictionary<string, ScoreDoc> _bestPerGroup = new(StringComparer.Ordinal);

        public CollapseSideCollector(CollapseField collapse, int topN)
        {
            _collapse = collapse;
            _topN = topN;
        }

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            string groupValue = ResolveCollapseValue(reader, _collapse.FieldName, localDocId);

            if (!_bestPerGroup.TryGetValue(groupValue, out var existing))
            {
                _bestPerGroup[groupValue] = new ScoreDoc(globalDocId, score);
            }
            else
            {
                bool replace = _collapse.Mode == CollapseMode.TopScore
                    ? score > existing.Score
                    : score < existing.Score;
                if (replace)
                    _bestPerGroup[groupValue] = new ScoreDoc(globalDocId, score);
            }
        }

        public TopDocs ToTopDocs()
        {
            var collapsed = _bestPerGroup.Values
                .OrderByDescending(sd => sd.Score)
                .Take(_topN)
                .ToArray();
            return new TopDocs(_bestPerGroup.Count, collapsed);
        }
    }

    private sealed class DrillSidewaysSideCollector : ISideCollector
    {
        private TopNCollector _hits;
        private readonly IFacetRequest[] _requests;
        private readonly SelectionGroup[] _selectionGroups;
        private readonly FacetsSideCollector? _allMatching;
        private readonly List<int> _unselectedIndexes = [];
        private readonly Dictionary<string, (FacetsSideCollector Collector, List<int> Indexes)> _selected = new(StringComparer.Ordinal);
        private readonly CancellationToken _cancellationToken;

        public DrillSidewaysSideCollector(DrillDownQuery query, int topN, IReadOnlyList<IFacetRequest> requests,
            IReadOnlyList<Index.Segment.SegmentReader> readers, int maxExactFacetBuckets,
            CancellationToken cancellationToken)
        {
            _hits = new TopNCollector(topN);
            _requests = requests.ToArray();
            _cancellationToken = cancellationToken;
            _selectionGroups = query.Selections
                .GroupBy(static selection => selection.Field, StringComparer.Ordinal)
                .Select(static group => new SelectionGroup(
                    group.Key,
                    group.Select(static selection => selection.IndexedValue)))
                .ToArray();
            var selectedFields = _selectionGroups
                .Select(static group => group.Field)
                .ToHashSet(StringComparer.Ordinal);
            var unselected = new List<IFacetRequest>();
            for (int i = 0; i < _requests.Length; i++)
            {
                if (!selectedFields.Contains(_requests[i].Field))
                {
                    unselected.Add(_requests[i]);
                    _unselectedIndexes.Add(i);
                }
            }
            _allMatching = unselected.Count == 0 ? null : new FacetsSideCollector(unselected, readers, maxExactFacetBuckets, cancellationToken);
            foreach (var field in selectedFields)
            {
                var indexes = new List<int>();
                var grouped = new List<IFacetRequest>();
                for (int i = 0; i < _requests.Length; i++)
                    if (_requests[i].Field == field) { indexes.Add(i); grouped.Add(_requests[i]); }
                    _selected.Add(field, (new FacetsSideCollector(grouped, readers, maxExactFacetBuckets, cancellationToken), indexes));
            }
        }

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string? failed = null;
            int failures = 0;
            foreach (var group in _selectionGroups)
            {
                if (group.Matches(reader, localDocId)) continue;
                failed = group.Field;
                failures++;
            }

            if (failures == 0)
            {
                _hits.Collect(globalDocId, score);
                _allMatching?.Collect(globalDocId, score, reader, localDocId);
            }

            foreach (var (field, entry) in _selected)
            {
                if (failures == 0 || (failures == 1 && field == failed))
                    entry.Collector.Collect(globalDocId, score, reader, localDocId);
            }
        }

        private sealed class SelectionGroup(string field, IEnumerable<string> indexedValues)
        {
            private readonly HashSet<string> _indexedValues = indexedValues.ToHashSet(StringComparer.Ordinal);

            public string Field { get; } = field;

            public bool Matches(Index.Segment.SegmentReader reader, int localDocId)
            {
                if (reader.TryGetSortedSetDocValues(Field, localDocId, out var values))
                {
                    foreach (var value in values)
                    {
                        if (_indexedValues.Contains(value))
                            return true;
                    }
                    return false;
                }

                return reader.TryGetSortedDocValue(Field, localDocId, out var sortedValue)
                    && _indexedValues.Contains(sortedValue);
            }
        }

        public TopDocs GetResults() => _hits.ToTopDocs();

        public IReadOnlyList<FacetResult> GetFacets()
        {
            var results = new FacetResult[_requests.Length];
            if (_allMatching is not null)
            {
                var regular = _allMatching.GetResults(_cancellationToken);
                for (int i = 0; i < _unselectedIndexes.Count; i++)
                    results[_unselectedIndexes[i]] = regular[i];
            }
            foreach (var entry in _selected.Values)
            {
                var sideways = entry.Collector.GetResults(_cancellationToken);
                for (int i = 0; i < entry.Indexes.Count; i++)
                    results[entry.Indexes[i]] = sideways[i];
            }
            return results;
        }
    }

    private sealed class FacetsSideCollector : ISideCollector
    {
        private readonly IFacetRequest[] _facetRequests;
        private readonly FacetsCollector _facetsCollector;
        private readonly NumericFieldAccessor[] _numericAccessors;
        private readonly NumericRangeExecutionPlan?[] _numericRangePlans;
        private readonly Int64RangeExecutionPlan?[] _int64RangePlans;
        private readonly Dictionary<string, FlatFacetOrdinalPlan> _flatOrdinalPlans = new(StringComparer.Ordinal);
        private readonly Dictionary<Index.Segment.SegmentReader, int> _readerIndexes = new(ReferenceEqualityComparer.Instance);
        private readonly CancellationToken _cancellationToken;

        public FacetsSideCollector(
            string[] facetFields,
            IReadOnlyList<Index.Segment.SegmentReader> readers,
            int maxExactFacetBuckets,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(facetFields);
            var distinctFields = facetFields.Distinct(StringComparer.Ordinal).ToArray();
            _facetRequests = distinctFields.Select(field => (IFacetRequest)new FacetRequest(field)).ToArray();
            _facetsCollector = new FacetsCollector(
                _facetRequests,
                includeEmptyResults: false,
                maxExactBuckets: maxExactFacetBuckets);
            _numericAccessors = ResolveNumericAccessors(_facetRequests, readers);
            (_numericRangePlans, _int64RangePlans) = BuildRangePlans(_facetRequests);
            _cancellationToken = cancellationToken;
            InitialiseStringFacetPlans(readers);
        }

        public FacetsSideCollector(
            IReadOnlyList<IFacetRequest> facetRequests,
            IReadOnlyList<Index.Segment.SegmentReader> readers,
            int maxExactFacetBuckets,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(facetRequests);
            _facetRequests = facetRequests.ToArray();
            _facetsCollector = new FacetsCollector(facetRequests, maxExactBuckets: maxExactFacetBuckets);
            _numericAccessors = ResolveNumericAccessors(_facetRequests, readers);
            (_numericRangePlans, _int64RangePlans) = BuildRangePlans(_facetRequests);
            _cancellationToken = cancellationToken;
            InitialiseStringFacetPlans(readers);
            RegisterRangeBuckets();
        }

        private void InitialiseStringFacetPlans(IReadOnlyList<Index.Segment.SegmentReader> readers)
        {
            for (int readerIndex = 0; readerIndex < readers.Count; readerIndex++)
                _readerIndexes[readers[readerIndex]] = readerIndex;

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in _facetRequests)
            {
                if (request is not (NumericRangeFacetRequest or Int64RangeFacetRequest or DateRangeFacetRequest or DateHistogramFacetRequest))
                    fields.Add(request.Field);
            }

            foreach (string field in fields)
            {
                bool hasSorted = false;
                bool hasSortedSet = false;
                bool hasSortedOnly = false;
                bool hasSortedSetOnly = false;
                bool fieldPresent = false;
                string? incompatibleSegment = null;
                foreach (var reader in readers)
                {
                    bool readerHasSorted = reader.GetSortedDocValueTerms(field) is not null;
                    bool readerHasSortedSet = reader.GetSortedSetDocValueTerms(field) is not null;
                    hasSorted |= readerHasSorted;
                    hasSortedSet |= readerHasSortedSet;
                    hasSortedOnly |= readerHasSorted && !readerHasSortedSet;
                    hasSortedSetOnly |= readerHasSortedSet && !readerHasSorted;
                    bool readerHasBinary = reader.GetBinaryDocValues(field) is not null;
                    bool readerHasField = reader.Info.FieldNames.Contains(field, StringComparer.Ordinal);
                    fieldPresent |= readerHasField;
                    if (!readerHasSorted && !readerHasSortedSet && (readerHasBinary || readerHasField))
                    {
                        incompatibleSegment ??= reader.Info.SegmentId;
                    }
                }

                if (hasSortedOnly && hasSortedSetOnly)
                    throw new InvalidOperationException($"Facet field '{field}' has incompatible Sorted and SortedSet DocValues representations across segments.");
                if (incompatibleSegment is not null)
                    throw new InvalidOperationException(
                        $"Facet field '{field}' requires Sorted or SortedSet DocValues; segment '{incompatibleSegment}' contains an incompatible representation.");
                if (!hasSorted && !hasSortedSet && fieldPresent)
                    throw new InvalidOperationException($"Facet field '{field}' requires Sorted or SortedSet DocValues.");

                bool sortedSet = hasSortedSet && !hasSortedOnly;
                var sourceTerms = new IReadOnlyList<string>[readers.Count];
                for (int i = 0; i < readers.Count; i++)
                {
                    sourceTerms[i] = (sortedSet
                        ? readers[i].GetSortedSetDocValueTerms(field)
                        : readers[i].GetSortedDocValueTerms(field)) ?? Array.Empty<string>();
                }

                var plan = new FlatFacetOrdinalPlan(field, sortedSet, OrdinalMap.Build(sourceTerms), hasSorted || hasSortedSet);
                _flatOrdinalPlans.Add(field, plan);
                for (int i = 0; i < _facetRequests.Length; i++)
                {
                    if (_facetRequests[i] is FacetRequest
                        && string.Equals(_facetRequests[i].Field, field, StringComparison.Ordinal))
                        _facetsCollector.ConfigureOrdinalFlat(i, plan.OrdinalMap);
                }
            }
        }

        private static NumericFieldAccessor[] ResolveNumericAccessors(
            IReadOnlyList<IFacetRequest> requests,
            IReadOnlyList<Index.Segment.SegmentReader> readers)
        {
            var accessors = new NumericFieldAccessor[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i] is NumericRangeFacetRequest
                    or Int64RangeFacetRequest
                    or DateRangeFacetRequest
                    or DateHistogramFacetRequest)
                {
                    accessors[i] = NumericFieldValues.ResolveFieldAccessor(requests[i].Field, readers);
                    bool fieldExists = false;
                    foreach (var reader in readers)
                    {
                        if (reader.HasNumericField(requests[i].Field))
                        {
                            fieldExists = true;
                            break;
                        }
                    }

                    if (fieldExists
                        && requests[i] is Int64RangeFacetRequest or DateRangeFacetRequest or DateHistogramFacetRequest
                        && !accessors[i].IsInt64)
                    {
                        throw new InvalidOperationException(
                            $"Facet field '{requests[i].Field}' requires Int64 DocValues for {requests[i].GetType().Name}.");
                    }
                }
            }

            return accessors;
        }

        private static (NumericRangeExecutionPlan?[] Numeric, Int64RangeExecutionPlan?[] Int64) BuildRangePlans(
            IReadOnlyList<IFacetRequest> requests)
        {
            var numeric = new NumericRangeExecutionPlan?[requests.Count];
            var int64 = new Int64RangeExecutionPlan?[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                switch (requests[i])
                {
                    case NumericRangeFacetRequest request:
                        numeric[i] = new NumericRangeExecutionPlan(request.Ranges);
                        break;
                    case Int64RangeFacetRequest request:
                        int64[i] = new Int64RangeExecutionPlan(request.Ranges);
                        break;
                    case DateRangeFacetRequest request:
                        int64[i] = new Int64RangeExecutionPlan(request.EncodedRanges);
                        break;
                }
            }
            return (numeric, int64);
        }

        private void RegisterRangeBuckets()
        {
            for (int i = 0; i < _facetRequests.Length; i++)
            {
                var request = _facetRequests[i];
                switch (request)
                {
                    case NumericRangeFacetRequest numeric:
                        foreach (var range in numeric.Ranges)
                            _facetsCollector.RegisterBucket(i, range.Label);
                        break;
                    case Int64RangeFacetRequest int64:
                        foreach (var range in int64.Ranges)
                            _facetsCollector.RegisterBucket(i, range.Label);
                        break;
                    case DateRangeFacetRequest date:
                        foreach (var range in date.Ranges)
                            _facetsCollector.RegisterBucket(i, range.Label);
                        break;
                }
            }
        }

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < _facetRequests.Length; i++)
            {
                var request = _facetRequests[i];
                bool hasValue = request switch
                {
                    HierarchicalFacetRequest hierarchical =>
                        CollectHierarchy(i, hierarchical, globalDocId, reader, localDocId),
                    NumericRangeFacetRequest numeric =>
                        CollectNumericRange(i, numeric, _numericAccessors[i], globalDocId, reader, localDocId),
                    Int64RangeFacetRequest int64 =>
                        CollectInt64Range(i, int64, _numericAccessors[i], globalDocId, reader, localDocId),
                    DateRangeFacetRequest date =>
                        CollectDateRange(i, date, _numericAccessors[i], globalDocId, reader, localDocId),
                    DateHistogramFacetRequest histogram =>
                        CollectDateHistogram(i, histogram, _numericAccessors[i], globalDocId, reader, localDocId),
                    _ => CollectFlat(i, request.Field, globalDocId, reader, localDocId)
                };

                if (!hasValue)
                    _facetsCollector.CollectMissing(i, globalDocId);
            }
        }

        private bool CollectNumericRange(int requestIndex,
            NumericRangeFacetRequest request,
            NumericFieldAccessor accessor,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            if (!NumericFieldValues.TryRead(reader, request.Field, localDocId, accessor, out var values))
                return false;

            if (values.IsInt64)
            {
                if (values.Int64Values is not null)
                {
                    foreach (long value in values.Int64Values)
                        CollectNumericRangeValue(requestIndex, request, globalDocId, value);
                }
                else
                {
                    CollectNumericRangeValue(requestIndex, request, globalDocId, values.Int64Value);
                }
            }
            else if (values.DoubleValues is not null)
            {
                foreach (double value in values.DoubleValues)
                    CollectNumericRangeValue(requestIndex, request, globalDocId, value);
            }
            else
            {
                CollectNumericRangeValue(requestIndex, request, globalDocId, values.DoubleValue);
            }

            return true;
        }

        private void CollectNumericRangeValue(int requestIndex,
            NumericRangeFacetRequest request,
            int globalDocId,
            long value)
        {
            int matched = _numericRangePlans[requestIndex]!.Find(value);
            if (matched >= 0)
                _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[matched].Label);
            else if (!_numericRangePlans[requestIndex]!.IsNonOverlapping)
            {
                int checks = 0;
                foreach (int index in _numericRangePlans[requestIndex]!.FindOverlapping(value))
                {
                    if ((checks++ & 31) == 0)
                        _cancellationToken.ThrowIfCancellationRequested();
                    _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[index].Label);
                }
            }
        }

        private void CollectNumericRangeValue(int requestIndex,
            NumericRangeFacetRequest request,
            int globalDocId,
            double value)
        {
            int matched = _numericRangePlans[requestIndex]!.Find(value);
            if (matched >= 0)
                _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[matched].Label);
            else if (!_numericRangePlans[requestIndex]!.IsNonOverlapping)
            {
                int checks = 0;
                foreach (int index in _numericRangePlans[requestIndex]!.FindOverlapping(value))
                {
                    if ((checks++ & 31) == 0)
                        _cancellationToken.ThrowIfCancellationRequested();
                    _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[index].Label);
                }
            }
        }

        private bool CollectInt64Range(int requestIndex,
            Int64RangeFacetRequest request,
            NumericFieldAccessor accessor,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            if (!NumericFieldValues.TryRead(reader, request.Field, localDocId, accessor, out var values))
                return false;

            if (!values.IsInt64)
                throw new InvalidOperationException($"Int64 range facet field '{request.Field}' has an incompatible numeric representation.");

            if (values.Int64Values is not null)
            {
                foreach (long value in values.Int64Values)
                    CollectInt64RangeValue(requestIndex, request, globalDocId, value);
            }
            else
            {
                CollectInt64RangeValue(requestIndex, request, globalDocId, values.Int64Value);
            }

            return true;
        }

        private void CollectInt64RangeValue(int requestIndex,
            Int64RangeFacetRequest request,
            int globalDocId,
            long value)
        {
            int matched = _int64RangePlans[requestIndex]!.Find(value);
            if (matched >= 0)
                _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[matched].Label);
            else if (!_int64RangePlans[requestIndex]!.IsNonOverlapping)
            {
                int checks = 0;
                foreach (int index in _int64RangePlans[requestIndex]!.FindOverlapping(value))
                {
                    if ((checks++ & 31) == 0)
                        _cancellationToken.ThrowIfCancellationRequested();
                    _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[index].Label);
                }
            }
        }

        private bool CollectDateRange(int requestIndex,
            DateRangeFacetRequest request,
            NumericFieldAccessor accessor,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            if (!NumericFieldValues.TryRead(reader, request.Field, localDocId, accessor, out var values))
                return false;

            if (!values.IsInt64)
                throw new InvalidOperationException($"Date range facet field '{request.Field}' has an incompatible numeric representation.");

            if (values.Int64Values is not null)
            {
                foreach (long value in values.Int64Values)
                    CollectDateRangeValue(requestIndex, request, globalDocId, value);
            }
            else
            {
                CollectDateRangeValue(requestIndex, request, globalDocId, values.Int64Value);
            }

            return true;
        }

        private void CollectDateRangeValue(int requestIndex,
            DateRangeFacetRequest request,
            int globalDocId,
            long value)
        {
            int matched = _int64RangePlans[requestIndex]!.Find(value);
            if (matched >= 0)
                _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[matched].Label);
            else if (!_int64RangePlans[requestIndex]!.IsNonOverlapping)
            {
                int checks = 0;
                foreach (int index in _int64RangePlans[requestIndex]!.FindOverlapping(value))
                {
                    if ((checks++ & 31) == 0)
                        _cancellationToken.ThrowIfCancellationRequested();
                    _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, request.Ranges[index].Label);
                }
            }
        }

        private bool CollectDateHistogram(int requestIndex,
            DateHistogramFacetRequest request,
            NumericFieldAccessor accessor,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            if (!NumericFieldValues.TryRead(reader, request.Field, localDocId, accessor, out var values))
                return false;

            if (!values.IsInt64)
                throw new InvalidOperationException($"Date histogram facet field '{request.Field}' has an incompatible numeric representation.");

            if (values.Int64Values is not null)
            {
                foreach (long value in values.Int64Values)
                    CollectDateHistogramValue(requestIndex, request, globalDocId, value);
            }
            else
            {
                CollectDateHistogramValue(requestIndex, request, globalDocId, values.Int64Value);
            }

            return true;
        }

        private void CollectDateHistogramValue(int requestIndex, DateHistogramFacetRequest request, int globalDocId, long value)
        {
            var (start, end) = request.Interval.GetBucket(value);
            _facetsCollector.CollectDateHistogramBucket(requestIndex, globalDocId, start, end);
        }

        private bool CollectFlat(
            int requestIndex,
            string facetField,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            var plan = _flatOrdinalPlans[facetField];
            int readerIndex = _readerIndexes[reader];
            bool hasValue = false;
            if (plan.SortedSet && reader.TryGetSortedSetDocValues(facetField, localDocId, out var setValues))
            {
                int lastOrdinal = -1;
                foreach (var value in setValues)
                {
                    if (plan.OrdinalMap.TryGetGlobalOrdinal(readerIndex, value, out int globalOrdinal)
                        && globalOrdinal != lastOrdinal)
                    {
                        _facetsCollector.CollectFlatDocumentOrdinal(requestIndex, globalOrdinal);
                        lastOrdinal = globalOrdinal;
                        hasValue = true;
                    }
                }
            }
            else if (!plan.SortedSet && reader.TryGetSortedDocValue(facetField, localDocId, out string val))
            {
                if (plan.OrdinalMap.TryGetGlobalOrdinal(readerIndex, val, out int globalOrdinal))
                {
                    _facetsCollector.CollectFlatDocumentOrdinal(requestIndex, globalOrdinal);
                    hasValue = true;
                }
            }
            return hasValue;
        }

        private bool CollectHierarchy(
            int requestIndex,
            HierarchicalFacetRequest request,
            int globalDocId,
            Index.Segment.SegmentReader reader,
            int localDocId)
        {
            bool hasHierarchyValue = false;
            if (reader.TryGetSortedSetDocValues(request.Field, localDocId, out var setValues))
            {
                foreach (var value in setValues)
                {
                    if (value is null)
                        continue;

                    hasHierarchyValue |= CollectHierarchyValue(requestIndex, request, globalDocId, value);
                }
            }
            else if (reader.TryGetSortedDocValue(request.Field, localDocId, out string value))
            {
                hasHierarchyValue = CollectHierarchyValue(requestIndex, request, globalDocId, value);
            }
            return hasHierarchyValue;
        }

        private bool CollectHierarchyValue(
            int requestIndex,
            HierarchicalFacetRequest request,
            int globalDocId,
            string encodedValue)
        {
            if (!FacetPathEncoder.IsEncodedPath(encodedValue))
                return false;

            if (FacetPathEncoder.TryGetImmediateChild(encodedValue, request.ParentPath, out string? child))
                _facetsCollector.CollectDocumentValue(requestIndex, globalDocId, child!);

            return true;
        }

        public IReadOnlyList<FacetResult> GetResults() => GetResults(_cancellationToken);

        public IReadOnlyList<FacetResult> GetResults(CancellationToken cancellationToken)
            => _facetsCollector.GetResults(cancellationToken);

        private sealed class FlatFacetOrdinalPlan(
            string field,
            bool sortedSet,
            OrdinalMap ordinalMap,
            bool hasDocValues)
        {
            public string Field { get; } = field;
            public bool SortedSet { get; } = sortedSet;
            public OrdinalMap OrdinalMap { get; } = ordinalMap;
            public bool HasDocValues { get; } = hasDocValues;
        }
    }
}
