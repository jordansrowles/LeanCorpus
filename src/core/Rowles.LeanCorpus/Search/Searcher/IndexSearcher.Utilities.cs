using System.Collections.Concurrent;
using System.Threading;
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
        var sideCollector = new FacetsSideCollector(facetFields);
        var (results, side) = SearchWithSideCollector(query, topN, sideCollector);
        if (side == null)
        {
            // Fallback: non-TermQuery, use two-pass
            var matches = SearchAllMatches(query, results.TotalHits);
            var seenDocs = new HashSet<int>();
            foreach (var sd in matches.ScoreDocs)
            {
                int readerIdx = ResolveReaderIndex(sd.DocId);
                var reader = _readers[readerIdx];
                int localDocId = sd.DocId - _docBases[readerIdx];
                sideCollector.Collect(sd.DocId, sd.Score, reader, localDocId);
            }
            return (results, sideCollector.GetResults());
        }
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

        var sideCollector = new AggregationSideCollector();
        var (results, side) = SearchWithSideCollector(query, topN, sideCollector);
        if (side == null)
        {
            // Fallback: non-TermQuery — use two-pass
            if (results.TotalHits == 0) return (results, []);
            var matches = SearchAllMatches(query, results.TotalHits);
            return (results, NumericAggregator.Aggregate(
                matches.ScoreDocs.AsSpan(), aggregations, _readers, _docBases, _totalDocCount));
        }
        if (results.TotalHits == 0) return (results, []);
        return (results, NumericAggregator.Aggregate(
            sideCollector.DocIds, aggregations, _readers, _docBases, _totalDocCount));
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
        var (results, side) = SearchWithSideCollector(query, candidateN, sideCollector);
        if (side == null)
        {
            // Fallback: non-TermQuery — use two-pass
            var topResults = Search(query, topN);
            var allResults = SearchAllMatches(query, topResults.TotalHits);
            if (allResults.TotalHits == 0) return allResults;
            foreach (var sd in allResults.ScoreDocs)
            {
                int readerIdx = ResolveReaderIndex(sd.DocId);
                var reader = _readers[readerIdx];
                int localDocId = sd.DocId - _docBases[readerIdx];
                sideCollector.Collect(sd.DocId, sd.Score, reader, localDocId);
            }
            return sideCollector.ToTopDocs();
        }
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

        // Check the MLT term cache for a previous extraction with the same parameters.
        var cacheKey = new MltCacheKey(mlt.DocId, p.MaxQueryTerms,
            p.MinTermFreq, p.MinDocFreq, p.MinWordLength);
        if (_mltCache != null && _mltCache.TryGetValue(cacheKey, out var cachedTerms))
        {
            var cachedBuilder = new BooleanQuery.Builder();
            for (int i = cachedTerms.Length - 1; i >= 0; i--)
                cachedBuilder.Add(new TermQuery(cachedTerms[i].Field, cachedTerms[i].Term), Occur.Should);
            var cachedBoolQ = cachedBuilder.Build();
            var cachedResults = SearchCore(cachedBoolQ, topN);
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
        var results = SearchCore(boolQ, topN);

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
        private readonly List<int> _docIds = new();

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            _docIds.Add(globalDocId);
        }

        public ReadOnlySpan<int> DocIds => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_docIds);
    }

    private (TopDocs Results, ISideCollector? Side) SearchWithSideCollector(
        Query query, int topN, ISideCollector? sideCollector)
    {
        if (query is TermQuery tq)
        {
            var results = SearchTermQuery(tq, topN, sideCollector);
            return (results, sideCollector);
        }

        var topResults = Search(query, topN);
        return (topResults, null);
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

    private sealed class FacetsSideCollector : ISideCollector
    {
        private readonly string[] _facetFields;
        private readonly FacetsCollector _facetsCollector = new();
        private readonly HashSet<int> _seenDocs = [];

        public FacetsSideCollector(string[] facetFields)
        {
            _facetFields = facetFields;
        }

        public void Collect(int globalDocId, float score, Index.Segment.SegmentReader reader, int localDocId)
        {
            if (!_seenDocs.Add(globalDocId)) return;

            foreach (var facetField in _facetFields)
            {
                if (reader.TryGetSortedSetDocValues(facetField, localDocId, out var setValues))
                {
                    foreach (var value in setValues)
                    {
                        if (!string.IsNullOrEmpty(value))
                            _facetsCollector.Collect(facetField, value);
                    }
                }
                else if (reader.TryGetSortedDocValue(facetField, localDocId, out string val) && !string.IsNullOrEmpty(val))
                {
                    _facetsCollector.Collect(facetField, val);
                }
                else if (reader.TryGetBinaryDocValues(facetField, localDocId, out var binaryValues))
                {
                    foreach (var value in binaryValues)
                    {
                        var decoded = System.Text.Encoding.UTF8.GetString(value);
                        if (!string.IsNullOrEmpty(decoded))
                            _facetsCollector.Collect(facetField, decoded);
                    }
                }
                else
                {
                    var stored = reader.GetStoredFields(localDocId, new HashSet<string> { facetField });
                    if (stored.TryGetValue(facetField, out var values))
                    {
                        foreach (var v in values)
                            _facetsCollector.Collect(facetField, v);
                    }
                }
            }
        }

        public IReadOnlyList<FacetResult> GetResults() => _facetsCollector.GetResults();
    }
}
