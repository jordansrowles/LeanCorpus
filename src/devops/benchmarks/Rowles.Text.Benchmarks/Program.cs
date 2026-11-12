using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Text.Json;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Benchmarks.Infrastructure;

namespace Rowles.Text.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].Equals("--devops-manifest", StringComparison.Ordinal))
        {
            var manifestTypes = GetBenchmarkTypes(includeGutenberg: true);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                project = "text",
                benchmarkClasses = manifestTypes.Select(static type => new { type = type.FullName }),
                discoveredBenchmarkClasses = manifestTypes.Select(static type => new { type = type.FullName })
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }

        string artifactsPath = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR")
            ?? Path.Combine(FindRepositoryRoot(), "artifacts", "benchmark", "runs", "direct", "text");
        Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", artifactsPath);
        Directory.CreateDirectory(artifactsPath);
        var config = BenchmarkParameterSelectionFilter.Apply(
            DefaultConfig.Instance.WithArtifactsPath(Path.Combine(artifactsPath, "_runner")));
        bool includeGutenberg = args.Any(argument =>
            argument.Contains(nameof(GutenbergAnalysisBenchmarks), StringComparison.OrdinalIgnoreCase));
        var benchmarkTypes = GetBenchmarkTypes(includeGutenberg);
        if (!args.Contains("--filter", StringComparer.OrdinalIgnoreCase)
            && !args.Contains("-f", StringComparer.OrdinalIgnoreCase))
            args = [.. args, "--filter", "*"];
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        var summaries = BenchmarkSwitcher.FromTypes(benchmarkTypes).Run(args, config).ToArray();
        if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
            return 0;
        BenchmarkRunReport report = WriteCanonicalReport(artifactsPath, args, startedAtUtc, summaries);
        bool reportInvalid = report.Suites.Count == 0 ||
            report.Suites.Any(static suite => suite.FailedBenchmarkCount > 0 || suite.MissingBenchmarkCount > 0);
        return HasInvalidResults(summaries) || reportInvalid ? 1 : 0;
    }

    /// <summary>
    /// Writes the canonical schema v6 <c>report.json</c> (ADR047), so managed DevOps can validate a
    /// Text-only run. Dataset identity comes from the DataForge sidecars the benchmarks wrote.
    /// </summary>
    private static BenchmarkRunReport WriteCanonicalReport(
        string artifactsPath,
        string[] benchmarkArgs,
        DateTimeOffset startedAtUtc,
        IReadOnlyList<Summary> summaries)
    {
        string repositoryRoot = FindRepositoryRoot();
        string gitCommit = Environment.GetEnvironmentVariable("BENCH_SOURCE_COMMIT") ?? string.Empty;
        var suiteSummaries = summaries
            .Where(static summary => summary.BenchmarksCases.Any())
            .Select(static summary => (Suite: summary.BenchmarksCases.First().Descriptor.Type.Name, Summary: summary))
            .ToArray();
        string runId = Environment.GetEnvironmentVariable("LEANCORPUS_RUN_ID") is { Length: > 0 } orchestrated
            ? orchestrated
            : startedAtUtc.ToString("yyyy-MM-dd HH-mm", System.Globalization.CultureInfo.InvariantCulture);
        var report = BenchmarkRunReportBuilder.Build(runId, startedAtUtc, benchmarkArgs, suiteSummaries,
            BenchmarkSemanticProofs.ReadAll(artifactsPath),
            BenchmarkChildRuntime.ReadAll(artifactsPath));
        report.CommitHash = gitCommit;
        report.RunType = "text";
        report.Provenance = BenchmarkProvenanceBuilder.Build(repositoryRoot, gitCommit, effectiveDocCount: null, artifactsPath);
        if (!report.Provenance.RscriptAvailable)
            report.QualityFlags.Add("RscriptUnavailable");
        BenchmarkRunReportWriter.WriteReport(artifactsPath, Directory.GetParent(artifactsPath)?.FullName ?? artifactsPath, report);
        return report;
    }

    private static Type[] GetBenchmarkTypes(bool includeGutenberg)
        => typeof(Program).Assembly.GetTypes()
            .Where(type => (includeGutenberg || type != typeof(GutenbergAnalysisBenchmarks))
                && type.GetMethods().Any(method => method.IsDefined(typeof(BenchmarkAttribute), inherit: false)))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        foreach (string startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(startPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                    File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "Rowles.LeanCorpus.slnx")))
                    return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "LeanCorpus repository root could not be found. Run this benchmark through ./devops benchmark or from inside the repository.");
    }

    private static bool HasInvalidResults(IEnumerable<Summary> summaries)
    {
        var summaryArray = summaries.ToArray();
        return summaryArray.Length == 0 || summaryArray.Any(summary =>
            summary.HasCriticalValidationErrors ||
            !summary.Reports.Any() ||
            summary.Reports.Any(report => report.ResultStatistics is null));
    }
}
