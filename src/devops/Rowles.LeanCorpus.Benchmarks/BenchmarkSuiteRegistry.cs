using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record BenchmarkSuiteDescriptor(
    string Id,
    ImmutableArray<string> Aliases,
    bool IsSelector,
    string HelpText);

internal sealed record BenchmarkWorkloadDescriptor(
    string ReportName,
    Type BenchmarkType,
    ImmutableArray<string> SuiteIds,
    bool IncludeInAll,
    bool IncludeWithExplicit);

internal static class BenchmarkSuiteRegistry
{
    private static readonly ImmutableArray<BenchmarkWorkloadDescriptor> Workloads =
    [
        Workload("query", typeof(TermQueryBenchmarks), true, false, "query"),
        Workload("parser", typeof(QueryParserBenchmarks), true, false, "parser"),
        Workload("parser-hot-path", typeof(QueryParserHotPathBenchmarks), true, false, "parser"),
        Workload("index", typeof(IndexingBenchmarks), true, false, "index"),
        Workload("index-language-analysis", typeof(LanguageAnalysisIndexingBenchmarks), true, false, "index"),
        Workload("boolean", typeof(BooleanQueryBenchmarks), true, false, "boolean"),
        Workload("phrase", typeof(PhraseQueryBenchmarks), true, false, "phrase"),
        Workload("prefix", typeof(PrefixQueryBenchmarks), true, false, "prefix"),
        Workload("fuzzy", typeof(FuzzyQueryBenchmarks), true, false, "fuzzy-parity"),
        Workload("fuzzy-cache", typeof(FuzzyExpansionCacheBenchmarks), true, false, "fuzzy-cache"),
        Workload("wildcard", typeof(WildcardQueryBenchmarks), true, false, "wildcard"),
        Workload("deletion-queue", typeof(DeletionQueueBenchmarks), true, false, "deletion", "deletion-queue"),
        Workload("deletion-commit", typeof(DeletionCommitBenchmarks), true, false, "deletion", "deletion-commit"),
        Workload("tokenbudget", typeof(TokenBudgetBenchmarks), false, true, "tokenbudget"),
        Workload("tokenbudget-policy-variants", typeof(TokenBudgetPolicyVariantBenchmarks), false, true, "tokenbudget"),
        Workload("diagnostics", typeof(DiagnosticsBenchmarks), false, true, "diagnostics"),
        Workload("suggester", typeof(SuggesterBenchmarks), true, false, "suggester"),
        Workload("schemajson", typeof(SchemaAndJsonBenchmarks), true, false, "schemajson"),
        Workload("indexsort-index", typeof(IndexSortIndexBenchmarks), true, false, "indexsort", "indexsort-index"),
        Workload("indexsort-search", typeof(IndexSortSearchBenchmarks), true, false, "indexsort", "indexsort-search"),
        Workload("blockjoin-index", typeof(BlockJoinIndexBenchmarks), true, false, "blockjoin", "blockjoin-index"),
        Workload("blockjoin-search", typeof(BlockJoinSearchBenchmarks), true, false, "blockjoin", "blockjoin-search"),
        Workload("gutenberg-index", typeof(GutenbergIndexingBenchmarks), false, false, "gutenberg-index"),
        Workload("gutenberg-search", typeof(GutenbergSearchBenchmarks), false, false, "gutenberg-search"),
        Workload("range", typeof(RangeQueryBenchmarks), true, false, "range"),
        Workload("regexp", typeof(RegexpQueryBenchmarks), true, false, "regexp"),
        Workload("dismax", typeof(DisjunctionMaxQueryBenchmarks), true, false, "dismax"),
        Workload("multiphrase", typeof(MultiPhraseQueryBenchmarks), true, false, "multiphrase"),
        Workload("span", typeof(SpanQueryBenchmarks), true, false, "span"),
        Workload("mlt", typeof(MoreLikeThisBenchmarks), true, false, "mlt"),
        Workload("mlt-single-segment", typeof(MoreLikeThisSingleSegmentBenchmarks), true, false, "mlt"),
        Workload("highlighter", typeof(HighlighterBenchmarks), true, false, "highlighter"),
        Workload("tv-highlighter", typeof(TermVectorHighlighterBenchmarks), true, false, "tv-highlighter"),
        Workload("searcher-mgr", typeof(SearcherManagerBenchmarks), true, false, "searcher-mgr"),
        Workload("combined", typeof(CombinedFieldsQueryBenchmarks), true, false, "combined"),
        Workload("terminset", typeof(TermInSetQueryBenchmarks), true, false, "terminset"),
        Workload("aggregation", typeof(AggregationBenchmarks), true, false, "aggregation"),
        Workload("approximation", typeof(ApproximationBenchmarks), false, true, "approximation"),
        Workload("spatial-aggregation", typeof(SpatialAggregationBenchmarks), true, false, "aggregation"),
        Workload("query-cache", typeof(QueryCacheBenchmarks), true, false, "query-cache"),
        Workload("parallel", typeof(ParallelSearchBenchmarks), true, false, "parallel"),
        Workload("function-score", typeof(FunctionScoreQueryBenchmarks), true, false, "function-score"),
        Workload("geo", typeof(GeoQueryBenchmarks), true, false, "geo"),
        Workload("collapse-facet", typeof(CollapseAndFacetBenchmarks), true, false, "collapse-facet"),
        Workload("similarity", typeof(SimilarityBenchmarks), true, false, "similarity"),
        Workload("async-index", typeof(AsyncIndexingBenchmarks), true, false, "async-index"),
        Workload("vq", typeof(VectorQuantisationBenchmarks), true, true, "vq"),
        Workload("hnsw", typeof(HnswSearchBenchmarks), true, true, "hnsw"),
        Workload("hybrid", typeof(HybridSearchBenchmarks), true, false, "hybrid"),
        Workload("packed-int-codec", typeof(PackedIntCodecBenchmarks), false, true, "packed-int-codec"),
        Workload("codec-frame", typeof(CodecFrameBenchmarks), false, true, "codec-frame"),
        Workload("codec-frame-read", typeof(CodecFrameReadBenchmarks), false, true, "codec-frame-read"),
        Workload("codec-migration", typeof(CodecMigrationBenchmarks), false, true, "codec-migration"),
        Workload("numeric-aggregator", typeof(NumericAggregatorSimdBenchmarks), false, true, "numeric-aggregator"),
        Workload("index-writer", typeof(IndexWriterContentionBenchmarks), false, true, "index-writer"),
        Workload("concurrent-write", typeof(ConcurrentVsSequentialBenchmarks), false, true, "concurrent-write"),
        Workload("merge", typeof(MergeBenchmarks), false, true, "merge"),
        Workload("merge-vector-memory", typeof(VectorMergeMemoryBenchmarks), false, true, "merge"),
        Workload("merge-payload", typeof(MergePayloadBenchmarks), false, true, "merge-payload"),
        Workload("flush", typeof(FlushBenchmarks), false, true, "flush"),
        Workload("flush-term-cardinality", typeof(FlushTermCardinalityBenchmarks), false, true, "flush-term-cardinality"),
        Workload("postings-arena", typeof(PostingsArenaBenchmarks), false, true, "postings-arena"),
        Workload("packed-bkd", typeof(PackedBkdBenchmarks), false, true, "packed-bkd"),
        Workload("nearest", typeof(SpatialNearestBenchmarks), false, true, "nearest"),
        Workload("nearest-compatibility", typeof(SpatialNearestCompatibilityBenchmarks), false, true, "nearest"),
        Workload("shape-index", typeof(ShapeIndexingBenchmarks), false, true, "shape"),
        Workload("shape-relations", typeof(ShapeRelationBenchmarks), false, true, "shape"),
        Workload("shape-docvalues", typeof(ShapeDocValuesBenchmarks), false, true, "shape"),
        Workload("shape-docvalues-merge", typeof(ShapeDocValuesMergeBenchmarks), false, true, "shape"),
        Workload("shape-docvalues-copy", typeof(ShapeDocValuesCopyBenchmarks), false, true, "shape"),
        Workload("spatial-utilities", typeof(SpatialUtilityBenchmarks), false, true, "shape"),
        Workload("docvalues-read", typeof(DocValuesReadBenchmarks), false, true, "docvalues-read"),
        Workload("docvalues-write", typeof(BinaryDocValuesWriteBenchmarks), false, true, "docvalues-write"),
        Workload("stored-fields", typeof(StoredFieldsReadBenchmarks), true, false, "stored-fields"),
        Workload("stored-fields-write", typeof(StoredFieldsWriteBenchmarks), true, false, "stored-fields"),
        Workload("stored-fields-byte-bounded", typeof(StoredFieldsByteBoundedBenchmarks), true, false, "stored-fields"),
        Workload("bkd", typeof(BKDTreeBenchmarks), false, true, "bkd"),
        Workload("fst-lookup", typeof(FstLookupBenchmarks), false, true, "fst-lookup"),
        Workload("mmap-io", typeof(MMapDirectoryIOBenchmarks), false, true, "mmap-io"),
        Workload("compound-file", typeof(CompoundFileBenchmarks), false, true, "compound-file"),
        Workload("windows-filesystem", typeof(WindowsFileSystemBenchmarks), false, true, "windows-filesystem"),
        Workload("windows-storage-path", typeof(WindowsStoragePathBenchmarks), false, true, "windows-storage"),
        Workload("incremental-backup", typeof(IncrementalBackupBenchmarks), false, true, "incremental-backup"),
        Workload("reader-manager", typeof(ReaderManagerLifecycleBenchmarks), false, true, "reader-manager"),
        Workload("term-caches", typeof(QualifiedTermCacheBenchmarks), false, true, "term-caches"),
        Workload("bounded-lru-cache", typeof(BoundedLruCacheFailureBenchmarks), false, true, "bounded-lru-cache"),
        Workload("segment-reader-cache", typeof(SegmentReaderCacheBenchmarks), false, true, "segment-reader-cache"),
        Workload("segment-reader-resource-cache", typeof(SegmentReaderResourceCacheBenchmarks), false, true, "segment-reader-resource-cache"),
        Workload("vector-first-touch", typeof(VectorFirstTouchBenchmarks), false, true, "vector-first-touch"),
        Workload("multi-reader", typeof(MultiReaderBenchmarks), false, true, "multi-reader"),
        Workload("ordinal-map", typeof(OrdinalMapBenchmarks), false, true, "ordinal-map"),
        Workload("search-session", typeof(SearchSessionBenchmarks), false, true, "search-session"),
        Workload("ranking-evaluation", typeof(RankingEvaluationBenchmarks), false, true, "ranking-evaluation"),
        Workload("ranking-pipeline", typeof(RankingPipelineBenchmarks), false, true, "ranking-pipeline")
    ];

