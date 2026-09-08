using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Rowles.LeanCorpus.Benchmarks.Compression;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.LeanCorpus.Benchmarks.Compression;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Register extension codecs in the parent process so the Ratio column can compress
        // payloads for ratio computation during report generation. Child processes receive
        // their own registrations via [GlobalSetup] in CompressionBenchmarks.
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();

        var artifactsPath = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR")
            ?? Path.Combine(FindRepositoryRoot(), "artifacts", "benchmark", "runs", "direct", "compression");
        Directory.CreateDirectory(artifactsPath);
        var config = DefaultConfig.Instance.WithArtifactsPath(Path.Combine(artifactsPath, "_runner"));
        var summary = BenchmarkRunner.Run<CompressionBenchmarks>(config, args);
        return HasInvalidResults(summary) ? 1 : 0;
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

    private static bool HasInvalidResults(Summary summary)
    {
        return summary.HasCriticalValidationErrors ||
            !summary.Reports.Any() ||
            summary.Reports.Any(report => report.ResultStatistics is null);
    }
}
