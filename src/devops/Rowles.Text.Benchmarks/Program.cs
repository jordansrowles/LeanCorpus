using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Rowles.Text.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        string artifactsPath = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR")
            ?? Path.Combine(FindRepositoryRoot(), "artifacts", "benchmark", "runs", "direct", "text");
        Directory.CreateDirectory(artifactsPath);
        var config = DefaultConfig.Instance.WithArtifactsPath(Path.Combine(artifactsPath, "_runner"));
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return HasInvalidResults(summaries) ? 1 : 0;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(startPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Rowles.LeanCorpus.slnx")))
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