    private static readonly Lazy<ImmutableDictionary<string, BenchmarkSuiteDescriptor>> SuiteCatalog = new(BuildSuites);
    private static readonly Lazy<ImmutableDictionary<string, string>> AliasCatalog = new(BuildAliases);

    public static IReadOnlyList<BenchmarkSuiteDescriptor> Descriptors => SuiteCatalog.Value.Values
            .OrderBy(static suite => suite.Id, StringComparer.Ordinal)
            .ToArray();

    public static string ParseSuiteId(string value)
    {
        var key = value.ToLowerInvariant();
        return AliasCatalog.Value.TryGetValue(key, out var suiteId)
            ? suiteId
            : throw new ArgumentException($"Unknown benchmark suite '{value}'. Use --help to list available suites.");
    }

    public static IReadOnlyList<BenchmarkWorkloadDescriptor> SelectWorkloads(IReadOnlySet<string> selectedSuiteIds)
    {
        var includesAll = selectedSuiteIds.Contains("all") || selectedSuiteIds.Contains("all-with-explicit");
        var includesExplicit = selectedSuiteIds.Contains("explicit") || selectedSuiteIds.Contains("all-with-explicit");
        return Workloads.Where(workload =>
                (includesAll && workload.IncludeInAll) ||
                (includesExplicit && workload.IncludeWithExplicit) ||
                workload.SuiteIds.Any(selectedSuiteIds.Contains))
            .ToArray();
    }

