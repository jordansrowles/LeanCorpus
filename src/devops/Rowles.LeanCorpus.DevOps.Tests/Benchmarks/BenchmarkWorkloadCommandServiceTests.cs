using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkWorkloadCommandServiceTests
{
    [Theory]
    [InlineData("Timeout", "TimedOut")]
    [InlineData("Cancelled", "Cancelled")]
    public async Task Outer_child_stop_retains_checkpoint_and_never_reports_success(string stoppedOutcome, string expectedStateName)
    {
        var expectedState = Enum.Parse<RunState>(expectedStateName);
        var root = Path.Combine(Path.GetTempPath(), "workload-service-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "src/devops/Rowles.LeanCorpus.Benchmark.Workloads/Rowles.LeanCorpus.Benchmark.Workloads.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(project, "<Project />");
        var paths = new ArtifactPaths(root);
        var store = new ArtifactRunStore(paths, runCategory: "benchmark");
        var processRunner = new StoppedChildProcessRunner(Enum.Parse<OutcomeKind>(stoppedOutcome));
        var checkout = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        var options = new BenchmarkWorkloadOptions("ingest-throughput", "lucenenet", "net10.0",
            "leancorpus-search", 42, 32, 0, 2, 1, 8, 10, 512, "explicit-commit", true, false, false);
        try
        {
            var service = new BenchmarkWorkloadCommandService(paths, store, processRunner,
                new SuccessfulSdkResolver(), new SuccessfulProvenanceReader(), null, null, "dotnet");
            var result = await service.RunAsync(
                new BenchmarkCommandRequest(checkout, BenchmarkCommandOptions.ForWorkload(options, null)),
                TestContext.Current.CancellationToken);
            Assert.Equal(Enum.Parse<OutcomeKind>(stoppedOutcome), result.CommandResult.Outcome);
            Assert.NotNull(result.RunId);
            Assert.NotNull(result.RunDirectory);
            var manifest = await store.ReadAsync(result.RunId!.Value, TestContext.Current.CancellationToken);
            Assert.Equal(expectedState, manifest!.State);
            Assert.Contains(manifest.Artifacts, artifact => artifact.EndsWith("terminal.json.partial", StringComparison.Ordinal));

            using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(result.RunDirectory!, "workload-report.json"), TestContext.Current.CancellationToken));
            var child = Assert.Single(report.RootElement.GetProperty("children").EnumerateArray());
            Assert.Equal("Incomplete", child.GetProperty("outcome").GetString());
            Assert.Equal(64, child.GetProperty("checkpointSha256").GetString()!.Length);
            Assert.Equal(expectedState.ToString(), report.RootElement.GetProperty("outcome").GetString());
            Assert.False(report.RootElement.GetProperty("eligibleForRatio").GetBoolean());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Semantic_parity_requires_two_successful_engines_with_identical_logical_work()
    {
        var ids = new[] { "id-1", "id-2" };
        var left = new WorkloadChildEvidence("leancorpus", "Completed", 0, "input-a", "output-a",
            null, new string('a', 64), ids, ["id-1"], null);
        var right = new WorkloadChildEvidence("lucenenet", "Completed", 0, "input-b", "output-b",
            null, new string('a', 64), ids, ["id-1"], null);

        Assert.True(BenchmarkWorkloadCommandService.HasSemanticParity([left, right]));
        Assert.False(BenchmarkWorkloadCommandService.HasSemanticParity([left, right with { CompletedSearchLogicalIds = ["id-2"] }]));
        Assert.False(BenchmarkWorkloadCommandService.HasSemanticParity([left, right with { VerifiedLogicalIds = ["id-2"] }]));
        Assert.False(BenchmarkWorkloadCommandService.HasSemanticParity([left, right with { Outcome = "TimedOut" }]));
        Assert.False(BenchmarkWorkloadCommandService.HasSemanticParity([left, left]));
    }

    [Fact]
    public void Terminal_reader_accepts_exact_completed_fixture_and_rejects_false_completion()
    {
        var options = new BenchmarkWorkloadOptions("ingest-throughput", "lucenenet", "net10.0",
            "leancorpus-search", 42, 32, 0, 4, 1, 8, 30, 4096, "explicit-commit", true, false, false);
        var input = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scenario = options.Scenario, engine = options.Engine,
            dataset = options.Dataset, datasetVersion = 1, seed = options.Seed,
            documentCount = options.DocumentCount, searchOperations = options.SearchOperations,
            concurrency = options.Concurrency, queueCapacity = options.QueueCapacity,
            refreshEveryDocuments = options.RefreshEveryDocuments, durationMilliseconds = options.DurationSeconds * 1000,
            memoryBudgetMiB = options.MemoryBudgetMiB, durability = options.Durability,
            sourceCommit = new string('a', 40), sourceDirty = false, indexPath = "/tmp/index"
        });
        using var parsedInput = JsonDocument.Parse(input);
        var ids = Enumerable.Range(0, 32).Select(index => $"id-{index:D3}").ToArray();
        var terminal = Terminal(parsedInput.RootElement, ids, 1);
        var path = Path.Combine(Path.GetTempPath(), "workload-terminal-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(terminal));
            var evidence = BenchmarkWorkloadCommandService.ReadTerminal(path, input, "lucenenet", options, 0, 0,
                new string('b', 64), null);
            Assert.Equal("Completed", evidence.Outcome);
            Assert.Equal(ids, evidence.VerifiedLogicalIds);
            Assert.Equal(64, evidence.OutputSha256!.Length);

            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(Terminal(parsedInput.RootElement, ids, 2)));
            Assert.Throws<InvalidDataException>(() => BenchmarkWorkloadCommandService.ReadTerminal(path, input,
                "lucenenet", options, 0, 0, new string('b', 64), null));

            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(Terminal(parsedInput.RootElement, ids[..^1], 1)));
            Assert.Throws<InvalidDataException>(() => BenchmarkWorkloadCommandService.ReadTerminal(path, input,
                "lucenenet", options, 0, 0, new string('b', 64), null));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Terminal_reader_rejects_a_request_that_does_not_match_the_hashed_input()
    {
        var options = new BenchmarkWorkloadOptions("ingest-throughput", "lucenenet", "net10.0",
            "leancorpus-search", 42, 32, 0, 4, 1, 8, 30, 4096, "explicit-commit", true, false, false);
        var input = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scenario = options.Scenario, engine = options.Engine,
            dataset = options.Dataset, datasetVersion = 1, seed = options.Seed,
            documentCount = options.DocumentCount, searchOperations = options.SearchOperations,
            concurrency = options.Concurrency, queueCapacity = options.QueueCapacity,
            refreshEveryDocuments = options.RefreshEveryDocuments, durationMilliseconds = options.DurationSeconds * 1000,
            memoryBudgetMiB = options.MemoryBudgetMiB, durability = options.Durability,
            sourceCommit = new string('a', 40), sourceDirty = false, indexPath = "/tmp/index"
        });
        using var parsedInput = JsonDocument.Parse(input);
        var ids = Enumerable.Range(0, 32).Select(index => $"id-{index:D3}").ToArray();
        var changed = JsonSerializer.SerializeToElement(new { schemaVersion = 1, engine = "leancorpus" });
        var terminal = Terminal(changed, ids, 1);
        var path = Path.Combine(Path.GetTempPath(), "workload-terminal-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(terminal));
            Assert.Throws<InvalidDataException>(() => BenchmarkWorkloadCommandService.ReadTerminal(path, input,
                "lucenenet", options, 0, 0, new string('b', 64), null));
        }
        finally { File.Delete(path); }
    }

    private static object Terminal(JsonElement request, string[] ids, int schemaVersion) => new
    {
        schemaVersion, request, outcome = "Completed", classification = "Unqualified",
        completionBoundary = "explicit-commit-during-measured-interval",
        datasetSha256 = new string('c', 64), preloadedDocuments = 0, admittedDocuments = 32,
        committedDocuments = 32, visibleDocuments = 32, completedSearches = 0,
        failedOperations = 0, cancelledOperations = 0, committedLogicalIds = ids,
        verifiedLogicalIds = ids, completedSearchLogicalIds = Array.Empty<string>(), failure = (string?)null
    };

    private sealed class SuccessfulSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.0", "11.0.0", "latestPatch", null));
    }

    private sealed class SuccessfulProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, new string('a', 40), "issue-97", false, null));
    }

    private sealed class StoppedChildProcessRunner(OutcomeKind childOutcome) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            File.WriteAllText(request.StandardOutputLogPath, string.Empty);
            File.WriteAllText(request.StandardErrorLogPath, string.Empty);
            var isWorker = request.Arguments[0] == "run";
            if (isWorker)
                File.WriteAllText(request.Arguments[^1] + ".partial", "{\"schemaVersion\":1,\"outcome\":\"Running\"}");
            return Task.FromResult(new ProcessResult(isWorker ? childOutcome : OutcomeKind.Success,
                isWorker ? null : 0, [], [], 0, 0, TimeSpan.FromMilliseconds(1),
                request.StandardOutputLogPath, request.StandardErrorLogPath,
                isWorker ? "Forced child stop for the test." : null));
        }
    }
}
