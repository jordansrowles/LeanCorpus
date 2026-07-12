using Rowles.LeanCorpus.Analysis.Analysers;
using System.Runtime.CompilerServices;
using System.Threading;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Store;
using System.Collections.Concurrent;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Scoring;
namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Holds a snapshot of segment readers and executes queries across all segments.
/// </summary>
public sealed partial class IndexSearcher : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly List<SegmentReader> _readers = [];
    private readonly int[] _docBases;
    private readonly int _totalDocCount;
    private readonly IndexStats _stats;
    private readonly ISimilarity _similarity;
    private readonly IndexSearcherConfig _config;
    private readonly bool _useLmScoring;
    [ThreadStatic] private static PostingsEnum[]? t_postingsBuffer;
    [ThreadStatic] private static ScoreDoc[]? t_collectorHeapCache;
    [ThreadStatic] private static HashSet<(string Field, string Term)>? t_docFreqTermsBuf;
    private static readonly Dictionary<(string Field, string Term), int> EmptyGlobalDFs = new();
    private const string CombinedFieldsDocFreqKey = "\u0001combined-fields";
    private readonly QueryCache? _queryCache;
    private ConcurrentDictionary<MltCacheKey, (string Field, string Term, float Score)[]>? _mltCache;
    private int _mltCacheCount;
    private const int MltCacheSoftCap = 64;

    private readonly record struct MltCacheKey(
        int DocId, int MaxQueryTerms, int MinTermFreq, int MinDocFreq, int MinWordLength);

    /// <summary>Corpus-wide statistics computed at construction.</summary>
    public IndexStats Stats => _stats;

    /// <summary>The query result cache, or null if caching is disabled.</summary>
    public QueryCache? Cache => _queryCache;

    /// <summary>Computes scoring factors for a term, dispatching to LM or classic path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float F1, float F2, float F3) ComputeTermFactors(
        int docFreq, float avgDocLength, long collectionFreq, string field)
    {
        long totalTerms = _useLmScoring ? _stats.GetFieldLengthSum(field) : 0;
        return _similarity.PrecomputeLmFactors(_totalDocCount, docFreq, avgDocLength, collectionFreq, totalTerms);
    }

    /// <summary>Scores a term via the unified language-model path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ScoreTerm(float f1, float f2, float f3, int tf, int docLength)
        => _similarity.ScoreLmPrecomputed(f1, f2, f3, tf, docLength);

    /// <summary>Computes the collection frequency for a term across all segments.</summary>
    private long GetGlobalCollectionFreq(string qualifiedTerm)
    {
        long total = 0;
        foreach (var reader in _readers)
            total += reader.GetCollectionFrequency(qualifiedTerm);
        return total;
    }

    /// <summary>The metrics collector for this searcher.</summary>
    public Diagnostics.IMetricsCollector Metrics => _config.Metrics;

    /// <summary>Exposes the underlying segment readers for advanced use (e.g., spelling suggestions).</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal IReadOnlyList<SegmentReader> GetSegmentReaders() => _readers;

    /// <summary>Calculates the on-disk size of the index.</summary>
    public Diagnostics.IndexSizeReport GetIndexSize()
        => Diagnostics.IndexSizeCalculator.Calculate(_directory.DirectoryPath);

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> by loading the latest committed segments from the given directory.
    /// </summary>
    /// <param name="directory">The index directory to open.</param>
    /// <param name="similarity">The scoring model to use. Defaults to BM25 if null.</param>
    public IndexSearcher(MMapDirectory directory, ISimilarity? similarity = null)
        : this(directory, new IndexSearcherConfig { Similarity = similarity ?? Bm25Similarity.Instance })
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> by loading the latest committed segments from the given directory.
    /// </summary>
    /// <param name="directory">The index directory to open.</param>
    /// <param name="config">Searcher configuration including similarity model, parallelism, and caching options.</param>
    public IndexSearcher(MMapDirectory directory, IndexSearcherConfig config)
    {
        _directory = directory;
        _config = config;
        _similarity = config.Similarity;

        _useLmScoring = _similarity.RequiresCollectionStatistics;

        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);

        var (segmentIds, generation) = LoadLatestCommitWithGeneration();
        IndexOpenGuard.EnsureCanOpenSegments(directory, segmentIds, config.CompatibilityMode, forWriting: false);

        // Load segment readers with a retry loop to handle the narrow window where a
        // background merge has deleted segment files but the commit file still references
        // the old segments. Re-reading the commit picks up the merged generation.
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _readers.Clear();
                foreach (var segId in segmentIds)
                {
                    var segPath = Path.Combine(directory.DirectoryPath, segId + ".seg");
                    if (!File.Exists(segPath)) continue;
                    var info = SegmentInfo.ReadFrom(segPath);
                    _readers.Add(new SegmentReader(directory, info));
                }
                break;
            }
            catch (FileNotFoundException) when (attempt < maxAttempts)
            {
                Thread.Sleep(10 * attempt);
                (segmentIds, generation) = LoadLatestCommitWithGeneration();
                IndexOpenGuard.EnsureCanOpenSegments(directory, segmentIds, config.CompatibilityMode, forWriting: false);
            }
        }

        _docBases = AssignDocBases();
        _totalDocCount = _docBases.Length > 0
            ? _docBases[^1] + _readers[^1].MaxDoc
            : 0;

        // Try to load persisted stats first; fall back to expensive recomputation
        var statsPath = IndexStats.GetStatsPath(directory.DirectoryPath, generation);
        _stats = IndexStats.TryLoadFrom(statsPath) ?? ComputeStats();

        if (config.EnableQueryCache)
            _queryCache = config.SharedCache ?? new QueryCache(config.QueryCacheMaxEntries);
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over the given pre-built segment list (NRT or snapshot scenario).
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The explicit list of segment infos to search.</param>
    /// <param name="similarity">The scoring model to use. Defaults to BM25 if null.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentInfo> segments, ISimilarity? similarity = null)
        : this(directory, segments, new IndexSearcherConfig { Similarity = similarity ?? Bm25Similarity.Instance })
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over the given pre-built segment list with the specified configuration.
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The explicit list of segment infos to search.</param>
    /// <param name="config">Searcher configuration including similarity model, parallelism, and caching options.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentInfo> segments, IndexSearcherConfig config)
    {
        _directory = directory;
        _config = config;
        _similarity = config.Similarity;

        _useLmScoring = _similarity.RequiresCollectionStatistics;
        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);
        IndexOpenGuard.EnsureCanOpenSegments(
            directory,
            segments.Select(static segment => segment.SegmentId),
            config.CompatibilityMode,
            forWriting: false);
        foreach (var info in segments)
            _readers.Add(new SegmentReader(directory, info));

        _docBases = AssignDocBases();
        _totalDocCount = _docBases.Length > 0
            ? _docBases[^1] + _readers[^1].MaxDoc
            : 0;
        _stats = ComputeStats();

        if (config.EnableQueryCache)
            _queryCache = config.SharedCache ?? new QueryCache(config.QueryCacheMaxEntries);
    }

    private int[] AssignDocBases()
    {
        var bases = new int[_readers.Count];
        int docBase = 0;
        for (int i = 0; i < _readers.Count; i++)
        {
            bases[i] = docBase;
            _readers[i].DocBase = docBase;
            docBase += _readers[i].MaxDoc;
        }
        return bases;
    }

    /// <summary>
    /// Executes a query and returns the top-<paramref name="topN"/> scoring documents.
    /// Checks the query cache first, then falls back to the full search pipeline.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <returns>A <see cref="TopDocs"/> containing the top-scoring documents and total hit count.</returns>
    public TopDocs Search(Query query, int topN)
    {
        int requestedTopN = topN;
        int effectiveTopN = NormaliseTopN(topN);
        if (effectiveTopN <= 0 || _readers.Count == 0)
            return TopDocs.Empty;

        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Search);
        activity?.SetTag("query.type", query.GetType().Name);

        // Check query cache
        if (_queryCache is not null)
        {
            var cached = _queryCache.TryGet(query, requestedTopN);
            if (cached is not null)
            {
                _config.Metrics.RecordCacheHit();
                activity?.SetTag("search.cache_hit", true);
                activity?.SetTag("search.total_hits", cached.TotalHits);
                return cached;
            }
            _config.Metrics.RecordCacheMiss();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = SearchCore(query, effectiveTopN);
        sw.Stop();
        _config.Metrics.RecordSearchLatency(sw.Elapsed);

        activity?.SetTag("search.cache_hit", false);
        activity?.SetTag("search.total_hits", result.TotalHits);

        _config.SlowQueryLog?.MaybeLog(query, sw.Elapsed, result.TotalHits);
        _config.SearchAnalytics?.Record(query, sw.Elapsed, result.TotalHits, cacheHit: false);

        _queryCache?.Put(query, requestedTopN, result);
        return result;
    }

    private int NormaliseTopN(int topN)
        => _totalDocCount > 0 && topN > _totalDocCount ? _totalDocCount : topN;

    private TopDocs SearchCore(Query query, int topN)
    {
        // MoreLikeThis is a cross-segment query: extract terms, build BooleanQuery, delegate
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN);

        // RRF: execute each child query independently, then fuse by rank
        if (query is RrfQuery rrf)
            return ExecuteRrfQuery(rrf, topN);

        // Block join: execute child query, map results to parent docs
        if (query is BlockJoinQuery bjq)
            return ExecuteBlockJoinQuery(bjq, topN);

        // Fast path for the most common query type — avoids
        // PrecomputeGlobalDocFreqs allocation and does only 1 dictionary
        // lookup per segment instead of 2.
        if (query is TermQuery tq)
            return SearchTermQuery(tq, topN);

        // Fast path for FunctionScoreQuery wrapping a TermQuery: inline scoring,
        // reuse ThreadStatic postings buffer, skip PrecomputeGlobalDocFreqs.
        if (query is FunctionScoreQuery fsq && fsq.Inner is TermQuery fsqTq)
            return SearchFunctionScoreTermQuery(fsqTq, fsq, topN);

        // Fast path for BooleanQuery with all-TermQuery clauses — compute
        // global DFs inline without the generic PrecomputeGlobalDocFreqs tree walk
        if (query is BooleanQuery bq && IsAllTermQueryBoolean(bq))
            return SearchBooleanTermQueryFast(bq, topN);

        // Pattern-based queries (Prefix, Wildcard, Fuzzy) don't have static terms,
        // so PrecomputeGlobalDocFreqs produces an empty dictionary. Skip the tree walk.
        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        var collector = new TopNCollector(topN);

        if (_readers.Count == 1 || !_config.ParallelSearch)
        {
            foreach (var reader in _readers)
                ExecuteQuery(query, reader, globalDFs, ref collector);
        }
        else
        {
            int maxDop = _config.MaxConcurrency > 0 ? _config.MaxConcurrency : Environment.ProcessorCount;
            var lockObj = new Lock();
            Parallel.ForEach(_readers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, reader =>
            {
                var localCollector = new TopNCollector(topN);
                ExecuteQuery(query, reader, globalDFs, ref localCollector);
                var localDocs = localCollector.ToTopDocs();
                lock (lockObj)
                {
                    foreach (var sd in localDocs.ScoreDocs)
                        collector.Collect(sd.DocId, sd.Score);
                }
            });
        }

        return collector.ToTopDocs();
    }

    /// <summary>
    /// Executes a query and collects results through the provided <see cref="ICollector"/>.
    /// When <paramref name="collector"/> is a <see cref="CountCollector"/>, this uses a
    /// zero-allocation count-only path that avoids materialising a scored heap.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="collector">The collector that receives matching document IDs and scores.</param>
    public void Search(Query query, ICollector collector)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(collector);

        if (_readers.Count == 0) return;

        // CountCollector: use the zero-allocation count-only path.
        // CountCollector is a struct; when passed as ICollector it gets boxed.
        // Unsafe.Unbox gives us a ref to the struct inside the box so we can
        // update TotalHits without allocating.
        if (collector is CountCollector)
        {
            ref var unboxed = ref Unsafe.Unbox<CountCollector>(collector);
            unboxed.TotalHits = Count(query);
            return;
        }

        // Bridge through the scored path for custom collectors that need actual results.
        int cap = Math.Min(_totalDocCount, 1024);
        var wrapper = new TopNCollectorWrapper(cap);
        var result = Search(query, cap);
        foreach (var sd in result.ScoreDocs)
            collector.Collect(sd.DocId, sd.Score);
    }

    /// <summary>
    /// Returns the total number of documents matching the query without materialising
    /// a scored heap. Uses a zero-allocation count-only collector.
    /// </summary>
    /// <param name="query">The query to count matches for.</param>
    /// <returns>The total number of matching documents.</returns>
    public int Count(Query query)
    {
        if (query is MatchAllDocsQuery)
            return _stats.LiveDocCount;

        if (_readers.Count == 0)
            return 0;

        // MoreLikeThis is cross-segment — delegate to SearchCore.
        if (query is MoreLikeThisQuery mlt)
            return SearchCore(mlt, 1).TotalHits;

        // RRF: execute each child, return max.
        if (query is RrfQuery rrf)
            return SearchCore(rrf, 1).TotalHits;

        // Block join: delegate to SearchCore.
        if (query is BlockJoinQuery bjq)
            return SearchCore(bjq, 1).TotalHits;

        return CountCore(query);
    }

    /// <summary>
    /// Zero-allocation count path. Uses <see cref="TopNCollector"/> with maxSize=0
    /// so that Collect only increments a counter without heap maintenance.
    /// </summary>
    private int CountCore(Query query)
    {
        // Fast path: TermQuery — count from postings directly when possible.
        if (query is TermQuery tq)
        {
            int total = 0;
            foreach (var reader in _readers)
            {
                var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
                using var postings = reader.GetPostingsEnum(qt);
                if (postings.IsExhausted) continue;
                if (reader.HasDeletions)
                {
                    while (postings.MoveNext())
                        if (reader.IsLive(postings.DocId))
                            total++;
                }
                else
                {
                    total += postings.DocFreq;
                }
            }
            return total;
        }

        // General path: count-only collector (zero allocation).
        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        var collector = new TopNCollector(maxSize: 0);

        if (_readers.Count == 1 || !_config.ParallelSearch)
        {
            foreach (var reader in _readers)
                ExecuteQuery(query, reader, globalDFs, ref collector);
        }
        else
        {
            int maxDop = _config.MaxConcurrency > 0 ? _config.MaxConcurrency : Environment.ProcessorCount;
            int total = 0;
            var lockObj = new object();
            Parallel.ForEach(_readers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, reader =>
            {
                var localCollector = new TopNCollector(maxSize: 0);
                ExecuteQuery(query, reader, globalDFs, ref localCollector);
                lock (lockObj) { total += localCollector.TotalHits; }
            });
            return total;
        }

        return collector.TotalHits;
    }

    /// <summary>
    /// Parses a query string, applies analysis, and searches.
    /// </summary>
    public TopDocs Search(string queryString, string defaultField, int topN, IAnalyser? analyser = null)
    {
        analyser ??= new StandardAnalyser();
        var parser = new QueryParser(defaultField, analyser);
        var query = parser.Parse(queryString);
        return Search(query, topN);
    }

    /// <summary>
    /// Searches with cancellation support. Checks the token between segments and between
    /// inner sub-clauses, allowing long-running queries to be interrupted.
    /// </summary>
    public TopDocs Search(Query query, int topN, CancellationToken cancellationToken)
    {
        if (topN <= 0 || _readers.Count == 0)
            return TopDocs.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        // Cross-segment / composite queries: dispatch before the generic ExecuteQuery path
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN);
        if (query is RrfQuery rrf)
            return ExecuteRrfQuery(rrf, topN);
        if (query is BlockJoinQuery bjq)
            return ExecuteBlockJoinQuery(bjq, topN);
        if (query is TermQuery tq)
            return SearchTermQuery(tq, topN);
        if (query is FunctionScoreQuery fsqCt && fsqCt.Inner is TermQuery fsqCtTq)
            return SearchFunctionScoreTermQuery(fsqCtTq, fsqCt, topN);
        // Fast path for BooleanQuery with all-TermQuery clauses
        if (query is BooleanQuery bq && IsAllTermQueryBoolean(bq))
            return SearchBooleanTermQueryFast(bq, topN);

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        var collector = new TopNCollector(topN);

        foreach (var reader in _readers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteQuery(query, reader, globalDFs, ref collector);
        }

        return collector.ToTopDocs();
    }

    /// <summary>
    /// Parses a query string and searches with cancellation support.
    /// </summary>
    public TopDocs Search(string queryString, string defaultField, int topN,
        IAnalyser? analyser, CancellationToken cancellationToken)
    {
        analyser ??= new StandardAnalyser();
        var parser = new QueryParser(defaultField, analyser);
        var query = parser.Parse(queryString);
        return Search(query, topN, cancellationToken);
    }

    /// <summary>
    /// Executes a query under the supplied <see cref="SearchOptions"/>.
    /// The top-N heap must fit within the configured result-byte budget.
    /// The deadline and cancellation token are checked at segment boundaries; on
    /// early termination the returned <see cref="TopDocs"/> has <see cref="TopDocs.IsPartial"/> set to true.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="topN">The maximum number of results to return.</param>
    /// <param name="options">Per-query resource controls.</param>
    public TopDocs Search(Query query, int topN, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (topN <= 0 || _readers.Count == 0)
            return TopDocs.Empty;

        long topNBytes = checked((long)topN * Scoring.ScoreDoc.EstimatedBytes);
        if (topNBytes > options.MaxResultBytes)
            throw new ArgumentException(
                $"MaxResultBytes ({options.MaxResultBytes}) is smaller than the requested top-N heap ({topNBytes} bytes).",
                nameof(options));

        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Search);
        activity?.SetTag("query.type", query.GetType().Name);
        if (options.Timeout.HasValue)
            activity?.SetTag("search.timeout_ms", (long)options.Timeout.Value.TotalMilliseconds);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? sw.ElapsedTicks + (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;

        var result = SearchCoreBudgeted(query, topN, options, deadlineTicks, sw);

        _config.SlowQueryLog?.MaybeLog(query, sw.Elapsed, result.TotalHits);
        _config.SearchAnalytics?.Record(query, sw.Elapsed, result.TotalHits, cacheHit: false);

        return result;
    }


    private TopDocs SearchCoreBudgeted(Query query, int topN, SearchOptions options,
        long? deadlineTicks, System.Diagnostics.Stopwatch sw)
    {
        // Cross-segment / composite queries: dispatch before the generic ExecuteQuery path
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN);
        if (query is RrfQuery rrf)
            return ExecuteRrfQuery(rrf, topN);
        if (query is BlockJoinQuery bjq)
            return ExecuteBlockJoinQuery(bjq, topN);

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        var collector = new TopNCollector(topN);
        bool partial = false;
        long topNBytes = (long)topN * Scoring.ScoreDoc.EstimatedBytes;

        foreach (var reader in _readers)
        {
            if (options.CancellationToken.IsCancellationRequested)
            {
                partial = true;
                break;
            }
            if (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value)
            {
                partial = true;
                break;
            }
            ExecuteQuery(query, reader, globalDFs, ref collector);
        }

        var docs = collector.ToTopDocs();
        return partial ? docs.AsPartial() : docs;
    }

    /// <summary>
    /// Executes a query and yields matches in segment order, segment by segment.
    /// Honours <see cref="SearchOptions.Timeout"/>, <see cref="SearchOptions.MaxResultBytes"/>,
    /// and <see cref="SearchOptions.CancellationToken"/> between segments. Results are
    /// not globally sorted by score: the caller receives each segment's local top-N,
    /// in segment order, with global doc IDs.
    /// </summary>
    /// <remarks>
    /// Use this when the caller wants to process results as they arrive, or when the
    /// total candidate count is too large to retain a global heap. The per-segment
    /// top-N matches the supplied <paramref name="perSegmentTopN"/>; pass
    /// <see cref="int.MaxValue"/> to retain every match per segment.
    /// </remarks>
    public IEnumerable<ScoreDoc> SearchStreaming(Query query, int perSegmentTopN = 1024,
        SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (perSegmentTopN <= 0 || _readers.Count == 0)
            yield break;

        options ??= SearchOptions.Default;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        long perSegmentBytes = (long)perSegmentTopN * Scoring.ScoreDoc.EstimatedBytes;
        long emittedBytes = 0;

        foreach (var reader in _readers)
        {
            if (options.CancellationToken.IsCancellationRequested) yield break;
            if (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value) yield break;
            if (emittedBytes + perSegmentBytes > options.MaxResultBytes) yield break;

            var segmentCollector = new TopNCollector(perSegmentTopN);
            ExecuteQuery(query, reader, globalDFs, ref segmentCollector);
            var segmentDocs = segmentCollector.ToTopDocs();
            emittedBytes += (long)segmentDocs.ScoreDocs.Length * Scoring.ScoreDoc.EstimatedBytes;
            foreach (var sd in segmentDocs.ScoreDocs)
                yield return sd;
        }
    }

    /// <summary>
    /// Executes a query and returns results as an <see cref="IEnumerable{ScoreDoc}"/>.
    /// When <see cref="SearchOptions.StreamResults"/> is true, results are yielded
    /// per-segment without building a global top-N heap. When false, results are
    /// materialised from a global top-N search and then yielded.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="options">Per-query resource controls. <see cref="SearchOptions.StreamResults"/>
    /// determines whether results are streamed or materialised.</param>
    public IEnumerable<ScoreDoc> Search(Query query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (_readers.Count == 0)
            yield break;

        if (options.StreamResults)
        {
            int perSegment = DerivePerSegmentTopN(options);
            foreach (var sd in SearchStreaming(query, perSegment, options))
                yield return sd;
            yield break;
        }

        // Non-streaming: materialise a global top-N then yield results.
        int topN = DerivePerSegmentTopN(options);
        var result = Search(query, topN, options);
        foreach (var sd in result.ScoreDocs)
            yield return sd;
    }

    /// <summary>
    /// Executes a query asynchronously and yields matches in segment order.
    /// Honours <see cref="SearchOptions.Timeout"/>, <see cref="SearchOptions.MaxResultBytes"/>,
    /// and both <paramref name="cancellationToken"/> and <see cref="SearchOptions.CancellationToken"/>.
    /// Results are not globally sorted by score.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="options">Per-query resource controls.</param>
    /// <param name="cancellationToken">A cancellation token combined with
    /// <see cref="SearchOptions.CancellationToken"/>.</param>
    public async IAsyncEnumerable<ScoreDoc> SearchAsync(
        Query query,
        SearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        if (_readers.Count == 0)
            yield break;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            options.CancellationToken, cancellationToken);
        var ct = linkedCts.Token;

        int perSegment = DerivePerSegmentTopN(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        long perSegmentBytes = (long)perSegment * Scoring.ScoreDoc.EstimatedBytes;
        long emittedBytes = 0;

        foreach (var reader in _readers)
        {
            ct.ThrowIfCancellationRequested();
            if (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value)
                yield break;
            if (emittedBytes + perSegmentBytes > options.MaxResultBytes)
                yield break;

            var segmentCollector = new TopNCollector(perSegment);
            ExecuteQuery(query, reader, globalDFs, ref segmentCollector);
            var segmentDocs = segmentCollector.ToTopDocs();
            emittedBytes += (long)segmentDocs.ScoreDocs.Length * Scoring.ScoreDoc.EstimatedBytes;

            foreach (var sd in segmentDocs.ScoreDocs)
                yield return sd;

            // Yield control back to the caller between segments.
            await Task.Yield();
        }
    }

    private static int DerivePerSegmentTopN(SearchOptions options)
    {
        if (options.MaxResultBytes == long.MaxValue)
            return 256;
        return Math.Max(1, (int)Math.Min(256, options.MaxResultBytes / Scoring.ScoreDoc.EstimatedBytes));
    }

    private IndexStats ComputeStats()
    {
        if (_readers.Count == 0)
            return IndexStats.Empty;

        int liveDocCount = 0;
        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var reader in _readers)
        {
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                if (!reader.IsLive(docId)) continue;
                liveDocCount++;

                // Accumulate per-field lengths
                foreach (var field in reader.Info.FieldNames)
                {
                    int fieldLen = reader.GetFieldLength(docId, field);
                    fieldLengthSums[field] = fieldLengthSums.GetValueOrDefault(field) + fieldLen;
                    fieldDocCounts[field] = fieldDocCounts.GetValueOrDefault(field) + 1;
                }
            }
        }

        var avgFieldLengths = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (field, sum) in fieldLengthSums)
        {
            int count = fieldDocCounts.GetValueOrDefault(field, 1);
            avgFieldLengths[field] = count > 0 ? (float)sum / count : 1.0f;
        }

        return new IndexStats(_totalDocCount, liveDocCount, avgFieldLengths, fieldDocCounts, fieldLengthSums);
    }

    private static bool ShouldSkipGlobalDocFreqs(Query query) =>
        query is PrefixQuery or WildcardQuery or FuzzyQuery
            or MatchAllDocsQuery or MatchNoDocsQuery
            or FieldExistsQuery or TermInSetQuery or PointInSetQuery
            or MultiPhraseQuery or IntervalsQuery or CombinedFieldsQuery;

    private Dictionary<(string Field, string Term), int> PrecomputeGlobalDocFreqsForSearch(Query query)
    {
        if (query is CombinedFieldsQuery combined)
            return PrecomputeCombinedFieldUnionDocFreqs(combined);

        return ShouldSkipGlobalDocFreqs(query)
            ? EmptyGlobalDFs
            : PrecomputeGlobalDocFreqs(query);
    }

    private (List<string> SegmentIds, int Generation) LoadLatestCommitWithGeneration()
    {
        var recovery = IndexRecovery.RecoverLatestCommit(_directory.DirectoryPath, cleanupOrphans: false);
        return recovery is not null
            ? (recovery.SegmentIds, recovery.Generation)
            : ([], 0);
    }

    private List<string> LoadLatestCommit()
    {
        var (ids, _) = LoadLatestCommitWithGeneration();
        return ids;
    }

    /// <summary>Disposes all underlying segment readers.</summary>
    public void Dispose()
    {
        foreach (var reader in _readers)
            reader.Dispose();
    }

    private TopDocs ExecuteRrfQuery(RrfQuery rrf, int topN)
    {
        if (rrf.Queries.Count == 0) return TopDocs.Empty;

        // Execute each child query independently to get ranked result lists
        var childResults = new TopDocs[rrf.Queries.Count];
        for (int i = 0; i < rrf.Queries.Count; i++)
            childResults[i] = SearchCore(rrf.Queries[i], topN);

        return RrfQuery.Combine(childResults, topN, rrf.K);
    }

    private TopDocs ExecuteBlockJoinQuery(BlockJoinQuery bjq, int topN)
    {
        var collector = new TopNCollector(topN);
        float boost = bjq.Boost;

        for (int r = 0; r < _readers.Count; r++)
        {
            var reader = _readers[r];
            int docBase = _docBases[r];
            var pbs = reader.GetParentBitSet();
            if (pbs is null) continue;

            // Block join children are contiguous before their parent, so parents
            // are encountered in non-decreasing docId order. A simple lastParent
            // check replaces the BitArray(MaxDoc) dedup at zero allocation cost.
            int lastParent = -1;

            if (bjq.ChildQuery is TermQuery tq)
            {
                // Fast path: stream PostingsEnum directly
                var qt = string.Concat(tq.Field, "\x00", tq.Term);
                using var pe = reader.GetPostingsEnum(qt);
                while (pe.MoveNext())
                {
                    if (pbs.IsParent(pe.DocId)) continue;
                    int parentLocal = pbs.NextParent(pe.DocId + 1);
                    if (parentLocal >= 0 && parentLocal != lastParent)
                    {
                        lastParent = parentLocal;
                        collector.Collect(docBase + parentLocal, boost);
                    }
                }
            }
            else
            {
                // General path: collect matching child doc IDs into a lightweight
                // BitArray instead of materialising a TopNCollector(MaxDoc).
                var childBits = new System.Collections.BitArray(reader.MaxDoc);
                CollectChildDocsIntoBitArray(bjq.ChildQuery, reader, childBits);

                for (int docId = 0; docId < reader.MaxDoc; docId++)
                {
                    if (!childBits[docId]) continue;
                    if (pbs.IsParent(docId)) continue;
                    int parentLocal = pbs.NextParent(docId + 1);
                    if (parentLocal >= 0 && parentLocal != lastParent)
                    {
                        lastParent = parentLocal;
                        collector.Collect(docBase + parentLocal, boost);
                    }
                }
            }
        }

        return collector.ToTopDocs();
    }

    /// <summary>Collects matching doc IDs for a child query into a BitArray (doc-ID-only, no scores).</summary>
    private void CollectChildDocsIntoBitArray(Query query, SegmentReader reader,
        System.Collections.BitArray bits)
    {
        switch (query)
        {
            case TermQuery tq:
                {
                    var qt = string.Concat(tq.Field, "\x00", tq.Term);
                    using var pe = reader.GetPostingsEnum(qt);
                    while (pe.MoveNext())
                        bits[pe.DocId] = true;
                    break;
                }
            case BooleanQuery bq:
                {
                    System.Collections.BitArray? mustResult = null;
                    System.Collections.BitArray? scratch = null;

                    foreach (var clause in bq.Clauses)
                    {
                        if (clause.Occur == Occur.MustNot) continue;

                        if (clause.Occur == Occur.Must && mustResult is null)
                        {
                            // First MUST clause owns its BitArray for the AND chain
                            mustResult = new System.Collections.BitArray(bits.Length);
                            CollectChildDocsIntoBitArray(clause.Query, reader, mustResult);
                        }
                        else
                        {
                            // Reuse scratch for SHOULD and subsequent MUST clauses
                            scratch ??= new System.Collections.BitArray(bits.Length);
                            scratch.SetAll(false);
                            CollectChildDocsIntoBitArray(clause.Query, reader, scratch);

                            if (clause.Occur == Occur.Must)
                                mustResult!.And(scratch);
                            else // Should
                                bits.Or(scratch);
                        }
                    }
                    if (mustResult is not null) bits.Or(mustResult);

                    foreach (var clause in bq.Clauses)
                    {
                        if (clause.Occur != Occur.MustNot) continue;
                        scratch ??= new System.Collections.BitArray(bits.Length);
                        scratch.SetAll(false);
                        CollectChildDocsIntoBitArray(clause.Query, reader, scratch);
                        bits.And(scratch.Not());
                    }
                    break;
                }
            default:
                {
                    // Fallback for complex child queries: full query execution
                    var globalDFs = PrecomputeGlobalDocFreqs(query);
                    var segCollector = new TopNCollector(reader.MaxDoc);
                    ExecuteQuery(query, reader, globalDFs, ref segCollector);
                    int docBase = reader.DocBase;
                    var childDocs = segCollector.ToTopDocs();
                    foreach (var sd in childDocs.ScoreDocs)
                        bits[sd.DocId - docBase] = true;
                    break;
                }
        }
    }

    // Reusable buffer for PrecomputeGlobalDocFreqs to avoid per-call HashSet allocation
    private Dictionary<(string Field, string Term), int> PrecomputeGlobalDocFreqs(Query query)
    {
        var buf = t_docFreqTermsBuf ??= new HashSet<(string, string)>();
        buf.Clear();
        CollectTerms(query, buf);

        var result = new Dictionary<(string Field, string Term), int>(buf.Count);
        foreach (var (field, term) in buf)
        {
            var qt = string.Concat(field, "\x00", term);
            int total = 0;
            foreach (var reader in _readers)
                total += reader.GetDocFreqByQualified(qt);
            result[(field, term)] = total;
        }

        return result;
    }

    private Dictionary<(string Field, string Term), int> PrecomputeCombinedFieldUnionDocFreqs(CombinedFieldsQuery query)
    {
        var result = new Dictionary<(string Field, string Term), int>(query.Terms.Count);
        foreach (var term in query.Terms)
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

            result[(CombinedFieldsDocFreqKey, term)] = total;
        }

        return result;
    }

    private static void CollectTerms(Query query, HashSet<(string Field, string Term)> terms)
    {
        switch (query)
        {
            case TermQuery tq:
                terms.Add((tq.Field, tq.Term));
                break;
            case BooleanQuery bq:
                foreach (var clause in bq.Clauses)
                    CollectTerms(clause.Query, terms);
                break;
            case PhraseQuery pq:
                foreach (var term in pq.Terms)
                    terms.Add((pq.Field, term));
                break;
            case ConstantScoreQuery csq:
                CollectTerms(csq.Inner, terms);
                break;
            case DisjunctionMaxQuery dmq:
                foreach (var d in dmq.Disjuncts)
                    CollectTerms(d, terms);
                break;
                // Expansion queries (prefix/wildcard/fuzzy/range/regexp) resolve terms at execution
                // time per-segment, so no static term collection is needed here.
        }
    }

    private TopDocs SearchTermQuery(TermQuery query, int topN, ISideCollector? sideCollector = null)
    {
        var qt = query.CachedQualifiedTerm ??= string.Concat(query.Field, "\x00", query.Term);
        int readerCount = _readers.Count;

        // Reuse a pre-allocated array to avoid per-query allocation
        if (t_postingsBuffer is null || t_postingsBuffer.Length < readerCount)
            t_postingsBuffer = new PostingsEnum[readerCount];
        var postingsArr = t_postingsBuffer;
        int globalDF = 0;
        for (int i = 0; i < readerCount; i++)
        {
            postingsArr[i] = _readers[i].GetPostingsEnum(qt);
            globalDF += postingsArr[i].DocFreq;
        }
        long globalCollectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;

        if (globalDF == 0)
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
            return TopDocs.Empty;
        }

        // Phase 2: score using already-decoded postings
        // Phase 2: score using already-decoded postings
        float avgDocLength = _stats.GetAvgFieldLength(query.Field);
        var (f1, f2, f3) = ComputeTermFactors(globalDF, avgDocLength, globalCollectionFreq, query.Field);
        float boost = query.Boost;

        // Reuse the backing ScoreDoc[] across queries to avoid per-query allocation
        if (t_collectorHeapCache is null || t_collectorHeapCache.Length < topN)
            t_collectorHeapCache = new ScoreDoc[topN];
        var collector = new TopNCollector(t_collectorHeapCache, topN);

        try
        {
            for (int i = 0; i < readerCount; i++)
            {
                ref var postings = ref postingsArr[i];
                if (postings.IsExhausted) continue;

                var reader = _readers[i];
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                reader.TryGetFieldLengths(query.Field, out var fieldLengths);

                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if (hasDeletions && !reader.IsLive(docId)) continue;

                    int tf = postings.Freq;
                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, tf, docLength);
                    if (boost != 1.0f) score *= boost;
                    score = ApplyFieldBoost(reader, docId, query.Field, score);
                    collector.Collect(docBase + docId, score);
                    sideCollector?.Collect(docBase + docId, score, reader, docId);
                }
            }
        }
        finally
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
        }

        return collector.ToTopDocs();
    }

    /// <summary>Fast path for FunctionScoreQuery wrapping a TermQuery.
    /// Same per-segment postings scan and BM25 scoring as SearchTermQuery,
    /// but combines the numeric field value inline after field boost and
    /// before the top-N collector. Skipping the generic SearchCore dispatch
    /// avoids PrecomputeGlobalDocFreqs, per-segment ExecuteQuery, and
    /// Parallel.ForEach delegate allocations.</summary>
    private TopDocs SearchFunctionScoreTermQuery(TermQuery tq, FunctionScoreQuery fsq, int topN)
    {
        // Open postings for the inner term across all segments.
        var qt = tq.CachedQualifiedTerm ??= string.Concat(tq.Field, "\x00", tq.Term);
        int readerCount = _readers.Count;

        if (t_postingsBuffer is null || t_postingsBuffer.Length < readerCount)
            t_postingsBuffer = new PostingsEnum[readerCount];
        var postingsArr = t_postingsBuffer;
        int globalDF = 0;
        for (int i = 0; i < readerCount; i++)
        {
            postingsArr[i] = _readers[i].GetPostingsEnum(qt);
            globalDF += postingsArr[i].DocFreq;
        }
        long globalCollectionFreq = _useLmScoring ? GetGlobalCollectionFreq(qt) : 0;

        if (globalDF == 0)
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
            return TopDocs.Empty;
        }

        // Compute BM25 factors once from the global doc frequency.
        float avgDocLength = _stats.GetAvgFieldLength(tq.Field);
        var (f1, f2, f3) = ComputeTermFactors(globalDF, avgDocLength, globalCollectionFreq, tq.Field);
        float boost = tq.Boost;

        // Reuse the ThreadStatic collector heap.
        if (t_collectorHeapCache is null || t_collectorHeapCache.Length < topN)
            t_collectorHeapCache = new ScoreDoc[topN];
        var collector = new TopNCollector(t_collectorHeapCache, topN);

        try
        {
            for (int i = 0; i < readerCount; i++)
            {
                ref var postings = ref postingsArr[i];
                if (postings.IsExhausted) continue;

                var reader = _readers[i];
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                reader.TryGetFieldLengths(tq.Field, out var fieldLengths);

                // Single pass: BM25, field boost, function score, then top-N collect.
                while (postings.MoveNext())
                {
                    int docId = postings.DocId;
                    if (hasDeletions && !reader.IsLive(docId)) continue;

                    int tf = postings.Freq;
                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Length
                        ? fieldLengths[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, tf, docLength);
                    if (boost != 1.0f) score *= boost;
                    score = ApplyFieldBoost(reader, docId, tq.Field, score);

                    // Modify the field-boosted BM25 score with the numeric doc value.
                    if (reader.TryGetNumericValue(fsq.NumericField, docId, out double fieldValue))
                        score = FunctionScoreQuery.Combine(score, fieldValue, fsq.Mode);

                    collector.Collect(docBase + docId, score * fsq.Boost);
                }
            }
        }
        finally
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
        }

        return collector.ToTopDocs();
    }
}
