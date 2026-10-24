using Rowles.LeanCorpus.DevOps.Benchmarks;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkResultValidationTests
{
    [Fact]
    public void RejectsMissingBenchmarkDotNetResults()
    {
        using var directory = TemporaryDirectory.Create();

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Contains("did not create a results directory", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsEmptyResultsEvenWhenTheRunnerCreatedItsDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "_runner", "results"));

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Contains("produced no non-empty result files", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedCoreReportJson()
    {
        using var directory = TemporaryDirectory.Create();
        var results = Path.Combine(directory.Path, "_runner", "results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "report.md"), "benchmark results");
        File.WriteAllText(Path.Combine(directory.Path, "report.json"), "{\"schemaVersion\":4");

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Contains("report is invalid", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedBenchmarkDotNetResultJsonForNonCoreProjects()
    {
        using var directory = TemporaryDirectory.Create();
        var results = Path.Combine(directory.Path, "_runner", "results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "report.md"), "benchmark results");
        File.WriteAllText(Path.Combine(results, "results.json"), "{\"Benchmarks\":[");

        var failure = BenchmarkCommandService.ValidateResults("text", directory.Path);

        Assert.Contains("is truncated or invalid", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsCompleteVersionFiveCoreReportAndNonEmptyBenchmarkDotNetOutput()
    {
        using var directory = TemporaryDirectory.Create();
        var results = Path.Combine(directory.Path, "_runner", "results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "report.md"), "benchmark results");
        File.WriteAllText(Path.Combine(directory.Path, "report.json"),
            "{\"schemaVersion\":5,\"totalBenchmarkCount\":1,\"suites\":[{\"failedBenchmarkCount\":0,\"intermediateBenchmarkCount\":0,\"missingBenchmarkCount\":0}]}");

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Null(failure);
    }

    [Fact]
    public void RejectsHistoricalVersionFourCoreReport()
    {
        using var directory = TemporaryDirectory.Create();
        var results = Path.Combine(directory.Path, "_runner", "results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "report.md"), "benchmark results");
        File.WriteAllText(Path.Combine(directory.Path, "report.json"),
            "{\"schemaVersion\":4,\"totalBenchmarkCount\":1,\"suites\":[{\"failedBenchmarkCount\":0,\"intermediateBenchmarkCount\":0,\"missingBenchmarkCount\":0}]}");

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Contains("unsupported schema", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIntermediateCoreRowsEvenWhenNoRowsFailedOrWereMissing()
    {
        using var directory = TemporaryDirectory.Create();
        var results = Path.Combine(directory.Path, "_runner", "results");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "report.md"), "benchmark results");
        File.WriteAllText(Path.Combine(directory.Path, "report.json"),
            "{\"schemaVersion\":5,\"totalBenchmarkCount\":1,\"suites\":[{\"failedBenchmarkCount\":0,\"intermediateBenchmarkCount\":1,\"missingBenchmarkCount\":0}]}");

        var failure = BenchmarkCommandService.ValidateResults("core", directory.Path);

        Assert.Contains("intermediate", failure, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus benchmark results " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
