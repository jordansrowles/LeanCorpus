using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.StoredFields;
using System.Runtime.CompilerServices;
using System.Threading;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Segment;
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
    private readonly BoundedLruCache<string, SegmentReaderState> _segmentReaderCache;
    private FileSnapshotLease? _snapshotLease;
    private readonly int[] _docBases;
    private readonly int _totalDocCount;
    private IndexStats? _stats;
    private readonly Lock _statsLock = new();
    private readonly AsyncLocal<FuzzyQueryExecutionContext?> _fuzzyQueryExecutionContext = new();
    private readonly ISimilarity _similarity;
    private readonly IndexSearcherConfig _config;
    private readonly bool _useLmScoring;
    private readonly bool _useBm25Scoring;
    [ThreadStatic] private static PostingsEnum[]? t_postingsBuffer;
    [ThreadStatic] private static ScoreDoc[]? t_collectorHeapCache;
    [ThreadStatic] private static HashSet<(string Field, string Term)>? t_docFreqTermsBuf;
    [ThreadStatic] private static IndexSearcher? t_similaritySearcher;
    [ThreadStatic] private static string? t_similarityField;
    [ThreadStatic] private static ISimilarity? t_fieldSimilarity;
    private static readonly Dictionary<(string Field, string Term), int> EmptyGlobalDFs = new();
    private const string CombinedFieldsDocFreqKey = "\u0001combined-fields";
    private readonly QueryCache? _queryCache;
    private int _commitGeneration;
    private const int CollectionFrequencyCacheMaximumEntries = 1024;
    private const int CollectionFrequencyCacheMaximumTermLength = 512;
    private readonly BoundedGenerationCache<string, long> _collectionFrequencyCache = new(CollectionFrequencyCacheMaximumEntries);
    private ConcurrentDictionary<MltCacheKey, (string Field, string Term, float Score)[]>? _mltCache;
    private int _mltCacheCount;
    private const int MltCacheSoftCap = 64;
    private const int MltDocFreqCacheMaximumEntries = 1024;
    private const int MltDocFreqCacheMaximumTermLength = 512;
    private readonly BoundedGenerationCache<string, int> _mltDocFreqCache = new(MltDocFreqCacheMaximumEntries);

    private FuzzyQueryExecutionContext? CurrentFuzzyQueryExecutionContext
        => _fuzzyQueryExecutionContext.Value;

    private MultiTermExpansionContext? CurrentMultiTermExpansionContext
        => _fuzzyQueryExecutionContext.Value?.Expansions;

    /// <summary>Starts one search's shared fuzzy plans and multi-term expansions; disposal restores the outer search's.</summary>
    private IDisposable PushFuzzyQueryExecutionContext()
        => EnterFuzzyQueryExecutionContext(CreateFuzzyQueryExecutionContext());

    private FuzzyQueryExecutionContext CreateFuzzyQueryExecutionContext()
        => new(_readers, _config.UseFuzzyExpansionCache, _seededVectorPlans.Value);

    /// <summary>
    /// Makes <paramref name="context"/> current until disposal. An asynchronous iterator re-enters
    /// the same context on each step, because an AsyncLocal value set in one step does not survive
    /// into the next.
    /// </summary>
    private IDisposable EnterFuzzyQueryExecutionContext(FuzzyQueryExecutionContext context)
    {
        var previous = _fuzzyQueryExecutionContext.Value;
        _fuzzyQueryExecutionContext.Value = context;
        return new FuzzyQueryExecutionContextScope(this, previous);
    }

    private sealed class FuzzyQueryExecutionContextScope(
        IndexSearcher searcher,
        FuzzyQueryExecutionContext? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                searcher._fuzzyQueryExecutionContext.Value = previous;
            }
        }
    }

    /// <summary>Returns this search's recorded expansion for <paramref name="query"/> in <paramref name="reader"/>, if any.</summary>
    private bool TryGetSharedExpansion(Query query, SegmentReader reader, out IReadOnlyList<(string Term, long Offset)> terms)
    {
        if (CurrentMultiTermExpansionContext is { } context)
            return context.TryGet(query, reader, out terms);
        terms = [];
        return false;
    }

    private int ResolvedSearchConcurrency => _config.MaxConcurrency > 0
        ? _config.MaxConcurrency
        : Math.Max(1, Environment.ProcessorCount);

    private bool CanSearchSegmentsInParallel()
        => !OperatingSystem.IsBrowser() && _readers.Count > 1 && _config.ParallelSearch && ResolvedSearchConcurrency > 1;

    /// <summary>
    /// Identifies one MoreLikeThis term extraction: the source document, its fields in order, every
    /// frequency limit and the word length. <c>BoostByScore</c> applies after lookup and is not part
    /// of it. The key is per searcher, so entries never cross generations.
    /// </summary>
    private readonly record struct MltCacheKey(
        int DocId, string Fields, int MaxQueryTerms, int MinTermFreq, int MinDocFreq, int MaxDocFreq, int MinWordLength);

    private static IndexSearcherConfig CreateConfig(ISimilarity? similarity)
    {
        var config = new IndexSearcherConfig();
        if (similarity is not null)
            config.Similarity = similarity;
        return config;
    }

    /// <summary>Corpus-wide statistics computed at construction.</summary>
    public IndexStats Stats
    {
        get
        {
            if (_stats is { } stats)
                return stats;
            lock (_statsLock)
                return _stats ??= ComputeStats();
        }
    }

    /// <summary>The query result cache, or null if caching is disabled.</summary>
    public QueryCache? Cache => _queryCache;

    /// <summary>Gets entry and eviction metrics for this searcher's collection-frequency cache.</summary>
    public Diagnostics.CacheMetricsSnapshot CollectionFrequencyCacheMetrics => _collectionFrequencyCache.Metrics;

    /// <summary>Gets retained-resource estimates for this searcher's heavy segment-reader cache.</summary>
    public Diagnostics.SegmentReaderCacheMetricsSnapshot SegmentReaderCacheMetrics
    {
        get
        {
            SegmentReaderCacheResourceMetrics metrics = _segmentReaderCache.Metrics;
            SegmentReaderCacheResourceUsage resources = metrics.Resources;
            return new Diagnostics.SegmentReaderCacheMetricsSnapshot
            {
                EntryCount = metrics.EntryCount,
                EvictionCount = metrics.EvictionCount,
                RetainedBytes = metrics.RetainedBytes,
                MaximumRetainedBytes = metrics.MaximumRetainedBytes,
                PostingsAndTermsBytes = resources.PostingsAndTermsBytes,
                NormsAndLengthsBytes = resources.NormsAndLengthsBytes,
                StoredFieldsBytes = resources.StoredFieldsBytes,
                TermVectorsBytes = resources.TermVectorsBytes,
                DocValuesBytes = resources.DocValuesBytes,
                NumericIndexesBytes = resources.NumericIndexesBytes,
                SpatialIndexesBytes = resources.SpatialIndexesBytes,
                VectorsBytes = resources.VectorsBytes,
                LiveDocsAndParentsBytes = resources.LiveDocsAndParentsBytes,
                OtherBytes = resources.OtherBytes,
                MappedVectorBytes = resources.MappedVectorBytes,
            };
        }
    }

    /// <summary>The committed generation represented by this immutable searcher snapshot.</summary>
    public int CommitGeneration => _commitGeneration;

    internal long SnapshotRetainedBytes => _snapshotLease?.RetainedBytes ?? 0;
    internal string[] SnapshotRetainedFiles => _snapshotLease?.RetainedFiles ?? [];
    internal string[] SnapshotPendingDeletionFiles => _snapshotLease?.PendingDeletionFiles ?? [];

    /// <summary>Computes scoring factors for a term, dispatching to LM or classic path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float F1, float F2, float F3) ComputeTermFactors(
        int docFreq, float avgDocLength, long collectionFreq, string field)
    {
        if (_config.PerFieldSimilarities is null)
        {
            long globalTotalTerms = _useLmScoring ? Stats.GetFieldLengthSum(field) : 0;
            return _similarity.PrecomputeLmFactors(
                _totalDocCount,
                docFreq,
                avgDocLength,
                collectionFreq,
                globalTotalTerms);
        }

        var similarity = GetSimilarity(field);
        long totalTerms = similarity.RequiresCollectionStatistics
            ? Stats.GetFieldLengthSum(field)
            : 0;
        return similarity.PrecomputeLmFactors(
            _totalDocCount, docFreq, avgDocLength, collectionFreq, totalTerms);
    }

    /// <summary>Scores a term via the unified language-model path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ScoreTerm(
        float f1,
        float f2,
        float f3,
        float tf,
        int docLength,
        string field)
    {
        if (_config.PerFieldSimilarities is null)
        {
            return _useBm25Scoring
                ? Bm25Scorer.ScorePrecomputed(f1, f2, tf, docLength)
                : _similarity.ScoreLmPrecomputed(f1, f2, f3, tf, docLength);
        }

        var similarity = GetSimilarity(field);
        return similarity is Bm25Similarity
            ? Bm25Scorer.ScorePrecomputed(f1, f2, tf, docLength)
            : similarity.ScoreLmPrecomputed(f1, f2, f3, tf, docLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ISimilarity GetSimilarity(string field)
    {
        if (ReferenceEquals(t_similaritySearcher, this)
            && string.Equals(t_similarityField, field, StringComparison.Ordinal))
        {
            return t_fieldSimilarity!;
        }

        var similarity = _config.PerFieldSimilarities is not null
            && _config.PerFieldSimilarities.TryGetValue(field, out var fieldSimilarity)
                ? fieldSimilarity
                : _similarity;
        t_similaritySearcher = this;
        t_similarityField = field;
        t_fieldSimilarity = similarity;
        return similarity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RequiresCollectionStatistics(string field)
        => _config.PerFieldSimilarities is null
            ? _useLmScoring
            : GetSimilarity(field).RequiresCollectionStatistics;

    /// <summary>Computes the collection frequency for a term across all segments.</summary>
    private long GetGlobalCollectionFreq(string qualifiedTerm)
        => qualifiedTerm.Length > CollectionFrequencyCacheMaximumTermLength
            ? ComputeGlobalCollectionFrequency(qualifiedTerm)
            : _collectionFrequencyCache.GetOrAdd(qualifiedTerm, this, static (term, searcher) =>
                searcher.ComputeGlobalCollectionFrequency(term));

    private long ComputeGlobalCollectionFrequency(string qualifiedTerm)
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

    /// <summary>
    /// Builds an immutable global ordinal map for a sorted DocValues field across this
    /// searcher's segment snapshots.
    /// </summary>
    /// <param name="fieldName">The sorted or sorted-set DocValues field.</param>
    /// <param name="sortedSet">When true, reads the sorted-set term dictionary.</param>
    /// <returns>A map whose source indexes match the current segment order.</returns>
    public OrdinalMap GetOrdinalMap(string fieldName, bool sortedSet = false)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        var sourceTerms = new IReadOnlyList<string>[_readers.Count];
        for (int i = 0; i < _readers.Count; i++)
        {
            sourceTerms[i] = sortedSet
                ? _readers[i].GetSortedSetDocValueTermsView(fieldName) ?? Array.Empty<string>()
                : _readers[i].GetSortedDocValueTermsView(fieldName) ?? Array.Empty<string>();
        }
        return OrdinalMap.Build(sourceTerms);
    }

    internal int CachedSegmentReaderCount => _segmentReaderCache.Count;

    internal long LoadedSegmentReaderCount => _segmentReaderCache.LoadCount;

    /// <summary>Calculates the on-disk size of the index.</summary>
    public Diagnostics.IndexSizeReport GetIndexSize()
        => Diagnostics.IndexSizeCalculator.Calculate(_directory.DirectoryPath);

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> by loading the latest committed segments from the given directory.
    /// </summary>
    /// <param name="directory">The index directory to open.</param>
    /// <param name="similarity">The scoring model to use. Defaults to BM25 if null.</param>
    public IndexSearcher(MMapDirectory directory, ISimilarity? similarity = null)
        : this(directory, CreateConfig(similarity))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> by loading the latest committed segments from the given directory.
    /// </summary>
    /// <param name="directory">The index directory to open.</param>
    /// <param name="config">Searcher configuration including similarity model, parallelism, and caching options.</param>
    public IndexSearcher(MMapDirectory directory, IndexSearcherConfig config)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        CompressionCodecRegistry.MarkIndexOpened();
        _directory = directory;
        _config = config;
        _segmentReaderCache = new BoundedLruCache<string, SegmentReaderState>(
            config.MaxCachedSegmentReaders,
            StringComparer.Ordinal,
            maxRetainedBytes: config.MaxCachedSegmentReaderBytes,
            resourceUsageSelector: static state => state.GetRetainedResourceUsage());
        _similarity = config.Similarity;
        _useLmScoring = _similarity.RequiresCollectionStatistics;
        _useBm25Scoring = _similarity is Bm25Similarity;

        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);

        var recovery = LoadLatestCommitWithGeneration();
        var segmentIds = recovery?.SegmentIds ?? [];
        var segmentInfos = recovery?.SegmentInfos ?? [];
        int generation = recovery?.Generation ?? 0;
        _commitGeneration = generation;
        IndexOpenGuard.EnsureCanOpenSegments(directory, segmentIds, config.CompatibilityMode, forWriting: false, config.CodecCatalog);

        // Load segment readers with a retry loop to handle the narrow window where a
        // background merge has deleted segment files but the commit file still references
        // the old segments. Re-reading the commit picks up the merged generation.
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            FileSnapshotLease? attemptSnapshot = null;
            try
            {
                _readers.Clear();
                var selectedGenerations = SelectedGenerations(
                    segmentInfos.Select(static info => (info.SegmentId, info.DelGeneration)));
                attemptSnapshot = directory.AcquireSnapshot(
                    name => SegmentFileSet.IsSnapshotFile(name, selectedGenerations, config.CodecCatalog), out _);
                foreach (var info in segmentInfos)
                    _readers.Add(new SegmentReader(
                        directory, info, _segmentReaderCache, config.CodecCatalog));
                _snapshotLease = attemptSnapshot;
                attemptSnapshot = null;
                break;
            }
            catch (FileNotFoundException) when (attempt < maxAttempts)
            {
                Thread.Sleep(10 * attempt);
                recovery = LoadLatestCommitWithGeneration();
                segmentIds = recovery?.SegmentIds ?? [];
                segmentInfos = recovery?.SegmentInfos ?? [];
                generation = recovery?.Generation ?? 0;
                _commitGeneration = generation;
                IndexOpenGuard.EnsureCanOpenSegments(directory, segmentIds, config.CompatibilityMode, forWriting: false, config.CodecCatalog);
            }
            finally
            {
                attemptSnapshot?.Dispose();
            }
        }

        try
        {
            _docBases = AssignDocBases();
            _totalDocCount = _docBases.Length > 0
                ? _docBases[^1] + _readers[^1].MaxDoc
                : 0;

            // Persisted statistics are cheap to load. Missing statistics remain lazy.
            var statsPath = IndexStats.GetStatsPath(directory.DirectoryPath, generation);
            _stats = IndexStats.TryLoadFrom(statsPath);

            if (config.EnableQueryCache)
                _queryCache = config.SharedCache ?? new QueryCache(config.QueryCacheMaxEntries);
        }
        catch
        {
            _segmentReaderCache.Dispose();
            _snapshotLease?.Dispose();
            _snapshotLease = null;
            config.DisposeOwnedDiagnostics();
            throw;
        }
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over the given pre-built segment list (NRT or snapshot scenario).
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The explicit list of segment infos to search.</param>
    /// <param name="similarity">The scoring model to use. Defaults to BM25 if null.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentInfo> segments, ISimilarity? similarity = null)
        : this(directory, CaptureDescriptors(segments), CreateConfig(similarity))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over an immutable segment descriptor list.
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The immutable segment descriptors to search.</param>
    /// <param name="similarity">The scoring model to use. Defaults to BM25 if null.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentDescriptor> segments, ISimilarity? similarity = null)
        : this(directory, CaptureDescriptors(segments), CreateConfig(similarity))
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over the given pre-built segment list with the specified configuration.
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The explicit list of segment infos to search.</param>
    /// <param name="config">Searcher configuration including similarity model, parallelism, and caching options.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentInfo> segments, IndexSearcherConfig config)
        : this(directory, CaptureDescriptors(segments), config)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="IndexSearcher"/> over an immutable segment descriptor list with the specified configuration.
    /// </summary>
    /// <param name="directory">The index directory containing segment files.</param>
    /// <param name="segments">The immutable segment descriptors to search.</param>
    /// <param name="config">Searcher configuration including similarity model, parallelism, and caching options.</param>
    public IndexSearcher(MMapDirectory directory, IReadOnlyList<SegmentDescriptor> segments, IndexSearcherConfig config)
        : this(directory, CaptureDescriptors(segments), config)
    {
    }

    private IndexSearcher(
        MMapDirectory directory,
        SegmentDescriptor[] segments,
        IndexSearcherConfig config)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        CompressionCodecRegistry.MarkIndexOpened();
        _directory = directory;
        _config = config;
        _segmentReaderCache = new BoundedLruCache<string, SegmentReaderState>(
            config.MaxCachedSegmentReaders,
            StringComparer.Ordinal,
            maxRetainedBytes: config.MaxCachedSegmentReaderBytes,
            resourceUsageSelector: static state => state.GetRetainedResourceUsage());
        _similarity = config.Similarity;
        _useLmScoring = _similarity.RequiresCollectionStatistics;
        _useBm25Scoring = _similarity is Bm25Similarity;

        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);
        IndexOpenGuard.EnsureCanOpenSegments(
            directory,
            segments.Select(static segment => segment.SegmentId),
            config.CompatibilityMode,
            forWriting: false,
            config.CodecCatalog);
        try
        {
            var selectedGenerations = SelectedGenerations(
                segments.Select(static segment => (segment.SegmentId, segment.DelGeneration)));
            _snapshotLease = directory.AcquireSnapshot(
                name => SegmentFileSet.IsSnapshotFile(name, selectedGenerations, config.CodecCatalog), out _);
            foreach (var descriptor in segments)
                _readers.Add(new SegmentReader(
                    directory, descriptor, _segmentReaderCache, config.CodecCatalog));

            _docBases = AssignDocBases();
            _totalDocCount = _docBases.Length > 0
                ? _docBases[^1] + _readers[^1].MaxDoc
                : 0;
            _stats = null;

            if (config.EnableQueryCache)
                _queryCache = config.SharedCache ?? new QueryCache(config.QueryCacheMaxEntries);
        }
        catch
        {
            _segmentReaderCache.Dispose();
            _snapshotLease?.Dispose();
            _snapshotLease = null;
            config.DisposeOwnedDiagnostics();
            throw;
        }
    }

    private static SegmentDescriptor[] CaptureDescriptors(IReadOnlyList<SegmentInfo> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Select(static info => new SegmentDescriptor(info)).ToArray();
    }

    private static SegmentDescriptor[] CaptureDescriptors(IReadOnlyList<SegmentDescriptor> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.ToArray();
    }

    // The snapshot leases each segment's files at its selected deletion generation only: superseded
    // generations can be pruned at any moment and are never read through this searcher.
    private static Dictionary<string, int?> SelectedGenerations(
        IEnumerable<(string SegmentId, int? DelGeneration)> segments)
    {
        var selected = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var (segmentId, delGeneration) in segments)
            selected[segmentId] = delGeneration;
        return selected;
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
        ArgumentNullException.ThrowIfNull(query);
        query = RewriteQuery(query);
        ValidateQueryComposition(query);
        int requestedTopN = topN;
        int effectiveTopN = NormaliseTopN(topN);
        if (effectiveTopN < 0 || _readers.Count == 0)
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

    private TopDocs SearchCore(Query query, int topN, ISideCollector? sideCollector = null)
    {
        query = RewriteQuery(query);
        // Public entry points validate composition once (ADR049); SearchCore is also reached for
        // whole-index children such as RRF branches, which the root validation already covered.
        if (query is FuzzyQuery fuzzyQuery && HasCachedFuzzyNoMatches(fuzzyQuery))
            return TopDocs.Empty;

        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

        if (query.CreateWeight(this) is { } weight)
            return ExecuteWeight(query, weight, topN, sideCollector);

        // MoreLikeThis is a cross-segment query: extract terms, build BooleanQuery, delegate
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN, sideCollector);

        // RRF: execute each child query independently, then fuse by rank
        if (query is RrfQuery rrf)
            return ExecuteRrfQuery(rrf, topN, sideCollector);

        // Block join: execute child query, map results to parent docs
        if (query is BlockJoinQuery bjq)
            return ExecuteBlockJoinQuery(bjq, topN, sideCollector);

        // Fast path for the most common query type — avoids
        // PrecomputeGlobalDocFreqs allocation and does only 1 dictionary
        // lookup per segment instead of 2.
        if (query is TermQuery tq)
            return SearchTermQuery(tq, topN, sideCollector);

        // Fast path for FunctionScoreQuery wrapping a TermQuery: inline scoring,
        // reuse ThreadStatic postings buffer, skip PrecomputeGlobalDocFreqs.
        if (query is FunctionScoreQuery { IsSimpleNumericField: true } fsq
            && fsq.Inner is TermQuery fsqTq)
            return SearchFunctionScoreTermQuery(fsqTq, fsq, topN, sideCollector);

        // Fast path for BooleanQuery with all-TermQuery clauses — compute
        // global DFs inline without the generic PrecomputeGlobalDocFreqs tree walk
        if (query is BooleanQuery bq && IsAllTermQueryBoolean(bq))
            return SearchBooleanTermQueryFast(bq, topN, sideCollector);

        // Pattern-based queries (Prefix, Wildcard, Fuzzy) don't have static terms,
        // so PrecomputeGlobalDocFreqs produces an empty dictionary. Skip the tree walk.
        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        var collector = new TopNCollector(topN, sideCollector);

        if (sideCollector is not null || !CanSearchSegmentsInParallel())
        {
            foreach (var reader in _readers)
                ExecuteQuery(query, reader, globalDFs, ref collector);
        }
        else
        {
            int maxDop = ResolvedSearchConcurrency;
            var lockObj = new Lock();
            Parallel.ForEach(_readers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, reader =>
            {
                var localCollector = new TopNCollector(topN);
                ExecuteQuery(query, reader, globalDFs, ref localCollector);
                var localDocs = localCollector.ToTopDocs();
                lock (lockObj)
                {
                    collector.MergeTopDocs(localDocs);
                }
            });
        }

        return collector.ToTopDocs();
    }

    private bool HasCachedFuzzyNoMatches(FuzzyQuery query)
    {
        if (!_config.UseFuzzyExpansionCache)
            return false;

        string fieldPrefix = string.Concat(query.Field, "\0");
        foreach (var reader in _readers)
        {
            if (!reader.TryGetCachedFuzzyMatches(
                    fieldPrefix,
                    query.Term,
                    query.MaxEdits,
                    query.MaxExpansions,
                    out var matches)
                || matches.Count != 0)
            {
                return false;
            }
        }

        return _readers.Count != 0;
    }

    internal TopDocs SearchWithCollectorStrategy(Query query, ITopNCollectorStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(strategy);

        query = RewriteQuery(query);
        ValidateQueryComposition(query);
        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();
        var collector = new TopNCollector(strategy);
        if (_readers.Count == 0)
            return collector.ToTopDocs();

        // These query families coordinate results across segments themselves.
        // Keep their existing execution and feed only their final candidates to
        // the bounded strategy.
        if (query is MoreLikeThisQuery or RrfQuery or BlockJoinQuery)
        {
            var results = SearchCore(query, _totalDocCount);
            foreach (var scoreDoc in results.ScoreDocs)
                collector.Collect(scoreDoc.DocId, scoreDoc.Score);
            return collector.ToTopDocs();
        }

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query);
        if (CanSearchSegmentsInParallel()
            && strategy is IParallelTopNCollectorStrategy parallelStrategy)
        {
            int maxDop = ResolvedSearchConcurrency;
            var mergeLock = new Lock();
            Parallel.ForEach(_readers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, reader =>
            {
                var workerStrategy = parallelStrategy.CreateWorker();
                var workerCollector = new TopNCollector(workerStrategy);
                ExecuteQuery(query, reader, globalDFs, ref workerCollector);
                lock (mergeLock)
                    parallelStrategy.MergeWorker(workerStrategy);
            });
        }
        else
        {
            foreach (var reader in _readers)
                ExecuteQuery(query, reader, globalDFs, ref collector);
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
        ValidateQueryComposition(query);

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
        var result = Search(query, _totalDocCount);
        if (collector is not ILeafCollector leafCollector)
        {
            foreach (var sd in result.ScoreDocs)
                collector.Collect(sd.DocId, sd.Score);
            return;
        }

        var scorer = new CollectorScorer();
        for (int ordinal = 0; ordinal < _readers.Count; ordinal++)
        {
            int docBase = _docBases[ordinal];
            int maxDoc = _readers[ordinal].MaxDoc;
            int endDoc = docBase + maxDoc;
            leafCollector.SetSegment(ordinal, docBase, maxDoc);
            leafCollector.SetScorer(scorer);

            // Search results are score ordered, so scan them once per segment.
            // Segment-aware collectors are intended for aggregation rather than top-N ranking.
            foreach (var sd in result.ScoreDocs)
            {
                if (sd.DocId < docBase || sd.DocId >= endDoc)
                    continue;
                scorer.SetCurrent(sd.DocId, sd.Score);
                leafCollector.Collect(sd.DocId, sd.Score);
            }
        }
    }

    private sealed class CollectorScorer : Scorer
    {
        private int _docId = -1;
        private float _score;

        public override int DocId => _docId;

        public override float Score() => _score;

        public override float Score(int docId, float approximationScore) => approximationScore;

        internal void SetCurrent(int docId, float score)
        {
            _docId = docId;
            _score = score;
        }
    }

    /// <summary>
    /// Returns the total number of documents matching the query without materialising
    /// a scored heap. Uses a zero-allocation count-only collector.
    /// </summary>
    /// <param name="query">The query to count matches for.</param>
    /// <returns>The total number of matching documents.</returns>
    public int Count(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = RewriteQuery(query);
        ValidateQueryComposition(query);

        if (query.CreateWeight(this) is { } weight)
        {
            if (ReferenceEquals(weight.Approximation, query))
                throw new InvalidOperationException(
                    "A custom query weight cannot use its owning query as its approximation.");
            return Count(weight.Approximation);
        }

        if (query is MatchAllDocsQuery)
            return Stats.LiveDocCount;

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
        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

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

        if (!CanSearchSegmentsInParallel())
        {
            foreach (var reader in _readers)
                ExecuteQuery(query, reader, globalDFs, ref collector);
        }
        else
        {
            int maxDop = ResolvedSearchConcurrency;
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
    /// <remarks>
    /// A parser is created for each call. If the supplied analyser is reused by overlapping
    /// calls, it must support concurrent use; otherwise pass a separate analyser to each call.
    /// </remarks>
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
        ArgumentNullException.ThrowIfNull(query);
        query = RewriteQuery(query);
        ValidateQueryComposition(query);
        int requestedTopN = topN;
        int effectiveTopN = NormaliseTopN(topN);
        if (effectiveTopN < 0 || _readers.Count == 0)
            return TopDocs.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Search);
        activity?.SetTag("query.type", query.GetType().Name);

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
        var result = SearchCancellableCore(query, effectiveTopN, cancellationToken);
        sw.Stop();
        _config.Metrics.RecordSearchLatency(sw.Elapsed);

        activity?.SetTag("search.cache_hit", false);
        activity?.SetTag("search.total_hits", result.TotalHits);

        _config.SlowQueryLog?.MaybeLog(query, sw.Elapsed, result.TotalHits);
        _config.SearchAnalytics?.Record(query, sw.Elapsed, result.TotalHits, cacheHit: false);

        _queryCache?.Put(query, requestedTopN, result);
        return result;
    }

    private TopDocs SearchCancellableCore(Query query, int topN, CancellationToken cancellationToken)
    {
        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

        // Cross-segment / composite queries: dispatch before the generic ExecuteQuery path
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN);
        if (query is RrfQuery rrf)
            return ExecuteRrfQuery(rrf, topN, checkResources: cancellationToken.ThrowIfCancellationRequested);
        if (query is BlockJoinQuery bjq)
            return ExecuteBlockJoinQuery(bjq, topN, checkResources: cancellationToken.ThrowIfCancellationRequested);
        if (query is TermQuery tq)
            return SearchTermQuery(tq, topN);
        if (query is FunctionScoreQuery { IsSimpleNumericField: true } fsqCt
            && fsqCt.Inner is TermQuery fsqCtTq)
            return SearchFunctionScoreTermQuery(fsqCtTq, fsqCt, topN);
        // Fast path for BooleanQuery with all-TermQuery clauses
        if (query is BooleanQuery bq && IsAllTermQueryBoolean(bq))
            return SearchBooleanTermQueryFast(bq, topN);

        var globalDFs = PrecomputeGlobalDocFreqsForSearch(query, cancellationToken.ThrowIfCancellationRequested);
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
    /// <remarks>
    /// A parser is created for each call. If the supplied analyser is reused by overlapping
    /// calls, it must support concurrent use; otherwise pass a separate analyser to each call.
    /// </remarks>
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
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);
        ValidateQueryComposition(query);
        if (topN < 0 || _readers.Count == 0)
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
        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

        // Cross-segment / composite queries: dispatch before the generic ExecuteQuery path
        if (query is MoreLikeThisQuery mlt)
            return ExecuteMoreLikeThis(mlt, topN);
        if (query is RrfQuery or BlockJoinQuery)
        {
            // Coordinators run whole child searches, so admit every bounded expansion first.
            if (!TryValidateExpansionLimitsWithResourceChecks(query, options, sw, deadlineTicks))
                return TopDocs.Empty.AsPartial();
            return query is RrfQuery budgetedRrf
                ? ExecuteRrfQuery(budgetedRrf, topN, expansionLimitsValidated: true)
                : ExecuteBlockJoinQuery((BlockJoinQuery)query, topN, expansionLimitsValidated: true);
        }

        var globalDFs = PrecomputeWithResourceChecks(query, options, sw, deadlineTicks);
        if (globalDFs is null) return TopDocs.Empty.AsPartial();
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
        ValidateQueryComposition(query);
        if (perSegmentTopN <= 0 || _readers.Count == 0)
            yield break;

        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

        options ??= SearchOptions.Default;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;

        var globalDFs = PrecomputeWithResourceChecks(query, options, sw, deadlineTicks);
        if (globalDFs is null) yield break;
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
        ValidateQueryComposition(query);

        if (_readers.Count == 0)
            yield break;

        using var fuzzyExecutionScope = PushFuzzyQueryExecutionContext();

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
        ValidateQueryComposition(query);

        if (_readers.Count == 0)
            yield break;

        // One context for the whole operation, entered only around synchronous work between yields.
        var executionContext = CreateFuzzyQueryExecutionContext();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            options.CancellationToken, cancellationToken);
        var ct = linkedCts.Token;

        int perSegment = DerivePerSegmentTopN(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long? deadlineTicks = options.Timeout.HasValue
            ? (long)(options.Timeout.Value.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
            : null;

        Dictionary<(string Field, string Term), int>? globalDFs;
        using (EnterFuzzyQueryExecutionContext(executionContext))
            globalDFs = PrecomputeWithResourceChecks(query, options, sw, deadlineTicks, ct);
        if (globalDFs is null) yield break;
        long perSegmentBytes = (long)perSegment * Scoring.ScoreDoc.EstimatedBytes;
        long emittedBytes = 0;

        foreach (var reader in _readers)
        {
            ct.ThrowIfCancellationRequested();
            if (deadlineTicks.HasValue && sw.ElapsedTicks > deadlineTicks.Value)
                yield break;
            if (emittedBytes + perSegmentBytes > options.MaxResultBytes)
                yield break;

            TopDocs segmentDocs;
            using (EnterFuzzyQueryExecutionContext(executionContext))
            {
                var segmentCollector = new TopNCollector(perSegment);
                ExecuteQuery(query, reader, globalDFs, ref segmentCollector);
                segmentDocs = segmentCollector.ToTopDocs();
            }
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

        var segmentStats = new List<SegmentStats>(_readers.Count);
        foreach (var reader in _readers)
            segmentStats.Add(SegmentStats.FromSegmentReader(reader));

        return IndexStats.FromSegmentStats(segmentStats);
    }

    private static bool ShouldSkipGlobalDocFreqs(Query query) =>
        query is PrefixQuery or WildcardQuery or FuzzyQuery
            or MatchAllDocsQuery or MatchNoDocsQuery
            or FieldExistsQuery or TermInSetQuery or TermsQuery or PointInSetQuery
            or MultiPhraseQuery or IntervalsQuery or CombinedFieldsQuery;

    private Dictionary<(string Field, string Term), int> PrecomputeGlobalDocFreqsForSearch(Query query, Action? checkResources = null)
    {
        ValidateExpansionLimits(query, checkResources);
        PrepareVectorQueries(query, checkResources);
        if (query is CombinedFieldsQuery combined)
            return PrecomputeCombinedFieldUnionDocFreqs(combined);

        var globalDFs = ShouldSkipGlobalDocFreqs(query)
            ? EmptyGlobalDFs
            : PrecomputeGlobalDocFreqs(query);

        // A single segment's own document frequencies are already global. Across segments, every
        // expanded and span term needs its frequency summed over all of them (ADR048), looked up
        // once per term here rather than per posting during scoring.
        if (_readers.Count <= 1 || !ExpansionDocFreqVisitor.Requires(query))
            return globalDFs;
        var merged = new Dictionary<(string Field, string Term), int>(globalDFs);
        query.Visit(new ExpansionDocFreqVisitor(this, merged, checkResources));
        return merged;
    }

    /// <summary>
    /// Collects global document frequencies for prefix, wildcard, regexp and fuzzy expansions and for
    /// span terms, by visiting each segment's matching terms once (ADR048).
    /// </summary>
    private sealed class ExpansionDocFreqVisitor(
        IndexSearcher searcher,
        Dictionary<(string Field, string Term), int> docFreqs,
        Action? checkResources) : QueryVisitor
    {
        private readonly HashSet<Query> _visited = new(ReferenceEqualityComparer.Instance);

        public static bool Requires(Query query)
        {
            var probe = new RequirementProbe();
            query.Visit(probe);
            return probe.Required;
        }

        public override void VisitLeaf(Query query)
        {
            if (!_visited.Add(query))
                return;
            switch (query)
            {
                case PrefixQuery prefix:
                    AddMatching(prefix.Field, reader => Shared(prefix, reader,
                        () => Visit(reader, prefix.Field, prefix.Prefix, null, null)));
                    break;
                case WildcardQuery wildcard:
                    AddMatching(wildcard.Field, reader => Shared(wildcard, reader,
                        () => Visit(reader, wildcard.Field, null, wildcard.Pattern, null)));
                    break;
                case RegexpQuery regexp when UsesDefaultRegexMatching(regexp.CompiledRegex) &&
                                             TryGetSimpleContainsRegexLiteral(regexp.Pattern, out var containsLiteral):
                    // Candidate superset: execution still verifies line-feed terms against the regex.
                    AddMatching(regexp.Field, reader => Shared(regexp, reader,
                        () => reader.GetTermsContaining(string.Concat(regexp.Field, "\0"), containsLiteral)));
                    break;
                case RegexpQuery regexp:
                    string? literal = UsesDefaultRegexMatching(regexp.CompiledRegex) &&
                                      TryGetRegexLiteralPrefix(regexp.Pattern, out var regexPrefix) ? regexPrefix : null;
                    AddMatching(regexp.Field, reader => Shared(regexp, reader,
                        () => Visit(reader, regexp.Field, literal, null, regexp.CompiledRegex)));
                    break;
                case FuzzyQuery fuzzy when searcher.CurrentFuzzyQueryExecutionContext is { } fuzzyContext:
                    // Build the search's execution plan now: one shared automaton and one walk per reader,
                    // selected globally. Execution reuses the same plan, and only selected terms need DFs.
                    foreach (string qualified in fuzzyContext.GetSelectedTerms(fuzzy, checkResources))
                        AddTerm(fuzzy.Field, qualified[(fuzzy.Field.Length + 1)..]);
                    break;
                case FuzzyQuery fuzzy:
                    AddMatching(fuzzy.Field, reader => reader.GetFuzzyMatches(string.Concat(fuzzy.Field, "\0"), fuzzy.Term.AsSpan(),
                            fuzzy.MaxEdits, fuzzy.MaxExpansions, searcher._config.UseFuzzyExpansionCache)
                        .Select(static match => (match.Term, match.Offset)).ToList());
                    break;
            }
        }

        public override void ConsumeTerm(Query query, string field, string term)
        {
            if (query is SpanTermQuery)
                AddTerm(field, term);
            else
                VisitLeaf(query);
        }

        private void AddTerm(string field, string term)
        {
            if (docFreqs.ContainsKey((field, term)))
                return;
            checkResources?.Invoke();
            string qualified = string.Concat(field, "\0", term);
            int total = 0;
            foreach (var reader in searcher._readers)
                total += reader.GetDocFreqByQualified(qualified);
            docFreqs[(field, term)] = total;
        }

        private List<(string Term, long Offset)> Visit(SegmentReader reader, string field, string? prefix, string? wildcard, System.Text.RegularExpressions.Regex? regex)
        {
            var terms = new List<(string Term, long Offset)>();
            reader.VisitMatchingTerms(field, prefix, wildcard, regex, (term, offset) => terms.Add((term, offset)), checkResources);
            return terms;
        }

        /// <summary>Reuses the admission walk's terms for this reader, or walks once and records them for execution.</summary>
        private IReadOnlyList<(string Term, long Offset)> Shared(
            Query query, SegmentReader reader, Func<List<(string Term, long Offset)>> walk)
        {
            var context = searcher.CurrentMultiTermExpansionContext;
            if (context is not null && context.TryGet(query, reader, out var recorded))
                return recorded;
            var terms = walk();
            context?.Store(query, reader, terms);
            return terms;
        }

        /// <summary>
        /// Sums each expanded term's frequency over every segment, including segments whose own
        /// expansion did not select it, so the value equals the merged index's frequency.
        /// </summary>
        private void AddMatching(string field, Func<SegmentReader, IReadOnlyList<(string Term, long Offset)>> matchingQualifiedTerms)
        {
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reader in searcher._readers)
            {
                checkResources?.Invoke();
                foreach (var (term, _) in matchingQualifiedTerms(reader))
                    expanded.Add(term);
            }
            foreach (string qualified in expanded)
            {
                var key = (field, qualified[(field.Length + 1)..]);
                if (docFreqs.ContainsKey(key))
                    continue;
                int total = 0;
                foreach (var reader in searcher._readers)
                    total += reader.GetDocFreqByQualified(qualified);
                docFreqs[key] = total;
            }
        }

        private sealed class RequirementProbe : QueryVisitor
        {
            public bool Required { get; private set; }

            public override void VisitLeaf(Query query)
                => Required |= query is PrefixQuery or WildcardQuery or RegexpQuery or FuzzyQuery;

            public override void ConsumeTerm(Query query, string field, string term)
                => Required |= query is SpanTermQuery;
        }
    }

    private IndexRecovery.RecoveryResult? LoadLatestCommitWithGeneration()
    {
        return IndexRecovery.RecoverLatestCommit(
            _directory.DirectoryPath,
            cleanupOrphans: false,
            catalog: _config.CodecCatalog);
    }

    private List<string> LoadLatestCommit()
    {
        var recovery = LoadLatestCommitWithGeneration();
        return recovery?.SegmentIds ?? [];
    }

    /// <summary>Disposes all underlying segment readers.</summary>
    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (var reader in _readers)
        {
            try { reader.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }

        try { _segmentReaderCache.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        var snapshotLease = _snapshotLease;
        _snapshotLease = null;
        if (snapshotLease is not null)
        {
            try { snapshotLease.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }

        try { _config.DisposeOwnedDiagnostics(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        if (failures is not null)
            throw new AggregateException("Index searcher cleanup failed.", failures);
    }

    private TopDocs ExecuteRrfQuery(RrfQuery rrf, int topN, ISideCollector? sideCollector = null,
        Action? checkResources = null, bool expansionLimitsValidated = false)
    {
        if (rrf.Queries.Count == 0) return TopDocs.Empty;

        // Reject an over-limit expansion in any child before the first child search runs.
        if (!expansionLimitsValidated)
            ValidateExpansionLimits(rrf, checkResources);

        // Execute each child query independently to get ranked result lists
        var childResults = new TopDocs[rrf.Queries.Count];
        int childTopN = sideCollector is null ? topN : _totalDocCount;
        for (int i = 0; i < rrf.Queries.Count; i++)
            childResults[i] = SearchCore(rrf.Queries[i], childTopN);

        var combined = RrfQuery.Combine(childResults, childTopN, rrf.K);
        if (sideCollector is not null)
        {
            foreach (var scoreDoc in combined.ScoreDocs)
            {
                int readerIndex = ResolveReaderIndex(scoreDoc.DocId);
                sideCollector.Collect(
                    scoreDoc.DocId,
                    scoreDoc.Score,
                    _readers[readerIndex],
                    scoreDoc.DocId - _docBases[readerIndex]);
            }
        }

        if (combined.ScoreDocs.Length <= topN)
            return combined;
        var page = new ScoreDoc[topN];
        Array.Copy(combined.ScoreDocs, page, topN);
        return new TopDocs(combined.TotalHits, page, combined.IsPartial);
    }

    private TopDocs ExecuteBlockJoinQuery(BlockJoinQuery bjq, int topN, ISideCollector? sideCollector = null,
        Action? checkResources = null, bool expansionLimitsValidated = false)
    {
        // Child matching below bypasses the generic precompute path, so admit bounded prefix,
        // wildcard and regexp children here, before any child postings are visited.
        if (!expansionLimitsValidated)
            ValidateExpansionLimits(bjq, checkResources);

        var collector = new TopNCollector(topN, sideCollector);
        foreach (var reader in _readers)
            ExecuteBlockJoinOnSegment(bjq, reader, ref collector);

        return collector.ToTopDocs();
    }

    /// <summary>
    /// Matches one segment's live children and collects each live parent once. Both the top-level
    /// block-join route and nested clauses use this matcher, so they share one child interpretation.
    /// </summary>
    private void ExecuteBlockJoinOnSegment(BlockJoinQuery bjq, SegmentReader reader, ref TopNCollector collector)
    {
        using var queryLease = reader.AcquireQueryLease();
        collector.SetSideCollectorContext(reader);
        int docBase = reader.DocBase;
        var pbs = reader.GetParentBitSet();
        if (pbs is null) return;

        float boost = bjq.Boost;

        // Block join children are contiguous before their parent, so parents
        // are encountered in non-decreasing docId order. A simple lastParent
        // check replaces the BitArray(MaxDoc) dedup at zero allocation cost.
        int lastParent = -1;

        if (bjq.ChildQuery is TermQuery tq)
        {
            // Fast path: stream PostingsEnum directly
            var qt = string.Concat(tq.Field, "\x00", tq.Term);
            using var pe = reader.GetPostingsEnum(qt);
            while (pe.MoveNextUnchecked(out int childDocId, out _))
            {
                if (!reader.IsLive(childDocId) || pbs.IsParent(childDocId)) continue;
                int parentLocal = pbs.NextParent(childDocId + 1);
                if (parentLocal >= 0 && parentLocal != lastParent)
                {
                    lastParent = parentLocal;
                    if (reader.IsLive(parentLocal))
                        collector.Collect(docBase + parentLocal, boost);
                }
            }
            return;
        }

        // General path: collect matching child doc IDs into a lightweight
        // BitArray instead of materialising a TopNCollector(MaxDoc).
        var childBits = new System.Collections.BitArray(reader.MaxDoc);
        CollectChildDocsIntoBitArray(bjq.ChildQuery, reader, childBits);

        for (int docId = 0; docId < reader.MaxDoc; docId++)
        {
            if (!childBits[docId] || !reader.IsLive(docId)) continue;
            if (pbs.IsParent(docId)) continue;
            int parentLocal = pbs.NextParent(docId + 1);
            if (parentLocal >= 0 && parentLocal != lastParent)
            {
                lastParent = parentLocal;
                if (reader.IsLive(parentLocal))
                    collector.Collect(docBase + parentLocal, boost);
            }
        }
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
                    while (pe.MoveNextUnchecked(out int docId, out _))
                    {
                        if (reader.IsLive(docId))
                            bits[docId] = true;
                    }
                    break;
                }
            case BooleanQuery bq:
                CollectChildBooleanIntoBitArray(bq, reader, bits);
                break;
            default:
                {
                    // Fallback for complex child queries: full query execution
                    var globalDFs = PrecomputeGlobalDocFreqs(query);
                    var segCollector = new TopNCollector(Math.Max(reader.MaxDoc, 1));
                    ExecuteQuery(query, reader, globalDFs, ref segCollector);
                    int docBase = reader.DocBase;
                    var childDocs = segCollector.ToTopDocs();
                    foreach (var sd in childDocs.ScoreDocs)
                        bits[sd.DocId - docBase] = true;
                    break;
                }
        }
    }

    /// <summary>
    /// Applies general Boolean matching to block-join children: Must clauses intersect, MustNot
    /// clauses exclude, and Should clauses are counted per child. With Must present, Should clauses
    /// are optional unless a positive minimum is set; without Must, at least
    /// <c>max(1, MinimumNumberShouldMatch)</c> must match. Pure-negative Booleans match nothing.
    /// </summary>
    private void CollectChildBooleanIntoBitArray(BooleanQuery bq, SegmentReader reader,
        System.Collections.BitArray bits)
    {
        int length = bits.Length;
        System.Collections.BitArray? mustResult = null;
        System.Collections.BitArray? scratch = null;
        int shouldCount = 0;
        foreach (var clause in bq.Clauses)
        {
            if (clause.Occur == Occur.Should)
            {
                shouldCount++;
                continue;
            }
            if (clause.Occur != Occur.Must)
                continue;

            if (mustResult is null)
            {
                mustResult = new System.Collections.BitArray(length);
                CollectChildDocsIntoBitArray(clause.Query, reader, mustResult);
            }
            else
            {
                scratch ??= new System.Collections.BitArray(length);
                scratch.SetAll(false);
                CollectChildDocsIntoBitArray(clause.Query, reader, scratch);
                mustResult.And(scratch);
            }
        }

        int minimum = bq.MinimumNumberShouldMatch;
        if (mustResult is null)
        {
            if (shouldCount == 0)
                return;
            minimum = Math.Max(1, minimum);
        }

        // Without Must, minimum is at least 1 here, so this branch always has a Must result.
        System.Collections.BitArray result;
        if (minimum <= 0 && mustResult is not null)
        {
            result = mustResult;
        }
        else if (minimum > shouldCount)
        {
            return;
        }
        else if (minimum == 1)
        {
            var anyShould = new System.Collections.BitArray(length);
            foreach (var clause in bq.Clauses)
            {
                if (clause.Occur != Occur.Should) continue;
                scratch ??= new System.Collections.BitArray(length);
                scratch.SetAll(false);
                CollectChildDocsIntoBitArray(clause.Query, reader, scratch);
                anyShould.Or(scratch);
            }
            result = mustResult is null ? anyShould : mustResult.And(anyShould);
        }
        else
        {
            var counts = new int[length];
            foreach (var clause in bq.Clauses)
            {
                if (clause.Occur != Occur.Should) continue;
                scratch ??= new System.Collections.BitArray(length);
                scratch.SetAll(false);
                CollectChildDocsIntoBitArray(clause.Query, reader, scratch);
                for (int docId = 0; docId < length; docId++)
                {
                    if (scratch[docId])
                        counts[docId]++;
                }
            }

            result = new System.Collections.BitArray(length);
            for (int docId = 0; docId < length; docId++)
            {
                if (counts[docId] >= minimum && (mustResult is null || mustResult[docId]))
                    result[docId] = true;
            }
        }

        foreach (var clause in bq.Clauses)
        {
            if (clause.Occur != Occur.MustNot) continue;
            scratch ??= new System.Collections.BitArray(length);
            scratch.SetAll(false);
            CollectChildDocsIntoBitArray(clause.Query, reader, scratch);
            result.And(scratch.Not());
        }

        bits.Or(result);
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
        // One query pin per segment covers every term and field, so liveness checks and cursor
        // steps reuse the pinned state instead of taking a read lease per posting.
        var totals = new int[query.Terms.Count];
        var docs = new HashSet<int>();
        foreach (var reader in _readers)
        {
            reader.WithQueryPin((Query: query, Totals: totals, Docs: docs), static (pinned, state) =>
            {
                var terms = state.Query.Terms;
                for (int t = 0; t < terms.Count; t++)
                {
                    state.Docs.Clear();
                    foreach (var field in state.Query.Fields)
                    {
                        using var postings = pinned.GetPostingsEnum(string.Concat(field, "\x00", terms[t]));
                        while (postings.MoveNextUnchecked(out int docId, out _))
                        {
                            if (pinned.IsLive(docId))
                                state.Docs.Add(docId);
                        }
                    }

                    state.Totals[t] += state.Docs.Count;
                }

                return 0;
            });
        }

        var result = new Dictionary<(string Field, string Term), int>(query.Terms.Count);
        for (int t = 0; t < totals.Length; t++)
            result[(CombinedFieldsDocFreqKey, query.Terms[t])] = totals[t];
        return result;
    }

    private void CollectTerms(Query query, HashSet<(string Field, string Term)> terms)
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
            case SynonymQuery sq:
                foreach (var term in sq.Terms)
                    terms.Add((sq.Field, term));
                break;
            case ConstantScoreQuery csq:
                CollectTerms(csq.Inner, terms);
                break;
            case FunctionScoreQuery fsq:
                // A nested function score must score its inner terms with global statistics (ADR048),
                // exactly as the top-level function-score path does.
                CollectTerms(fsq.Inner, terms);
                break;
            case DisjunctionMaxQuery dmq:
                foreach (var d in dmq.Disjuncts)
                    CollectTerms(d, terms);
                break;
            default:
                var rewritten = query.Rewrite();
                if (!ReferenceEquals(rewritten, query))
                    CollectTerms(RewriteQuery(rewritten), terms);
                else if (query.CreateWeight(this) is { } weight)
                    CollectTerms(weight.Approximation, terms);
                break;
        }
    }

    private TopDocs ExecuteWeight(Query owner, Weight weight, int topN, ISideCollector? sideCollector = null)
    {
        if (ReferenceEquals(weight.Approximation, owner))
            throw new InvalidOperationException(
                "A custom query weight cannot use its owning query as its approximation.");
        var scorer = weight.CreateScorer(this)
            ?? throw new InvalidOperationException("Weight.CreateScorer() returned null.");

        // Stream approximation matches directly through a zero-capacity side
        // collector. This avoids materialising one ScoreDoc for every candidate
        // when a custom weight is used with exact facets or aggregations.
        var collector = new WeightedCandidateCollector(scorer, topN, sideCollector);
        SearchCore(weight.Approximation, 0, collector);
        return collector.ToTopDocs();
    }

    /// <summary>Scores approximation matches while preserving exhaustive side collection.</summary>
    private sealed class WeightedCandidateCollector : ISideCollector
    {
        private readonly Scorer _scorer;
        private readonly ISideCollector? _sideCollector;
        private TopNCollector _collector;

        public WeightedCandidateCollector(Scorer scorer, int topN, ISideCollector? sideCollector)
        {
            _scorer = scorer;
            _sideCollector = sideCollector;
            _collector = new TopNCollector(topN);
        }

        public void Collect(int globalDocId, float score, SegmentReader reader, int localDocId)
        {
            float weightedScore = _scorer.Score(globalDocId, score);
            _sideCollector?.Collect(globalDocId, weightedScore, reader, localDocId);
            _collector.Collect(globalDocId, weightedScore);
        }

        public TopDocs ToTopDocs() => _collector.ToTopDocs();
    }

    private static Query RewriteQuery(Query query)
    {
        const int maxRewriteRounds = 16;
        for (int i = 0; i < maxRewriteRounds; i++)
        {
            var rewritten = query.Rewrite()
                ?? throw new InvalidOperationException("Query.Rewrite() returned null.");
            if (ReferenceEquals(rewritten, query))
                return query;
            query = rewritten;
        }

        throw new InvalidOperationException(
            $"Query rewrite did not stabilise after {maxRewriteRounds} rounds.");
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
        long globalCollectionFreq = RequiresCollectionStatistics(query.Field)
            ? GetGlobalCollectionFreq(qt)
            : 0;

        if (globalDF == 0)
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
            return TopDocs.Empty;
        }

        // Phase 2: score using already-decoded postings
        float avgDocLength = Stats.GetAvgFieldLength(query.Field);
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
                using var queryLease = reader.AcquireQueryLease();
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                var fieldLengths = reader.GetFieldLengthsForQuery(query.Field);
                reader.TryGetFieldBoosts(query.Field, out var fieldBoosts);

                while (postings.MoveNextUnchecked(out int docId, out int tf))
                {
                    if (hasDeletions && !reader.IsLive(docId)) continue;

                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Value.Length
                        ? fieldLengths.Value.Span[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, tf, docLength, query.Field);
                    if (boost != 1.0f) score *= boost;
                    score = ApplyFieldBoost(fieldBoosts, docId, score);
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
    private TopDocs SearchFunctionScoreTermQuery(TermQuery tq, FunctionScoreQuery fsq, int topN, ISideCollector? sideCollector = null)
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
        long globalCollectionFreq = RequiresCollectionStatistics(tq.Field)
            ? GetGlobalCollectionFreq(qt)
            : 0;

        if (globalDF == 0)
        {
            for (int i = 0; i < readerCount; i++)
                postingsArr[i].Dispose();
            return TopDocs.Empty;
        }

        // Compute BM25 factors once from the global doc frequency.
        float avgDocLength = Stats.GetAvgFieldLength(tq.Field);
        var (f1, f2, f3) = ComputeTermFactors(globalDF, avgDocLength, globalCollectionFreq, tq.Field);
        float boost = tq.Boost;

        // Reuse the ThreadStatic collector heap.
        if (t_collectorHeapCache is null || t_collectorHeapCache.Length < topN)
            t_collectorHeapCache = new ScoreDoc[topN];
        var collector = new TopNCollector(t_collectorHeapCache, topN, sideCollector);

        try
        {
            for (int i = 0; i < readerCount; i++)
            {
                ref var postings = ref postingsArr[i];
                if (postings.IsExhausted) continue;

                var reader = _readers[i];
                using var queryLease = reader.AcquireQueryLease();
                collector.SetSideCollectorContext(reader);
                int docBase = reader.DocBase;
                bool hasDeletions = reader.HasDeletions;
                var fieldLengths = reader.GetFieldLengthsForQuery(tq.Field);
                reader.TryGetFieldBoosts(tq.Field, out var fieldBoosts);
                bool hasNumericDocValues = reader.TryGetNumericDocValues(
                    fsq.NumericField, out var numericValues);

                // Single pass: BM25, field boost, function score, then top-N collect.
                while (postings.MoveNextUnchecked(out int docId, out int tf))
                {
                    if (hasDeletions && !reader.IsLive(docId)) continue;

                    int docLength = fieldLengths is not null && (uint)docId < (uint)fieldLengths.Value.Length
                        ? fieldLengths.Value.Span[docId] : 1;
                    float score = ScoreTerm(f1, f2, f3, tf, docLength, tq.Field);
                    if (boost != 1.0f) score *= boost;
                    score = ApplyFieldBoost(fieldBoosts, docId, score);

                    // Modify the field-boosted BM25 score with the numeric doc value.
                    // Read the packed column once per segment instead of probing the
                    // sparse numeric map for every matching document.
                    if (hasNumericDocValues)
                    {
                        if (numericValues!.TryGetValue(docId, out double numericValue))
                            score = FunctionScoreQuery.Combine(score, numericValue, fsq.Mode);
                    }
                    else if (reader.TryGetNumericValue(fsq.NumericField, docId, out double fieldValue))
                    {
                        score = FunctionScoreQuery.Combine(score, fieldValue, fsq.Mode);
                    }

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
