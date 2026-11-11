using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkCampaignPolicyTests
{
    [Theory]
    [InlineData("CommandFailure", true)]
    [InlineData("Timeout", false)]
    [InlineData("Cancelled", false)]
    [InlineData("InfrastructureFailure", false)]
    [InlineData("ValidationFailure", false)]
    public void Only_an_ordinary_child_failure_lets_a_campaign_continue(string outcome, bool expected)
        => Assert.Equal(expected, BenchmarkCommandService.ContinuesCampaignAfterChildFailure(Enum.Parse<OutcomeKind>(outcome)));

    [Fact]
    public async Task A_failed_middle_project_does_not_stop_the_remaining_projects_and_the_run_still_fails()
    {
        using var checkout = new CampaignCheckout();
        var runner = new ScriptedProcessRunner(static (target, phase) =>
            phase == "run" && target == "core" ? OutcomeKind.CommandFailure : OutcomeKind.Success);

        var result = await checkout.RunAsync(runner);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(
            ["compression:build", "compression:run", "core:build", "core:run", "text:build", "text:run"],
            runner.Calls.Where(static call => !call.EndsWith(":run-skipped", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task Report_opens_with_separate_execution_identity_and_qualification_lines()
    {
        using var checkout = new CampaignCheckout();
        var runner = new ScriptedProcessRunner(static (target, phase) =>
            phase == "run" && target == "core" ? OutcomeKind.CommandFailure : OutcomeKind.Success);

        var result = await checkout.RunAsync(runner);

        string[] lines = File.ReadAllLines(Path.Combine(result.RunDirectory!, "report.md"));
        Assert.StartsWith("# Benchmark run ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Execution: ", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("Identity: Rejected; reasons: ", lines[3], StringComparison.Ordinal);
        Assert.DoesNotContain("reasons: none", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("Qualification: Unqualified, scope none;", lines[4], StringComparison.Ordinal);
        Assert.Contains("ExecutionIncomplete", lines[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_build_failure_still_stops_the_campaign()
    {
        using var checkout = new CampaignCheckout();
        var runner = new ScriptedProcessRunner(static (target, phase) =>
            phase == "build" && target == "core" ? OutcomeKind.CommandFailure : OutcomeKind.Success);

        var result = await checkout.RunAsync(runner);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.DoesNotContain(runner.Calls, static call => call.StartsWith("text:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_timeout_still_stops_the_campaign()
    {
        using var checkout = new CampaignCheckout();
        var runner = new ScriptedProcessRunner(static (target, phase) =>
            phase == "run" && target == "core" ? OutcomeKind.Timeout : OutcomeKind.Success);

        var result = await checkout.RunAsync(runner);

        Assert.Equal(OutcomeKind.Timeout, result.CommandResult.Outcome);
        Assert.DoesNotContain(runner.Calls, static call => call.StartsWith("text:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_later_stopping_failure_replaces_an_earlier_case_failure()
    {
        using var checkout = new CampaignCheckout();
        var runner = new ScriptedProcessRunner(static (target, phase) => phase == "run"
            ? target switch
            {
                "compression" => OutcomeKind.CommandFailure,
                "core" => OutcomeKind.Timeout,
                _ => OutcomeKind.Success
            }
            : OutcomeKind.Success);

        var result = await checkout.RunAsync(runner);

        Assert.Equal(OutcomeKind.Timeout, result.CommandResult.Outcome);
        Assert.DoesNotContain(runner.Calls, static call => call.StartsWith("text:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("compression")]
    [InlineData("core")]
    [InlineData("text")]
    public void Every_benchmark_project_receives_explicit_tiered_pgo(string projectKey)
    {
        using var checkout = new CampaignCheckout();
        var service = checkout.CreateService(new ScriptedProcessRunner(static (_, _) => OutcomeKind.Success));
        var options = CampaignCheckout.Options();
        var plan = BenchmarkCommandPlanner.Create(options, checkout.Configuration, checkout.Root);
        var invocation = plan.Invocations.First(item => item.ProjectKey == projectKey);
        var manifest = new RunManifest
        {
            SchemaVersion = RunManifest.CurrentSchemaVersion,
            RunId = Guid.NewGuid(),
            Command = "benchmark",
            State = RunState.Running,
            StartedUtc = DateTimeOffset.UtcNow,
            SourceDirty = false,
            Platform = "test",
            Inputs = new Dictionary<string, string>(),
            Artifacts = [],
            ChildExitCodes = []
        };

        var environment = service.BuildEnvironment(manifest, checkout.Root, options, plan, invocation);

        Assert.Equal("1", environment[BenchmarkCommandService.TieredPgoVariable]);
    }

    [Fact]
    public async Task The_run_manifest_records_the_tiered_pgo_setting()
    {
        using var checkout = new CampaignCheckout();
        var result = await checkout.RunAsync(new ScriptedProcessRunner(static (_, phase) =>
            phase == "run" ? OutcomeKind.CommandFailure : OutcomeKind.Success));

        var paths = new ArtifactPaths(checkout.Root);
        var manifest = await new ArtifactRunStore(paths, runCategory: "benchmark")
            .ReadAsync(result.RunId!.Value, TestContext.Current.CancellationToken);

        Assert.Equal("1", manifest!.Inputs["tieredPgo"]);
    }

    private sealed class ScriptedProcessRunner(Func<string, string, OutcomeKind> outcomeFor) : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            File.WriteAllText(request.StandardOutputLogPath, string.Empty);
            File.WriteAllText(request.StandardErrorLogPath, string.Empty);
            var joined = string.Join(' ', request.Arguments);
            var target = joined.Contains("Rowles.LeanCorpus.Benchmarks.Compression", StringComparison.Ordinal)
                ? "compression"
                : joined.Contains("Rowles.Text.Benchmarks", StringComparison.Ordinal)
                    ? "text"
                    : "core";
            var phase = request.Arguments[0] == "build" ? "build" : "run";
            Calls.Add($"{target}:{phase}");
            var outcome = outcomeFor(target, phase);
            if (phase == "run" && outcome == OutcomeKind.Success)
            {
                // A completed child must leave a non-empty result for the campaign's result validation.
                var results = Path.Combine(Path.GetDirectoryName(request.StandardOutputLogPath)!, "_runner", "results");
                Directory.CreateDirectory(results);
                File.WriteAllText(Path.Combine(results, "result.md"), "result");
                // Core and Rowles.Text hosts must also write a canonical schema v6 report (ADR047).
                if (target is "core" or "text")
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(request.StandardOutputLogPath)!, "report.json"),
                        "{\"schemaVersion\":6,\"totalBenchmarkCount\":1,\"suites\":[{\"failedBenchmarkCount\":0,\"intermediateBenchmarkCount\":0,\"missingBenchmarkCount\":0}]}");
            }

            return Task.FromResult(new ProcessResult(outcome, outcome == OutcomeKind.Success ? 0 : 1, [], [], 0, 0,
                TimeSpan.FromMilliseconds(1), request.StandardOutputLogPath, request.StandardErrorLogPath,
                outcome == OutcomeKind.Success ? null : "Forced failure for the test."));
        }
    }

    private sealed class CampaignCheckout : IDisposable
    {
        private readonly List<string> runDirectories = [];

        public CampaignCheckout()
        {
            // The configuration loader validates the real repository layout, so these tests use the real
            // checkout and remove the run directories they create. Nothing is executed: the runner is scripted.
            Root = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
            Configuration = new ConfigurationLoader(Root).Load();
        }

        public string Root { get; }

        public DevOpsConfiguration Configuration { get; }

        public static BenchmarkCommandOptions Options()
            => new("all", "fast", "net10.0", DocumentCount: 0, "synthetic", ReferencePath: null, Area: null, Group: null,
                SourceCommit: null, SourceRef: null, SourceManifest: null, Controlled: false, CorpusOnly: false,
                GcDump: false, PrepareData: false, BookCount: 200, Dry: false, List: false, []);

        public BenchmarkCommandService CreateService(IProcessRunner runner)
        {
            var paths = new ArtifactPaths(Root);
            return new BenchmarkCommandService(paths, new ArtifactRunStore(paths, runCategory: "benchmark"), runner,
                new SuccessfulSdkResolver(), new SuccessfulProvenanceReader());
        }

        public async Task<BenchmarkCommandResult> RunAsync(IProcessRunner runner)
        {
            var identity = new CheckoutIdentity(Root, Path.Combine(Root, ".git"), CheckoutGitMarkerKind.Directory);
            var result = await CreateService(runner).RunAsync(new BenchmarkCommandRequest(identity, Options()),
                TestContext.Current.CancellationToken);
            if (result.RunDirectory is { } directory)
                runDirectories.Add(directory);
            return result;
        }

        public void Dispose()
        {
            foreach (var directory in runDirectories)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { }
            }
        }
    }

    private sealed class SuccessfulSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.0", "11.0.0", "latestPatch", null));
    }

    private sealed class SuccessfulProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, new string('a', 40), "4.0/perf", false, null));
    }
}
