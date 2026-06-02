using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Globalization;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
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

        // Resolve effective run type for metadata (does not affect output path)
        var effectiveRunType = string.IsNullOrEmpty(runType) ? "full" : runType;

        var suiteSummaries = new List<(string Suite, Summary Summary)>();

        if (runAll || suites.Contains(BenchmarkSuite.Query))
            RunSuite<TermQueryBenchmarks>("query", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Index))
            RunSuite<IndexingBenchmarks>("index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Analysis))
            RunSuite<AnalysisBenchmarks>("analysis", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.AnalysisParity))
            RunSuite<AnalyserParityBenchmarks>("analysis-parity", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.AnalysisFilters))
            RunSuite<TokenFilterBenchmarks>("analysis-filters", runDir, benchmarkArgs, suiteSummaries, gcDump);

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

        if (runAll || suites.Contains(BenchmarkSuite.BlockJoin) || suites.Contains(BenchmarkSuite.BlockJoinIndex))
            RunSuite<BlockJoinIndexBenchmarks>("blockjoin-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.BlockJoin) || suites.Contains(BenchmarkSuite.BlockJoinSearch))
            RunSuite<BlockJoinSearchBenchmarks>("blockjoin-search", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergAnalysis))
            RunSuite<GutenbergAnalysisBenchmarks>("gutenberg-analysis", runDir, benchmarkArgs, suiteSummaries, gcDump);

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
            RunSuite<MoreLikeThisBenchmarks>("mlt", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Highlighter))
            RunSuite<HighlighterBenchmarks>("highlighter", runDir, benchmarkArgs, suiteSummaries, gcDump);

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
        if (runAll || suites.Contains(BenchmarkSuite.Stemmer))
            RunSuite<StemmerParityBenchmarks>("stemmer", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.KStemmer))
            RunSuite<KStemmerParityBenchmarks>("kstemmer", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.LightEnglish))
            RunSuite<LightEnglishStemmerBenchmarks>("lightenglish", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Hunspell))
            RunSuite<HunspellBenchmarks>("hunspell", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.NGram))
            RunSuite<NGramTokeniserBenchmarks>("ngram", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.Synonym))
            RunSuite<SynonymBenchmarks>("synonym", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.AsyncIndex))
            RunSuite<AsyncIndexingBenchmarks>("async-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.VectorQuantisation))
            RunSuite<VectorQuantisationBenchmarks>("vq", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (suiteSummaries.Count == 0)
        {
            Console.Error.WriteLine("No benchmark suite selected.");
            return 1;
        }

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
        var artifactsPath = Path.Combine(runDir, suiteName);
        Directory.CreateDirectory(artifactsPath);
        var config = DefaultConfig.Instance.WithArtifactsPath(artifactsPath);
        if (gcDump)
            config = config.AddDiagnoser(new GcDumpDiagnoser());
        var summary = BenchmarkRunner.Run<T>(config, benchmarkArgs);
        suiteSummaries.Add((suiteName, summary));
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
              index            IndexingBenchmarks -- bulk indexing throughput (vs Lucene.NET)
              query            TermQueryBenchmarks -- single-term search (vs Lucene.NET)
              analysis         AnalysisBenchmarks -- tokenisation pipeline throughput
              analysis-parity  AnalyserParityBenchmarks -- lightweight analyser parity throughput
              analysis-filters TokenFilterBenchmarks -- token filter allocation and throughput
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

              gutenberg-analysis  GutenbergAnalysisBenchmarks -- analysis on real ebook text
              gutenberg-index     GutenbergIndexingBenchmarks -- indexing real ebook data
              gutenberg-search    GutenbergSearchBenchmarks -- search on real ebook data
              range               RangeQueryBenchmarks -- BKD range queries
              regexp              RegexpQueryBenchmarks -- regexp query parity
              dismax              DisjunctionMaxQueryBenchmarks -- disjunction max parity
              multiphrase         MultiPhraseQueryBenchmarks -- multi-slot phrase parity
              span                SpanQueryBenchmarks -- span query parity
              mlt                 MoreLikeThisBenchmarks -- MoreLikeThis query
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
              stemmer             StemmerParityBenchmarks -- stemming parity
              kstemmer            KStemmerParityBenchmarks -- Krovetz stemmer parity (vs Lucene.NET KStemmer)
              lightenglish        LightEnglishStemmerBenchmarks -- LightEnglish vs Porter throughput
              hunspell            HunspellBenchmarks -- Hunspell parse and stem throughput
              ngram               NGramTokeniserBenchmarks -- N-gram tokenisation
              synonym             SynonymBenchmarks -- synonym indexing overhead
              async-index         AsyncIndexingBenchmarks -- sync vs async indexing
              tokenbudget         TokenBudgetBenchmarks -- token budget enforcement overhead (explicit only)
              diagnostics         DiagnosticsBenchmarks -- SlowQueryLog + Analytics hook overhead (explicit only)

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
            "index" => BenchmarkSuite.Index,
            "query" => BenchmarkSuite.Query,
            "analysis" => BenchmarkSuite.Analysis,
            "analysisparity" or "analysis-parity" => BenchmarkSuite.AnalysisParity,
            "analysisfilters" or "analysis-filters" => BenchmarkSuite.AnalysisFilters,
            "boolean" => BenchmarkSuite.Boolean,
            "phrase" => BenchmarkSuite.Phrase,
            "prefix" => BenchmarkSuite.Prefix,
            "fuzzy" => BenchmarkSuite.Fuzzy,
            "wildcard" => BenchmarkSuite.Wildcard,
            "deletion" => BenchmarkSuite.Deletion,
            "deletionqueue" or "deletion-queue" => BenchmarkSuite.DeletionQueue,
            "deletioncommit" or "deletion-commit" => BenchmarkSuite.DeletionCommit,
            "tokenbudget" => BenchmarkSuite.TokenBudget,
            "diagnostics" => BenchmarkSuite.Diagnostics,
            "suggester" => BenchmarkSuite.Suggester,
            "schemajson" => BenchmarkSuite.SchemaJson,
            "indexsort" => BenchmarkSuite.IndexSort,
            "blockjoin" => BenchmarkSuite.BlockJoin,
            "blockjoinindex" or "blockjoin-index" => BenchmarkSuite.BlockJoinIndex,
            "blockjoinsearch" or "blockjoin-search" => BenchmarkSuite.BlockJoinSearch,
            "gutenberganalysis" or "gutenberg-analysis" => BenchmarkSuite.GutenbergAnalysis,
            "gutenbergindex" or "gutenberg-index" => BenchmarkSuite.GutenbergIndex,
            "gutenbergsearch" or "gutenberg-search" => BenchmarkSuite.GutenbergSearch,
            "range" => BenchmarkSuite.Range,
            "regexp" => BenchmarkSuite.Regexp,
            "dismax" => BenchmarkSuite.Dismax,
            "multiphrase" => BenchmarkSuite.MultiPhrase,
            "span" => BenchmarkSuite.Span,
            "mlt" => BenchmarkSuite.MoreLikeThis,
            "highlighter" => BenchmarkSuite.Highlighter,
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
            "stemmer" => BenchmarkSuite.Stemmer,
            "kstemmer" => BenchmarkSuite.KStemmer,
            "lightenglish" or "light-english" => BenchmarkSuite.LightEnglish,
            "hunspell" => BenchmarkSuite.Hunspell,
            "vectorquantisation" or "vq" => BenchmarkSuite.VectorQuantisation,
            "ngram" => BenchmarkSuite.NGram,
            "synonym" => BenchmarkSuite.Synonym,
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
        Index,
        Query,
        Analysis,
        AnalysisParity,
        AnalysisFilters,
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
        BlockJoin,
        BlockJoinIndex,
        BlockJoinSearch,
        GutenbergAnalysis,
        GutenbergIndex,
        GutenbergSearch,
        Range,
        Regexp,
        Dismax,
        MultiPhrase,
        Span,
        MoreLikeThis,
        Highlighter,
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
        Stemmer,
        KStemmer,
        LightEnglish,
        Hunspell,
        NGram,
        Synonym,
        AsyncIndex,
        VectorQuantisation,
    }
}