    public static bool UsesStandardSearchFixture(IReadOnlySet<string> selectedSuiteIds)
        => selectedSuiteIds.Contains("all") || selectedSuiteIds.Contains("all-with-explicit") ||
            selectedSuiteIds.Overlaps(
            [
                "query", "boolean", "phrase", "prefix", "fuzzy", "fuzzy-parity", "fuzzy-cache", "wildcard", "regexp", "dismax",
                "multiphrase", "span", "searcher-mgr", "terminset", "query-cache", "similarity"
            ]);

    public static string CreateManifestJson()
        => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            project = "core",
            suites = Descriptors.Select(static suite => new
            {
                suite.Id,
                suite.Aliases,
                suite.IsSelector
            }),
            benchmarkClasses = Workloads.Select(static workload => new
            {
                type = workload.BenchmarkType.FullName,
                workload.ReportName,
                workload.SuiteIds,
                workload.IncludeInAll,
                workload.IncludeWithExplicit
            }),
            discoveredBenchmarkClasses = DiscoverBenchmarkTypes().Select(static type => new { type = type.FullName })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static IEnumerable<Type> DiscoverBenchmarkTypes()
        => typeof(BenchmarkSuiteRegistry).Assembly.GetTypes()
            .Where(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(static method => method.IsDefined(typeof(BenchmarkAttribute), inherit: false)))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);

    public static void PrintHelp()
    {
        Console.WriteLine("Suites:");
        foreach (var suite in Descriptors)
            Console.WriteLine($"  {suite.Id,-32} {suite.HelpText}");
    }

    private static ImmutableDictionary<string, BenchmarkSuiteDescriptor> BuildSuites()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, BenchmarkSuiteDescriptor>(StringComparer.Ordinal);
        builder.Add("all", new("all", [], true, "Run all primary synthetic benchmark suites (default)."));
        builder.Add("all-with-explicit", new("all-with-explicit", [.. LegacyAliases["all-with-explicit"]], true, "Run all primary plus all explicit-only suites."));
        builder.Add("explicit", new("explicit", [], true, "Run all explicit-only suites."));

        foreach (var suiteId in Workloads.SelectMany(static workload => workload.SuiteIds).Distinct(StringComparer.Ordinal))
        {
            if (builder.ContainsKey(suiteId))
                continue;
            var matching = Workloads.Where(workload => workload.SuiteIds.Contains(suiteId, StringComparer.Ordinal)).ToArray();
            var types = string.Join(", ", matching.Select(static workload => workload.BenchmarkType.Name));
            var explicitOnly = matching.All(static workload => !workload.IncludeInAll && workload.IncludeWithExplicit);
            ImmutableArray<string> aliases = LegacyAliases.TryGetValue(suiteId, out var configuredAliases) ? [.. configuredAliases] : [];
            builder.Add(suiteId, new(suiteId, aliases, false,
                explicitOnly ? $"{types} (explicit only)." : types + "."));
        }

        if (Workloads.Select(static workload => workload.BenchmarkType).Distinct().Count() != Workloads.Length)
            throw new InvalidOperationException("Core benchmark registry contains a benchmark class more than once.");

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, string> BuildAliases()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var suite in SuiteCatalog.Value.Values)
        {
            AddAlias(suite.Id, suite.Id);
            foreach (var alias in suite.Aliases)
                AddAlias(alias, suite.Id);
        }
        foreach (var (suiteId, aliases) in LegacyAliases)
        {
            foreach (var alias in aliases)
                AddAlias(alias, suiteId);
        }

        return builder.ToImmutable();

        void AddAlias(string alias, string suiteId)
        {
            if (builder.TryGetValue(alias, out var existing))
            {
                if (!string.Equals(existing, suiteId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Benchmark suite alias '{alias}' maps to more than one suite ID.");
                return;
            }

            builder.Add(alias, suiteId);
        }
    }

    private static readonly IReadOnlyDictionary<string, string[]> LegacyAliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["all-with-explicit"] = ["allwithexplicit"],
        ["fuzzy-parity"] = ["fuzzy"],
        ["packed-int-codec"] = ["packedintcodec"],
        ["codec-frame"] = ["codecframe"],
        ["codec-frame-read"] = ["codecframeread"],
        ["codec-migration"] = ["codecmigration"],
        ["numeric-aggregator"] = ["numericaggregator"],
        ["index-writer"] = ["indexwriter"],
        ["concurrent-write"] = ["concurrentwrite"],
        ["merge-payload"] = ["mergepayload"],
        ["postings-arena"] = ["postingsarena"],
        ["packed-bkd"] = ["packedbkd"],
        ["nearest"] = ["spatial-nearest"],
        ["shape"] = ["shapes"],
        ["docvalues-read"] = ["docvaluesread"],
        ["docvalues-write"] = ["docvalueswrite"],
        ["stored-fields"] = ["storedfields"],
        ["bkd"] = ["bkd-tree"],
        ["fst-lookup"] = ["fstlookup"],
        ["mmap-io"] = ["mmapio"],
        ["compound-file"] = ["compoundfile"],
        ["windows-filesystem"] = ["windowsfilesystem"],
        ["windows-storage"] = ["windowsstorage"],
        ["incremental-backup"] = ["incrementalbackup"],
        ["reader-manager"] = ["readermanager"],
        ["term-caches"] = ["termcaches"],
        ["bounded-lru-cache"] = ["boundedlrucache"],
        ["segment-reader-cache"] = ["segmentreadercache"],
        ["segment-reader-resource-cache"] = ["segmentreaderresourcecache"],
        ["multi-reader"] = ["multireader"],
        ["ordinal-map"] = ["ordinalmap"],
        ["search-session"] = ["searchsession"],
        ["ranking-evaluation"] = ["rankingevaluation"],
        ["ranking-pipeline"] = ["rankingpipeline"],
        ["deletion-queue"] = ["deletionqueue"],
        ["deletion-commit"] = ["deletioncommit"],
        ["indexsort-index"] = ["indexsortindex"],
        ["indexsort-search"] = ["indexsortsearch"],
        ["tokenbudget"] = [],
        ["schemajson"] = [],
        ["blockjoin-index"] = ["blockjoinindex"],
        ["blockjoin-search"] = ["blockjoinsearch"],
        ["gutenberg-index"] = ["gutenbergindex"],
        ["gutenberg-search"] = ["gutenbergsearch"],
        ["multiphrase"] = [],
        ["tv-highlighter"] = ["tvhighlighter"],
        ["searcher-mgr"] = ["searchermgr"],
        ["terminset"] = ["term-in-set"],
        ["query-cache"] = ["querycache"],
        ["function-score"] = ["functionscore"],
        ["collapse-facet"] = ["collapsefacet"],
        ["vq"] = ["vectorquantisation"],
        ["hnsw"] = ["hnsw-search"],
        ["vector-first-touch"] = ["vectorfirsttouch"],
        ["async-index"] = ["asyncindex"]
    };

    private static BenchmarkWorkloadDescriptor Workload(
        string reportName,
        Type benchmarkType,
        bool includeInAll,
        bool includeWithExplicit,
        params string[] suiteIds)
        => new(reportName, benchmarkType, [.. suiteIds], includeInAll, includeWithExplicit);
}
