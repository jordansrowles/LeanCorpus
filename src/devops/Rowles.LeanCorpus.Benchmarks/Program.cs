using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Globalization;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class Program
{
    private static readonly List<PendingSuite> PendingSuites = [];

    public static int Main(string[] args)
    {
        PendingSuites.Clear();

        HashSet<BenchmarkSuite> suites;
        string runType;
        string[] benchmarkArgs;
        bool showHelp;
        int? docCount;
        bool gcDump;

        try
        {
            (suites, runType, benchmarkArgs, showHelp, docCount, gcDump) = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (showHelp)
        {
            PrintHelp();
            return 0;
        }

        // Expose doc count override as env var for [GlobalSetup] to read
        if (docCount is not null)
            Environment.SetEnvironmentVariable("BENCH_DOC_COUNT", docCount.Value.ToString(CultureInfo.InvariantCulture));

        var repoRoot = FindRepositoryRoot();
        var now = DateTimeOffset.UtcNow;

        var machineDir = Path.Combine(repoRoot, "bench", Environment.MachineName);
        var runDir = Path.Combine(
            machineDir,
            now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            now.ToString("HH-mm", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDir);

        var gitCommitHash = GetGitShortHash(repoRoot);
        var sourceCommit = Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT");
        var commitHash = !string.IsNullOrWhiteSpace(sourceCommit)
            ? sourceCommit
            : gitCommitHash;
        var runId = string.IsNullOrEmpty(commitHash)
            ? now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)
            : $"{now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)} ({commitHash})";

        bool runAll = suites.Contains(BenchmarkSuite.All);

        // Expand 'all-with-explicit' into 'all' + 'explicit'.
        if (suites.Contains(BenchmarkSuite.AllWithExplicit))
        {
            suites.Remove(BenchmarkSuite.AllWithExplicit);
            suites.Add(BenchmarkSuite.All);
            suites.Add(BenchmarkSuite.Explicit);
            runAll = true;
        }

        if (suites.Remove(BenchmarkSuite.Explicit))
        {
            suites.UnionWith([
                BenchmarkSuite.TokenBudget,
                BenchmarkSuite.Diagnostics,
                BenchmarkSuite.PackedIntCodec,
                BenchmarkSuite.CodecFrame,
                BenchmarkSuite.CodecFrameRead,
                BenchmarkSuite.CodecMigration,
                BenchmarkSuite.NumericAggregatorSimd,
                BenchmarkSuite.IndexWriterContention,
                BenchmarkSuite.ConcurrentWrite,
                BenchmarkSuite.Merge,
                BenchmarkSuite.Flush,
                BenchmarkSuite.DocValuesRead,
                BenchmarkSuite.BKDTree,
                BenchmarkSuite.FstLookup,
                BenchmarkSuite.MMapIO,
                BenchmarkSuite.HnswSearch,
                BenchmarkSuite.VectorQuantisation,
                BenchmarkSuite.CompoundFile,
                BenchmarkSuite.WindowsFileSystem,
                BenchmarkSuite.IncrementalBackup,
                BenchmarkSuite.ReaderManagerLifecycle,
                BenchmarkSuite.MultiReader,
                BenchmarkSuite.OrdinalMap,
                BenchmarkSuite.SearchSession,
                BenchmarkSuite.RankingEvaluation,
                BenchmarkSuite.RankingPipeline,
            ]);
        }

        // Resolve effective run type for metadata (does not affect output path)
        var effectiveRunType = string.IsNullOrEmpty(runType) ? "full" : runType;

        // Clean up any stale temp directories from previous aborted runs
        // before any suite builds its index.
        BenchmarkHelpers.CleanTempRoot();

        if (UsesStandardSearchFixture(suites, runAll))
        {
            var effectiveDocCount = docCount ?? BenchmarkData.DefaultDocCount;
            Console.WriteLine($"Preparing shared search fixture ({effectiveDocCount:N0} documents)...");
            SharedStandardIndex.PrepareForRun(effectiveDocCount);
        }

        var suiteSummaries = new List<(string Suite, Summary Summary)>();

        if (runAll || suites.Contains(BenchmarkSuite.Query))
            RunSuite<TermQueryBenchmarks>("query", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Index))
            RunSuite<IndexingBenchmarks>("index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Boolean))
            RunSuite<BooleanQueryBenchmarks>("boolean", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Phrase))
            RunSuite<PhraseQueryBenchmarks>("phrase", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Prefix))
            RunSuite<PrefixQueryBenchmarks>("prefix", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Fuzzy))
            RunSuite<FuzzyQueryBenchmarks>("fuzzy", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Wildcard))
            RunSuite<WildcardQueryBenchmarks>("wildcard", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Deletion) || suites.Contains(BenchmarkSuite.DeletionQueue))
            RunSuite<DeletionQueueBenchmarks>("deletion-queue", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Deletion) || suites.Contains(BenchmarkSuite.DeletionCommit))
            RunSuite<DeletionCommitBenchmarks>("deletion-commit", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.TokenBudget))
            RunSuite<TokenBudgetBenchmarks>("tokenbudget", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.Diagnostics))
            RunSuite<DiagnosticsBenchmarks>("diagnostics", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Suggester))
            RunSuite<SuggesterBenchmarks>("suggester", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.SchemaJson))
            RunSuite<SchemaAndJsonBenchmarks>("schemajson", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.IndexSort))
        {
            RunSuite<IndexSortIndexBenchmarks>("indexsort-index", runDir, benchmarkArgs, suiteSummaries, gcDump);
            RunSuite<IndexSortSearchBenchmarks>("indexsort-search", runDir, benchmarkArgs, suiteSummaries, gcDump);
        }

        if (suites.Contains(BenchmarkSuite.IndexSortIndex))
            RunSuite<IndexSortIndexBenchmarks>("indexsort-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.IndexSortSearch))
            RunSuite<IndexSortSearchBenchmarks>("indexsort-search", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.BlockJoin) || suites.Contains(BenchmarkSuite.BlockJoinIndex))
            RunSuite<BlockJoinIndexBenchmarks>("blockjoin-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.BlockJoin) || suites.Contains(BenchmarkSuite.BlockJoinSearch))
            RunSuite<BlockJoinSearchBenchmarks>("blockjoin-search", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergIndex))
            RunSuite<GutenbergIndexingBenchmarks>("gutenberg-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergSearch))
            RunSuite<GutenbergSearchBenchmarks>("gutenberg-search", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Phase 1: query parity
        if (runAll || suites.Contains(BenchmarkSuite.Range))
            RunSuite<RangeQueryBenchmarks>("range", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Regexp))
            RunSuite<RegexpQueryBenchmarks>("regexp", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Dismax))
            RunSuite<DisjunctionMaxQueryBenchmarks>("dismax", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.MultiPhrase))
            RunSuite<MultiPhraseQueryBenchmarks>("multiphrase", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Span))
            RunSuite<SpanQueryBenchmarks>("span", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Phase 2: standalone (no Lucene.NET parity)
        if (runAll || suites.Contains(BenchmarkSuite.MoreLikeThis))
        {
            RunSuite<MoreLikeThisBenchmarks>("mlt", runDir, benchmarkArgs, suiteSummaries, gcDump);
            RunSuite<MoreLikeThisSingleSegmentBenchmarks>("mlt-single-segment", runDir, benchmarkArgs, suiteSummaries, gcDump);
        }

        if (runAll || suites.Contains(BenchmarkSuite.Highlighter))
            RunSuite<HighlighterBenchmarks>("highlighter", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.TvHighlighter))
            RunSuite<TermVectorHighlighterBenchmarks>("tv-highlighter", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.SearcherManager))
            RunSuite<SearcherManagerBenchmarks>("searcher-mgr", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Phase 3: standalone post-Gutenberg
        if (runAll || suites.Contains(BenchmarkSuite.CombinedFields))
            RunSuite<CombinedFieldsQueryBenchmarks>("combined", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.TermInSet))
            RunSuite<TermInSetQueryBenchmarks>("terminset", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Aggregation))
            RunSuite<AggregationBenchmarks>("aggregation", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.QueryCache))
            RunSuite<QueryCacheBenchmarks>("query-cache", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.ParallelSearch))
            RunSuite<ParallelSearchBenchmarks>("parallel", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.FunctionScore))
            RunSuite<FunctionScoreQueryBenchmarks>("function-score", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Geo))
            RunSuite<GeoQueryBenchmarks>("geo", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.CollapseAndFacet))
            RunSuite<CollapseAndFacetBenchmarks>("collapse-facet", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Similarity))
            RunSuite<SimilarityBenchmarks>("similarity", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Phase 4: analysis
        if (runAll || suites.Contains(BenchmarkSuite.AsyncIndex))
            RunSuite<AsyncIndexingBenchmarks>("async-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.VectorQuantisation))
            RunSuite<VectorQuantisationBenchmarks>("vq", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.HnswSearch))
            RunSuite<HnswSearchBenchmarks>("hnsw", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Microbenchmarks — explicit only, not included in --suite all.
        if (suites.Contains(BenchmarkSuite.PackedIntCodec))
            RunSuite<PackedIntCodecBenchmarks>("packed-int-codec", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.CodecFrame))
            RunSuite<CodecFrameBenchmarks>("codec-frame", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.CodecFrameRead))
            RunSuite<CodecFrameReadBenchmarks>("codec-frame-read", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.CodecMigration))
            RunSuite<CodecMigrationBenchmarks>("codec-migration", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.NumericAggregatorSimd))
            RunSuite<NumericAggregatorSimdBenchmarks>("numeric-aggregator", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.IndexWriterContention))
            RunSuite<IndexWriterContentionBenchmarks>("index-writer", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.ConcurrentWrite))
            RunSuite<ConcurrentVsSequentialBenchmarks>("concurrent-write", runDir, benchmarkArgs, suiteSummaries, gcDump);

        // Subsystem benchmarks — explicit only, not included in --suite all.
        if (suites.Contains(BenchmarkSuite.Merge))
            RunSuite<MergeBenchmarks>("merge", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.Flush))
            RunSuite<FlushBenchmarks>("flush", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.DocValuesRead))
            RunSuite<DocValuesReadBenchmarks>("docvalues-read", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.BKDTree))
            RunSuite<BKDTreeBenchmarks>("bkd", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.FstLookup))
            RunSuite<FstLookupBenchmarks>("fst-lookup", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.MMapIO))
            RunSuite<MMapDirectoryIOBenchmarks>("mmap-io", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.CompoundFile))
            RunSuite<CompoundFileBenchmarks>("compound-file", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.WindowsFileSystem))
            RunSuite<WindowsFileSystemBenchmarks>("windows-filesystem", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.IncrementalBackup))
            RunSuite<IncrementalBackupBenchmarks>("incremental-backup", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.ReaderManagerLifecycle))
            RunSuite<ReaderManagerLifecycleBenchmarks>("reader-manager", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.MultiReader))
            RunSuite<MultiReaderBenchmarks>("multi-reader", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.OrdinalMap))
            RunSuite<OrdinalMapBenchmarks>("ordinal-map", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.SearchSession))
            RunSuite<SearchSessionBenchmarks>("search-session", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.RankingEvaluation))
            RunSuite<RankingEvaluationBenchmarks>("ranking-evaluation", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suites.Contains(BenchmarkSuite.RankingPipeline))
            RunSuite<RankingPipelineBenchmarks>("ranking-pipeline", runDir, benchmarkArgs, suiteSummaries, gcDump);

        ExecuteSuites(runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suiteSummaries.Count == 0)
        {
            Console.Error.WriteLine("No benchmark suite selected.");
            return 1;
        }

        // Release shared index resources now that all suites have finished.
        SharedStandardIndex.Cleanup();

        // Non-shared Lucene indexes (one per non-standard suite).
        HnswSearchBenchmarks.CleanupLuceneResources();
        VectorQuantisationBenchmarks.CleanupLuceneResources();
        ParallelSearchBenchmarks.CleanupLuceneResources();

        // Nuke the entire bench/tmp tree so subsequent runs start clean.
        BenchmarkHelpers.CleanTempRoot();

        // Build and write consolidated report + index.json
        var report = BenchmarkRunReportBuilder.Build(
            runId,
            now,
            benchmarkArgs,
            suiteSummaries);
        report.CommitHash = commitHash;
        report.RunType = effectiveRunType;
        report.Provenance = BenchmarkProvenanceBuilder.Build(
            repoRoot,
            gitCommitHash,
            docCount ?? BenchmarkData.DefaultDocCount);
        if (!report.Provenance.RscriptAvailable)
            report.QualityFlags.Add("RscriptUnavailable");

        BenchmarkRunReportWriter.WriteReport(runDir, machineDir, report);

        Console.WriteLine();
        Console.WriteLine($"Run:    {runId}");
        Console.WriteLine($"Type:   {effectiveRunType}");
        Console.WriteLine($"Commit: {(string.IsNullOrEmpty(commitHash) ? "(unknown)" : commitHash)}");
        Console.WriteLine($"Output: {runDir}");
        Console.WriteLine($"Suites: {string.Join(", ", suiteSummaries.Select(s => s.Suite))}");
        return 0;
    }

    private static void RunSuite<T>(
        string suiteName,
        string runDir,
        string[] benchmarkArgs,
        List<(string Suite, Summary Summary)> suiteSummaries,
        bool gcDump = false) where T : class
    {
        if (PendingSuites.All(suite => suite.Type != typeof(T)))
            PendingSuites.Add(new PendingSuite(suiteName, typeof(T)));
    }

    private static void ExecuteSuites(
        string runDir,
        string[] benchmarkArgs,
        List<(string Suite, Summary Summary)> suiteSummaries,
        bool gcDump)
    {
        if (PendingSuites.Count == 0)
            return;

        var artifactsPath = Path.Combine(runDir, "_runner");
        Directory.CreateDirectory(artifactsPath);
        var baseConfig = DefaultConfig.Instance.WithArtifactsPath(artifactsPath);
        var (job, effectiveBenchmarkArgs) = ExtractJob(benchmarkArgs);

        // BDN nightly >= 20260608 goes interactive when args are empty,
        // even when types are supplied via FromTypes. Inject a filter to
        // select all benchmarks from the supplied types without prompting.
        if (!HasBenchmarkDotNetOption(effectiveBenchmarkArgs, "--filter", "-f"))
            effectiveBenchmarkArgs = [.. effectiveBenchmarkArgs, "--filter", "*"];

        var backupSuite = PendingSuites.SingleOrDefault(
            suite => suite.Type == typeof(IncrementalBackupBenchmarks));
        var regularSuites = PendingSuites
            .Where(suite => suite.Type != typeof(IncrementalBackupBenchmarks))
            .ToArray();

        if (regularSuites.Length > 0)
        {
            var regularConfig = baseConfig;
            if (job is not null)
            {
                // The benchmark project is multi-targeted. Serialise the generated
                // project build so cross-target reference discovery cannot race the
                // two target-framework builds on hosts with a single benchmark job.
                regularConfig = regularConfig
                    .AddJob(job.WithMsBuildArguments("-m:1"))
                    .WithUnionRule(ConfigUnionRule.AlwaysUseGlobal);
            }
            if (gcDump)
                regularConfig = regularConfig.AddDiagnoser(new GcDumpDiagnoser());

            var summaries = BenchmarkSwitcher
                .FromTypes([.. regularSuites.Select(suite => suite.Type)])
                .Run(effectiveBenchmarkArgs, regularConfig);
            RecordSuiteSummaries(
                summaries, regularSuites, artifactsPath, runDir, suiteSummaries);
        }

        if (backupSuite is not null)
        {
            // Durable backup operations must not use BenchmarkDotNet's throughput
            // pilot, which can invoke a multi-minute operation repeatedly before
            // measurement. Monitoring performs exactly three direct samples.
            var backupJob = (job ?? Job.Default)
                .WithStrategy(RunStrategy.Monitoring)
                .WithLaunchCount(1)
                .WithWarmupCount(0)
                .WithIterationCount(3)
                .WithInvocationCount(1)
                .WithUnrollFactor(1)
                .WithMsBuildArguments("-m:1");
            var backupConfig = baseConfig
                .AddJob(backupJob)
                .WithUnionRule(ConfigUnionRule.AlwaysUseGlobal);
            if (gcDump)
                backupConfig = backupConfig.AddDiagnoser(new GcDumpDiagnoser());

            Console.WriteLine("[incremental-backup] Preparing shared loose and compound fixtures...");
            var fixtureStopwatch = Stopwatch.StartNew();
            using var sharedBackupFixtures = IncrementalBackupBenchmarks.PrepareSharedFixtures();
            fixtureStopwatch.Stop();
            Console.WriteLine(
                $"[incremental-backup] Shared fixtures prepared in {fixtureStopwatch.Elapsed:g}.");
            var summaries = BenchmarkSwitcher
                .FromTypes([backupSuite.Type])
                .Run(effectiveBenchmarkArgs, backupConfig);
            RecordSuiteSummaries(
                summaries, [backupSuite], artifactsPath, runDir, suiteSummaries);
        }
    }

    private static void RecordSuiteSummaries(
        IEnumerable<Summary> summaries,
        IReadOnlyList<PendingSuite> pendingSuites,
        string artifactsPath,
        string runDir,
        List<(string Suite, Summary Summary)> suiteSummaries)
    {
        foreach (var summary in summaries)
        {
            var benchmarkType = summary.BenchmarksCases.First().Descriptor.Type;
            var suite = pendingSuites.First(item => item.Type == benchmarkType);
            suiteSummaries.Add((suite.Name, summary));
            CopySuiteArtifacts(artifactsPath, runDir, suite);
        }
    }

    private static (Job? Job, string[] Arguments) ExtractJob(string[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            string? value = null;
            var consumed = 1;

            if ((string.Equals(arguments[i], "--job", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(arguments[i], "-j", StringComparison.OrdinalIgnoreCase))
                && i + 1 < arguments.Length)
            {
                value = arguments[i + 1];
                consumed = 2;
            }
            else if (arguments[i].StartsWith("--job=", StringComparison.OrdinalIgnoreCase))
            {
                value = arguments[i]["--job=".Length..];
            }
            else if (arguments[i].StartsWith("-j=", StringComparison.OrdinalIgnoreCase))
            {
                value = arguments[i]["-j=".Length..];
            }

            var job = value?.ToLowerInvariant() switch
            {
                "dry" => Job.Dry,
                "short" => Job.ShortRun,
                "default" => Job.Default,
                _ => null,
            };

            if (job is null)
                continue;

            var remaining = new string[arguments.Length - consumed];
            if (i > 0)
                Array.Copy(arguments, 0, remaining, 0, i);
            if (i + consumed < arguments.Length)
            {
                Array.Copy(
                    arguments,
                    i + consumed,
                    remaining,
                    i,
                    arguments.Length - i - consumed);
            }

            return (job, remaining);
        }

        return (null, arguments);
    }

    private static void CopySuiteArtifacts(
        string artifactsPath,
        string runDir,
        PendingSuite suite)
    {
        var suitePath = Path.Combine(runDir, suite.Name);
        var suiteResultsPath = Path.Combine(suitePath, "results");
        Directory.CreateDirectory(suiteResultsPath);

        var runnerResultsPath = Path.Combine(artifactsPath, "results");
        if (Directory.Exists(runnerResultsPath))
        {
            foreach (var file in Directory.EnumerateFiles(
                         runnerResultsPath,
                         $"*{suite.Type.Name}*",
                         SearchOption.TopDirectoryOnly))
            {
                File.Copy(
                    file,
                    Path.Combine(suiteResultsPath, Path.GetFileName(file)),
                    overwrite: true);
            }
        }

        foreach (var file in Directory.EnumerateFiles(
                     artifactsPath,
                     $"*{suite.Type.Name}*.log",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(suitePath, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static bool UsesStandardSearchFixture(
        HashSet<BenchmarkSuite> suites,
        bool runAll)
    {
        if (runAll)
            return true;

        return suites.Overlaps(
        [
            BenchmarkSuite.Query,
            BenchmarkSuite.Boolean,
            BenchmarkSuite.Phrase,
            BenchmarkSuite.Prefix,
            BenchmarkSuite.Fuzzy,
            BenchmarkSuite.Wildcard,
            BenchmarkSuite.Regexp,
            BenchmarkSuite.Dismax,
            BenchmarkSuite.MultiPhrase,
            BenchmarkSuite.Span,
            BenchmarkSuite.SearcherManager,
            BenchmarkSuite.TermInSet,
            BenchmarkSuite.QueryCache,
            BenchmarkSuite.Similarity,
        ]);
    }

    private static string GetGitShortHash(string repoRoot)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short HEAD",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            LeanCorpus Benchmark Runner

            Usage:
              dotnet run -c Release --project <path> -- [options] [-- BenchmarkDotNet args]

            Options:
              --suite <name>   Run one or more benchmark suites, comma-separated (default: all)
                               e.g. --suite fuzzy,boolean,prefix
              --type <name>    Run type label stored in report metadata (default: full)
              --doccount <n>   Override document count for all suites (env: BENCH_DOC_COUNT)
              --gcdump         Collect GC heap dumps after each benchmark run
              --corpus-only    Skip Lucene.NET comparison benchmarks, run LeanCorpus only
              --help, -h       Show this help message

            Suites:
              all              Run all primary benchmark suites, including Gutenberg (default)
              all-with-explicit  Run all primary plus all explicit-only suites
              explicit         Run all explicit-only suites, including subsystem and recent-feature benchmarks
              index            IndexingBenchmarks -- bulk indexing throughput (vs Lucene.NET)
              query            TermQueryBenchmarks -- single-term search (vs Lucene.NET)
              boolean          BooleanQueryBenchmarks -- deterministic clause shapes
              phrase           PhraseQueryBenchmarks -- exact and slop phrase matching
              prefix           PrefixQueryBenchmarks -- prefix matching (vs Lucene.NET)
              fuzzy            FuzzyQueryBenchmarks -- deterministic fuzzy/edit-distance scenarios
              wildcard         WildcardQueryBenchmarks -- wildcard pattern matching
              deletion         DeletionQueue/CommitBenchmarks -- delete queueing and commit application
              deletion-queue   DeletionQueueBenchmarks -- enqueue delete terms
              deletion-commit  DeletionCommitBenchmarks -- apply queued deletes on commit
              suggester        SuggesterBenchmarks -- DidYouMean spelling correction (vs Lucene.NET)
              schemajson       SchemaAndJsonBenchmarks -- schema validation + JSON mapping
              indexsort        IndexSortIndex/SearchBenchmarks -- index-time sort + sorted search
              blockjoin        BlockJoinIndex/SearchBenchmarks -- block-join indexing and query hot path
              blockjoin-index  BlockJoinIndexBenchmarks -- block-join indexing
              blockjoin-search BlockJoinSearchBenchmarks -- block-join query hot path

              gutenberg-index     GutenbergIndexingBenchmarks -- indexing real ebook data
              gutenberg-search    GutenbergSearchBenchmarks -- search on real ebook data
              range               RangeQueryBenchmarks -- BKD range queries
              regexp              RegexpQueryBenchmarks -- regexp query parity
              dismax              DisjunctionMaxQueryBenchmarks -- disjunction max parity
              multiphrase         MultiPhraseQueryBenchmarks -- multi-slot phrase parity
              span                SpanQueryBenchmarks -- span query parity
              mlt                 MoreLikeThisBenchmarks and MoreLikeThisSingleSegmentBenchmarks -- MoreLikeThis query
              highlighter         HighlighterBenchmarks -- snippet highlighting
              searcher-mgr        SearcherManagerBenchmarks -- acquire/release hot path
              combined            CombinedFieldsQueryBenchmarks -- BM25F multi-field search
              terminset           TermInSetQueryBenchmarks -- set membership search
              aggregation         AggregationBenchmarks -- aggregation overhead
              query-cache         QueryCacheBenchmarks -- query cache overhead
              parallel            ParallelSearchBenchmarks -- parallel search
              function-score      FunctionScoreQueryBenchmarks -- function score modes
              geo                 GeoQueryBenchmarks -- geo distance and bounding-box search
              collapse-facet      CollapseAndFacetBenchmarks -- collapse and facet collection
              similarity          SimilarityBenchmarks -- BM25 vs TF-IDF
              async-index         AsyncIndexingBenchmarks -- sync vs async indexing
              vq                  VectorQuantisationBenchmarks -- HNSW search with vector quantisation (vs Lucene.NET flat scan)
              hnsw                HnswSearchBenchmarks -- HNSW graph search vs flat scan (vs Lucene.NET baseline)
              tokenbudget         TokenBudgetBenchmarks -- token budget enforcement overhead (explicit only)
              diagnostics         DiagnosticsBenchmarks -- SlowQueryLog + Analytics hook overhead (explicit only)
              packed-int-codec    PackedIntCodecBenchmarks -- Pack/Unpack scalar loop throughput (explicit only)
              codec-frame         CodecFrameBenchmarks -- frame write and checksum cost (explicit only)
              codec-frame-read    CodecFrameReadBenchmarks -- frame open and checksum validation costs (explicit only)
              codec-migration     CodecMigrationBenchmarks -- streamed migration throughput and allocation (explicit only)
              numeric-aggregator  NumericAggregatorSimdBenchmarks -- scalar vs Vector256 aggregation (explicit only)
              index-writer        IndexWriterContentionBenchmarks -- concurrent AddDocument throughput (explicit only)
              concurrent-write    ConcurrentVsSequentialBenchmarks -- DWPT parallel vs sequential indexing (explicit only)

              merge               MergeBenchmarks -- segment merge throughput (explicit only)
              flush               FlushBenchmarks -- segment flush latency per doc count (explicit only)
              docvalues-read      DocValuesReadBenchmarks -- DocValues read throughput (explicit only)
              bkd                 BKDTreeBenchmarks -- BKD range search throughput (explicit only)
              fst-lookup          FstLookupBenchmarks -- FST term dictionary lookup (explicit only)
              mmap-io             MMapDirectoryIOBenchmarks -- raw I/O throughput (explicit only)
              compound-file       CompoundFileBenchmarks -- loose files vs compound segment storage (explicit only)
              windows-filesystem  WindowsFileSystemBenchmarks -- durability and compound-file matrix (explicit only)
              incremental-backup  IncrementalBackupBenchmarks -- full and parent-linked backup operations (explicit only)
              reader-manager      ReaderManagerLifecycleBenchmarks -- generic reader lifecycle overhead (explicit only)
              multi-reader        MultiReaderBenchmarks -- federated search and pagination (explicit only)
              ordinal-map         OrdinalMapBenchmarks -- global ordinal construction and lookup (explicit only)
              search-session      SearchSessionBenchmarks -- stable cursor pagination and session lifecycle (explicit only)
              ranking-evaluation  RankingEvaluationBenchmarks -- IR metrics and MMR diversification (explicit only)
              ranking-pipeline    RankingPipelineBenchmarks -- profiles, rules and bounded reranking (explicit only)

            Output:
              Results are written to bench/{machine-name}/{yyyy-MM-dd}/{HH-mm}/
              A consolidated JSON report and per-machine index.json are maintained.

            Examples:
              dotnet run -c Release -- --suite all
              dotnet run -c Release -- --suite gutenberg-search
              dotnet run -c Release -- --corpus-only --suite query,index
              dotnet run -c Release -- --type smoke --suite analysis --job dry

            Script wrapper:
              .\scripts\benchmark.ps1 -Suite all
              .\scripts\benchmark.ps1 -Suite query -CorpusOnly
              .\scripts\benchmark.ps1 -Suite gutenberg-search
              .\scripts\benchmark.ps1 -Help
            """);
    }

    private static (HashSet<BenchmarkSuite> Suites, string RunType, string[] BenchmarkArgs, bool ShowHelp, int? DocCount, bool GcDump) ParseArguments(string[] args)
    {
        var suites = new HashSet<BenchmarkSuite> { BenchmarkSuite.All };
        var benchmarkArgs = new List<string>(args.Length);
        var showHelp = false;
        int? docCount = null;
        string runType = string.Empty;
        bool gcDump = false;
        bool corpusOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (string.Equals(args[i], "--", StringComparison.Ordinal))
            {
                benchmarkArgs.AddRange(args[(i + 1)..]);
                break;
            }

            if (string.Equals(args[i], "--gcdump", StringComparison.OrdinalIgnoreCase))
            {
                gcDump = true;
                continue;
            }

            if (string.Equals(args[i], "--corpus-only", StringComparison.OrdinalIgnoreCase))
            {
                corpusOnly = true;
                continue;
            }

            if (string.Equals(args[i], "--suite", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                suites = ParseSuites(args[++i]);
                continue;
            }

            if (string.Equals(args[i], "--type", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                runType = args[++i].ToLowerInvariant();
                continue;
            }

            if (string.Equals(args[i], "--doccount", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc))
                    docCount = dc;
                continue;
            }

            benchmarkArgs.Add(args[i]);
        }

        // Inject BDN filter to exclude Lucene.NET benchmarks unless a caller supplied a more specific BDN filter.
        if (corpusOnly && !HasBenchmarkDotNetOption(benchmarkArgs, "--filter", "-f"))
            benchmarkArgs.AddRange(["--filter", "*LeanCorpus_*"]);

        return (suites, runType, [.. benchmarkArgs], showHelp, docCount, gcDump);
    }

    private static bool HasBenchmarkDotNetOption(IEnumerable<string> args, params string[] names)
    {
        foreach (var arg in args)
        {
            foreach (var name in names)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static HashSet<BenchmarkSuite> ParseSuites(string value)
    {
        var result = new HashSet<BenchmarkSuite>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(ParseSingleSuite(part));
        }
        return result;
    }

    private static BenchmarkSuite ParseSingleSuite(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "all" => BenchmarkSuite.All,
            "all-with-explicit" or "allwithexplicit" => BenchmarkSuite.AllWithExplicit,
            "explicit" => BenchmarkSuite.Explicit,
            "index" => BenchmarkSuite.Index,
            "query" => BenchmarkSuite.Query,
            "packedintcodec" or "packed-int-codec" => BenchmarkSuite.PackedIntCodec,
            "codecframe" or "codec-frame" => BenchmarkSuite.CodecFrame,
            "codecframeread" or "codec-frame-read" => BenchmarkSuite.CodecFrameRead,
            "codecmigration" or "codec-migration" => BenchmarkSuite.CodecMigration,
            "numericaggregator" or "numeric-aggregator" => BenchmarkSuite.NumericAggregatorSimd,
            "indexwriter" or "index-writer" => BenchmarkSuite.IndexWriterContention,
            "concurrentwrite" or "concurrent-write" => BenchmarkSuite.ConcurrentWrite,
            "merge" => BenchmarkSuite.Merge,
            "flush" => BenchmarkSuite.Flush,
            "docvalues-read" or "docvaluesread" => BenchmarkSuite.DocValuesRead,
            "bkd" or "bkd-tree" => BenchmarkSuite.BKDTree,
            "fst-lookup" or "fstlookup" => BenchmarkSuite.FstLookup,
            "mmap-io" or "mmapio" => BenchmarkSuite.MMapIO,
            "compound-file" or "compoundfile" => BenchmarkSuite.CompoundFile,
            "windows-filesystem" or "windowsfilesystem" => BenchmarkSuite.WindowsFileSystem,
            "incremental-backup" or "incrementalbackup" => BenchmarkSuite.IncrementalBackup,
            "reader-manager" or "readermanager" => BenchmarkSuite.ReaderManagerLifecycle,
            "multi-reader" or "multireader" => BenchmarkSuite.MultiReader,
            "ordinal-map" or "ordinalmap" => BenchmarkSuite.OrdinalMap,
            "search-session" or "searchsession" => BenchmarkSuite.SearchSession,
            "ranking-evaluation" or "rankingevaluation" => BenchmarkSuite.RankingEvaluation,
            "ranking-pipeline" or "rankingpipeline" => BenchmarkSuite.RankingPipeline,
            "boolean" => BenchmarkSuite.Boolean,
            "phrase" => BenchmarkSuite.Phrase,
            "prefix" => BenchmarkSuite.Prefix,
            "fuzzy" => BenchmarkSuite.Fuzzy,
            "wildcard" => BenchmarkSuite.Wildcard,
            "deletion" => BenchmarkSuite.Deletion,
            "deletionqueue" or "deletion-queue" => BenchmarkSuite.DeletionQueue,
            "deletioncommit" or "deletion-commit" => BenchmarkSuite.DeletionCommit,
            "indexsort" => BenchmarkSuite.IndexSort,
            "indexsortindex" or "indexsort-index" => BenchmarkSuite.IndexSortIndex,
            "indexsortsearch" or "indexsort-search" => BenchmarkSuite.IndexSortSearch,
            "tokenbudget" => BenchmarkSuite.TokenBudget,
            "diagnostics" => BenchmarkSuite.Diagnostics,
            "suggester" => BenchmarkSuite.Suggester,
            "schemajson" => BenchmarkSuite.SchemaJson,
            "blockjoin" => BenchmarkSuite.BlockJoin,
            "blockjoinindex" or "blockjoin-index" => BenchmarkSuite.BlockJoinIndex,
            "blockjoinsearch" or "blockjoin-search" => BenchmarkSuite.BlockJoinSearch,
            "gutenbergindex" or "gutenberg-index" => BenchmarkSuite.GutenbergIndex,
            "gutenbergsearch" or "gutenberg-search" => BenchmarkSuite.GutenbergSearch,
            "range" => BenchmarkSuite.Range,
            "regexp" => BenchmarkSuite.Regexp,
            "dismax" => BenchmarkSuite.Dismax,
            "multiphrase" => BenchmarkSuite.MultiPhrase,
            "span" => BenchmarkSuite.Span,
            "mlt" => BenchmarkSuite.MoreLikeThis,
            "highlighter" => BenchmarkSuite.Highlighter,
            "tv-highlighter" or "tvhighlighter" => BenchmarkSuite.TvHighlighter,
            "searcher-mgr" or "searchermgr" => BenchmarkSuite.SearcherManager,
            "combined" => BenchmarkSuite.CombinedFields,
            "terminset" or "term-in-set" => BenchmarkSuite.TermInSet,
            "aggregation" => BenchmarkSuite.Aggregation,
            "query-cache" or "querycache" => BenchmarkSuite.QueryCache,
            "parallel" => BenchmarkSuite.ParallelSearch,
            "function-score" or "functionscore" => BenchmarkSuite.FunctionScore,
            "geo" => BenchmarkSuite.Geo,
            "collapse-facet" or "collapsefacet" => BenchmarkSuite.CollapseAndFacet,
            "similarity" => BenchmarkSuite.Similarity,
            "vectorquantisation" or "vq" => BenchmarkSuite.VectorQuantisation,
            "hnsw" or "hnsw-search" => BenchmarkSuite.HnswSearch,
            "async-index" or "asyncindex" => BenchmarkSuite.AsyncIndex,
            _ => throw new ArgumentException($"Unknown benchmark suite '{value}'. Use --help to list available suites.")
        };
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "Rowles.LeanCorpus.slnx");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private enum BenchmarkSuite
    {
        All,
        AllWithExplicit,
        Explicit,
        Index,
        Query,
        Boolean,
        Phrase,
        Prefix,
        Fuzzy,
        Wildcard,
        Deletion,
        DeletionQueue,
        DeletionCommit,
        TokenBudget,
        Diagnostics,
        Suggester,
        SchemaJson,
        IndexSort,
        IndexSortIndex,
        IndexSortSearch,
        BlockJoin,
        BlockJoinIndex,
        BlockJoinSearch,
        GutenbergIndex,
        GutenbergSearch,
        Range,
        Regexp,
        Dismax,
        MultiPhrase,
        Span,
        MoreLikeThis,
        Highlighter,
        TvHighlighter,
        SearcherManager,
        CombinedFields,
        TermInSet,
        Aggregation,
        QueryCache,
        ParallelSearch,
        FunctionScore,
        Geo,
        CollapseAndFacet,
        Similarity,
        AsyncIndex,
        VectorQuantisation,
        HnswSearch,
        PackedIntCodec,
        CodecFrame,
        CodecFrameRead,
        CodecMigration,
        NumericAggregatorSimd,
        IndexWriterContention,
        ConcurrentWrite,
        Merge,
        Flush,
        DocValuesRead,
        BKDTree,
        FstLookup,
        MMapIO,
        CompoundFile,
        WindowsFileSystem,
        IncrementalBackup,
        ReaderManagerLifecycle,
        MultiReader,
        OrdinalMap,
        SearchSession,
        RankingEvaluation,
        RankingPipeline,
    }

    private sealed record PendingSuite(string Name, Type Type);
}
