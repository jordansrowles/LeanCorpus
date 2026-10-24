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
        if (args.Length == 1 && args[0].Equals("--devops-manifest", StringComparison.Ordinal))
        {
            Console.WriteLine(BenchmarkSuiteRegistry.CreateManifestJson());
            return 0;
        }

        PendingSuites.Clear();

        HashSet<string> suites;
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

        var suppliedArtifactDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        var runDir = !string.IsNullOrWhiteSpace(suppliedArtifactDirectory)
            ? Path.GetFullPath(suppliedArtifactDirectory)
            : Path.Combine(repoRoot, "artifacts", "benchmark", "runs",
                now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), "core");
        Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", runDir);
        var machineDir = Directory.GetParent(runDir)?.FullName ?? runDir;
        Directory.CreateDirectory(runDir);
        string packedBkdEvidenceDirectory = Path.Combine(
            runDir,
            "packed-bkd",
            "observer-counters");
        Environment.SetEnvironmentVariable(
            "LEANCORPUS_PACKED_BKD_EVIDENCE_DIR",
            packedBkdEvidenceDirectory);
        Environment.SetEnvironmentVariable(
            "LEANCORPUS_SPATIAL_NEAREST_EVIDENCE_DIR",
            Path.Combine(runDir, "nearest", "observer-counters"));
        Environment.SetEnvironmentVariable(
            "LEANCORPUS_SHAPE_EVIDENCE_DIR",
            Path.Combine(runDir, "shapes", "observer-counters"));

        var gitCommitHash = GetGitShortHash(repoRoot);
        var sourceCommit = Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT");
        var commitHash = !string.IsNullOrWhiteSpace(sourceCommit)
            ? sourceCommit
            : gitCommitHash;
        var orchestratedRunId = Environment.GetEnvironmentVariable("LEANCORPUS_RUN_ID");
        var runId = !string.IsNullOrWhiteSpace(orchestratedRunId)
            ? orchestratedRunId
            : string.IsNullOrEmpty(commitHash)
            ? now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)
            : $"{now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)} ({commitHash})";

        // Resolve effective run type for metadata (does not affect output path)
        var effectiveRunType = string.IsNullOrEmpty(runType) ? "full" : runType;
        var listingOnly = IsBenchmarkListRequest(benchmarkArgs);

        // Clean up stale benchmark state only for a measured run.
        if (!listingOnly)
            BenchmarkHelpers.CleanTempRoot();

        if (!listingOnly && BenchmarkSuiteRegistry.UsesStandardSearchFixture(suites))
        {
            var effectiveDocCount = docCount ?? BenchmarkData.DefaultDocCount;
            Console.WriteLine($"Preparing shared search fixture ({effectiveDocCount:N0} documents)...");
            bool includesSimilaritySuite = BenchmarkSuiteRegistry.SelectWorkloads(suites)
                .Any(static workload => workload.BenchmarkType == typeof(SimilarityBenchmarks));
            SharedStandardIndex.PrepareForRun(effectiveDocCount, includesSimilaritySuite);
        }

        var suiteSummaries = new List<(string Suite, Summary Summary)>();

        foreach (var workload in BenchmarkSuiteRegistry.SelectWorkloads(suites))
            RunSuite(workload);

        ExecuteSuites(runDir, benchmarkArgs, suiteSummaries, gcDump);

        if (listingOnly)
            return 0;

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

        // Remove this run's benchmark temp tree so subsequent runs start clean.
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
            docCount ?? BenchmarkData.DefaultDocCount,
            runDir);
        if (!report.Provenance.RscriptAvailable)
            report.QualityFlags.Add("RscriptUnavailable");

        bool includesSuggesterBenchmarks = suiteSummaries
            .SelectMany(static item => item.Summary.BenchmarksCases)
            .Any(static benchmarkCase => benchmarkCase.Descriptor.Type == typeof(SuggesterBenchmarks));
        int suggesterEvidenceCount = SuggesterQualityEvidenceCollector.AttachToReport(runDir, report);
        bool missingSuggesterEvidence = includesSuggesterBenchmarks && suggesterEvidenceCount == 0;
        bool unlinkedSuggesterEvidence = report.SuggesterQuality.Any(static evidence =>
            evidence.Engines.Any(static engine => engine.BenchmarkResults.Length == 0));
        if (missingSuggesterEvidence)
            report.QualityFlags.Add("MissingSuggesterQualityEvidence");
        if (unlinkedSuggesterEvidence)
            report.QualityFlags.Add("UnlinkedSuggesterQualityEvidence");

        BenchmarkRunReportWriter.WriteReport(runDir, machineDir, report);
        SuggesterQualityEvidenceCollector.DeleteSidecars(runDir);

        Console.WriteLine();
        Console.WriteLine($"Run:    {runId}");
        Console.WriteLine($"Type:   {effectiveRunType}");
        Console.WriteLine($"Commit: {(string.IsNullOrEmpty(commitHash) ? "(unknown)" : commitHash)}");
        Console.WriteLine($"Output: {runDir}");
        Console.WriteLine($"Suites: {string.Join(", ", suiteSummaries.Select(s => s.Suite))}");
        var hasInvalidResults = missingSuggesterEvidence || unlinkedSuggesterEvidence ||
            suiteSummaries.Any(item => item.Summary.HasCriticalValidationErrors) ||
            report.Suites.Any(suite => suite.FailedBenchmarkCount > 0 || suite.MissingBenchmarkCount > 0);
        return hasInvalidResults ? 1 : 0;
    }

    private static void RunSuite(BenchmarkWorkloadDescriptor workload)
    {
        if (PendingSuites.All(suite => suite.Type != workload.BenchmarkType))
            PendingSuites.Add(new PendingSuite(workload.ReportName, workload.BenchmarkType));
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

        if (IsBenchmarkListRequest(effectiveBenchmarkArgs))
        {
            BenchmarkSwitcher.FromTypes([.. PendingSuites.Select(static suite => suite.Type).Distinct()])
                .Run(effectiveBenchmarkArgs, baseConfig);
            return;
        }

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
            if (!summary.BenchmarksCases.Any())
                continue;

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
            """);
        BenchmarkSuiteRegistry.PrintHelp();
        Console.WriteLine("""

            Output:
              Results are written under artifacts/benchmark/runs/.
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

    private static (HashSet<string> Suites, string RunType, string[] BenchmarkArgs, bool ShowHelp, int? DocCount, bool GcDump) ParseArguments(string[] args)
    {
        var suites = new HashSet<string>(StringComparer.Ordinal) { "all" };
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
        if (corpusOnly &&
            !HasBenchmarkDotNetOption(benchmarkArgs, "--filter", "-f"))
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

    private static bool IsBenchmarkListRequest(IEnumerable<string> args)
        => args.Any(static argument => argument.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--list=", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ParseSuites(string value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(BenchmarkSuiteRegistry.ParseSuiteId(part));
        }
        return result;
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

    private sealed record PendingSuite(string Name, Type Type);
}
