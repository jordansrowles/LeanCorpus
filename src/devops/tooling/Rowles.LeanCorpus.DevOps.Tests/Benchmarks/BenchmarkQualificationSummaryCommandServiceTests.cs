using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkQualificationSummaryCommandServiceTests
{
    private const string SourceCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task WritesSchemaSummaryJobSummaryAndTerminalRunEvidence()
    {
        using var checkout = new TemporaryCheckout();
        var paths = new ArtifactPaths(checkout.Root);
        var service = CreateService(paths);
        string reportsPath = Path.Combine(checkout.Root, "collected reports.json");
        string outputPath = Path.Combine(checkout.Root, "qualification summary.json");
        string stepSummaryPath = Path.Combine(checkout.Root, "step summary.md");
        await File.WriteAllTextAsync(reportsPath, CreateReports(), TestContext.Current.CancellationToken);

        var result = await service.RunAsync(checkout.Identity,
            new BenchmarkQualificationSummaryCommandOptions(reportsPath, outputPath, false, 1),
            new Dictionary<string, string?> { ["GITHUB_STEP_SUMMARY"] = stepSummaryPath },
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        using JsonDocument output = JsonDocument.Parse(await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Equal(1, output.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(SourceCommit, output.RootElement.GetProperty("sourceCommit").GetString());
        Assert.False(output.RootElement.GetProperty("comparativeValuesPublished").GetBoolean());
        Assert.Contains("Source commit:", await File.ReadAllTextAsync(stepSummaryPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(result.RunDirectory, "run.json"), TestContext.Current.CancellationToken));
        Assert.Equal("succeeded", manifest.RootElement.GetProperty("state").GetString());
        Assert.Equal(SourceCommit, manifest.RootElement.GetProperty("sourceCommit").GetString());
        Assert.Contains("qualification-summary.json", manifest.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(static item => item.GetString()!), StringComparer.Ordinal);
    }

    [Fact]
    public async Task WritesFailingSummaryAndRunEvidenceWhenReportsAreMissing()
    {
        using var checkout = new TemporaryCheckout();
        var paths = new ArtifactPaths(checkout.Root);
        var service = CreateService(paths);
        string outputPath = Path.Combine(checkout.Root, "qualification-summary.json");

        var result = await service.RunAsync(checkout.Identity,
            new BenchmarkQualificationSummaryCommandOptions(Path.Combine(checkout.Root, "missing.json"), outputPath, false, 1),
            new Dictionary<string, string?> { ["GITHUB_STEP_SUMMARY"] = null },
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(1, result.CommandResult.ExitCode);
        using JsonDocument output = JsonDocument.Parse(await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Contains("CollectedReportsMissing", output.RootElement.GetProperty("failureReasonCodes").EnumerateArray()
            .Select(static item => item.GetString()!), StringComparer.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(result.RunDirectory, "run.json"), TestContext.Current.CancellationToken));
        Assert.Equal("failed", manifest.RootElement.GetProperty("state").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("childExitCodes")[0].GetInt32());
    }

    private static BenchmarkQualificationSummaryCommandService CreateService(ArtifactPaths paths)
    {
        var processRunner = new ProcessRunner();
        return new BenchmarkQualificationSummaryCommandService(
            paths,
            new ArtifactRunStore(paths, runCategory: "benchmark"),
            new FixedProvenanceReader());
    }

    private static string CreateReports()
        => JsonSerializer.Serialize(new[]
        {
            new
            {
                path = "artifacts/benchmark/runs/run-1/run-report.json",
                report = new
                {
                    runId = "run-1",
                    status = "Passed",
                    source = new { commit = SourceCommit },
                    execution = new { classification = "Completed" },
                    comparisonAssessment = new
                    {
                        qualificationState = "Qualified",
                        eligibleForRatio = true,
                        reasonCodes = Array.Empty<string>()
                    }
                }
            }
        });

    private sealed class FixedProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, SourceCommit, "issue-97", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            Root = Path.Combine(Path.GetTempPath(), "LeanCorpus benchmark summary " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            string gitDirectory = Path.Combine(Root, ".git");
            Directory.CreateDirectory(gitDirectory);
            Identity = new CheckoutIdentity(Root, gitDirectory, CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
