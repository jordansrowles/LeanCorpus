using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Globalization;

namespace Rowles.LeanLucene.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        var (suites, runType, benchmarkArgs, showHelp, docCount, gcDump) = ParseArguments(args);

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

        if (runAll || suites.Contains(BenchmarkSuite.Deletion))
            RunSuite<DeletionBenchmarks>("deletion", runDir, benchmarkArgs, suiteSummaries, gcDump);

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

        if (runAll || suites.Contains(BenchmarkSuite.BlockJoin))
            RunSuite<BlockJoinBenchmarks>("blockjoin", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergAnalysis))
            RunSuite<GutenbergAnalysisBenchmarks>("gutenberg-analysis", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergIndex))
            RunSuite<GutenbergIndexingBenchmarks>("gutenberg-index", runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (runAll || suites.Contains(BenchmarkSuite.GutenbergSearch))
            RunSuite<GutenbergSearchBenchmarks>("gutenberg-search", runDir, benchmarkArgs, suiteSummaries, gcDump);

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
            LeanLucene Benchmark Runner

            Usage:
              dotnet run -c Release --project <path> -- [options] [-- BenchmarkDotNet args]

            Options:
              --suite <name>   Run one or more benchmark suites, comma-separated (default: all)
                               e.g. --suite fuzzy,boolean,prefix
              --type <name>    Run type label stored in report metadata (default: full)
              --doccount <n>   Override document count for all suites (env: BENCH_DOC_COUNT)
              --gcdump         Collect GC heap dumps after each benchmark run
              --lean-only      Skip Lucene.NET comparison benchmarks, run LeanLucene only
              --help, -h       Show this help message

            Suites:
              all              Run all primary benchmark suites, including Gutenberg (default)
              index            IndexingBenchmarks -- bulk indexing throughput (vs Lucene.NET)
              query            TermQueryBenchmarks -- single-term search (vs Lucene.NET)
              analysis         AnalysisBenchmarks -- tokenisation pipeline throughput
              analysis-parity  AnalyserParityBenchmarks -- lightweight analyser parity throughput
              analysis-filters TokenFilterBenchmarks -- token filter allocation and throughput
              boolean          BooleanQueryBenchmarks -- Must/Should/MustNot queries
              phrase           PhraseQueryBenchmarks -- exact and slop phrase matching
              prefix           PrefixQueryBenchmarks -- prefix matching (vs Lucene.NET)
              fuzzy            FuzzyQueryBenchmarks -- fuzzy/edit-distance matching
              wildcard         WildcardQueryBenchmarks -- wildcard pattern matching
              deletion         DeletionBenchmarks -- delete throughput (vs Lucene.NET)
              suggester        SuggesterBenchmarks -- DidYouMean spelling correction (vs Lucene.NET)
              schemajson       SchemaAndJsonBenchmarks -- schema validation + JSON mapping
              indexsort        IndexSortIndex/SearchBenchmarks -- index-time sort + sorted search
              blockjoin        BlockJoinBenchmarks -- block-join queries (vs Lucene.NET)

              gutenberg-analysis  GutenbergAnalysisBenchmarks -- analysis on real ebook text
              gutenberg-index     GutenbergIndexingBenchmarks -- indexing real ebook data
              gutenberg-search    GutenbergSearchBenchmarks -- search on real ebook data
              tokenbudget         TokenBudgetBenchmarks -- token budget enforcement overhead (explicit only)
              diagnostics         DiagnosticsBenchmarks -- SlowQueryLog + Analytics hook overhead (explicit only)

            Output:
              Results are written to bench/{machine-name}/{yyyy-MM-dd}/{HH-mm}/
              A consolidated JSON report and per-machine index.json are maintained.

            Examples:
              dotnet run -c Release -- --suite all
              dotnet run -c Release -- --suite gutenberg-search
              dotnet run -c Release -- --lean-only --suite query,index
              dotnet run -c Release -- --type smoke --suite analysis --job dry

            Script wrapper:
              .\scripts\benchmark.ps1 -Suite all
              .\scripts\benchmark.ps1 -Suite query -LeanOnly
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
        bool leanOnly = false;

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

            if (string.Equals(args[i], "--lean-only", StringComparison.OrdinalIgnoreCase))
            {
                leanOnly = true;
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
        if (leanOnly && !HasBenchmarkDotNetOption(benchmarkArgs, "--filter", "-f"))
            benchmarkArgs.AddRange(["--filter", "*LeanLucene*"]);

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
        if (value.Equals("index", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Index;
        if (value.Equals("query", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Query;
        if (value.Equals("analysis", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Analysis;
        if (value.Equals("analysisparity", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("analysis-parity", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.AnalysisParity;
        if (value.Equals("analysisfilters", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("analysis-filters", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.AnalysisFilters;
        if (value.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Boolean;
        if (value.Equals("phrase", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Phrase;
        if (value.Equals("prefix", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Prefix;
        if (value.Equals("fuzzy", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Fuzzy;
        if (value.Equals("wildcard", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Wildcard;
        if (value.Equals("deletion", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Deletion;
        if (value.Equals("tokenbudget", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.TokenBudget;
        if (value.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Diagnostics;
        if (value.Equals("suggester", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.Suggester;
        if (value.Equals("schemajson", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.SchemaJson;
        if (value.Equals("indexsort", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.IndexSort;
        if (value.Equals("blockjoin", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.BlockJoin;
        if (value.Equals("gutenberganalysis", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("gutenberg-analysis", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.GutenbergAnalysis;
        if (value.Equals("gutenbergindex", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("gutenberg-index", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.GutenbergIndex;
        if (value.Equals("gutenbergsearch", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("gutenberg-search", StringComparison.OrdinalIgnoreCase))
            return BenchmarkSuite.GutenbergSearch;

        return BenchmarkSuite.All;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "Rowles.LeanLucene.slnx");
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
        TokenBudget,
        Diagnostics,
        Suggester,
        SchemaJson,
        IndexSort,
        BlockJoin,
        GutenbergAnalysis,
        GutenbergIndex,
        GutenbergSearch
    }
}
