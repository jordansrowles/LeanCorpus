using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestRunPlannerTests
{
    [Fact]
    public async Task PreservesRepetitionProfileAndArtefactPresets()
    {
        var (checkout, configuration) = Load();
        var planner = new TestRunPlanner(configuration, new AffectedSelectionService(new RejectProcessRunner()));
        var ordinary = await planner.PlanAsync(checkout, new TestRunOptions
        {
            Selection = new TestTargetRequest { Suites = ["core"], Category = "Unit" }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, ordinary.Count);
        Assert.True(ordinary.RunnerOptions.ArtifactsEnabled);
        Assert.Equal("unit", ordinary.RunnerOptions.ParallelProfile);

        var flaky = await planner.PlanAsync(checkout, new TestRunOptions
        {
            Selection = new TestTargetRequest { Suites = ["core"] }, Flaky = true, ExplicitOnly = true,
            ProcessTimeout = "1.5m"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(30, flaky.Count);
        Assert.True(flaky.RunnerOptions.ArtifactsEnabled);
        Assert.False(flaky.RunnerOptions.Diagnostics);
        Assert.Equal("stress", flaky.RunnerOptions.ParallelProfile);
        Assert.Equal("only", flaky.RunnerOptions.ExplicitMode);
        Assert.Equal(TimeSpan.FromSeconds(90), flaky.ProcessTimeout);
    }

    [Theory]
    [InlineData(0, "integration", "off", "100s", "")]
    [InlineData(1, "missing", "off", "100s", "")]
    [InlineData(1, "integration", "forever", "100s", "")]
    [InlineData(1, "integration", "off", "", "")]
    [InlineData(1, "integration", "off", "100s", "HEAD")]
    public async Task RejectsInvalidOptionsBeforeAnyChildRuns(int count, string profile, string timeout, string hangTimeout, string commit)
    {
        var (checkout, configuration) = Load();
        var planner = new TestRunPlanner(configuration, new AffectedSelectionService(new RejectProcessRunner()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => planner.PlanAsync(checkout, new TestRunOptions
        {
            Selection = new TestTargetRequest { Suites = ["core"] }, Count = count, Profile = profile,
            ProcessTimeout = timeout, HangTimeout = hangTimeout, Commit = commit
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunsRealPreparedMtpApphostThroughPlannerAndExecutor()
    {
        var (checkout, configuration) = Load();
        var suite = new TestSuiteConfiguration("Managed DevOps", "src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj",
            "Mtp", ["net10.0"], null, false, ["Trx"], false, false, null);
        configuration = configuration with { TestSuites = configuration.TestSuites.SetItem("devops-probe", suite) };
        var runner = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(runner)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = ["devops-probe"], ContinuousIntegration = true,
                    Filter = "FullyQualifiedName~TestFilterBuilderTests|FullyQualifiedName~NativeAotManagedQualificationTests"
                },
                Count = 2, HangTimeout = "off", ProcessTimeout = "1m"
            }, TestContext.Current.CancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var result = await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), runner,
            new DotNetSdkResolver(runner), new GitRepositoryProvenanceReader(runner))
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.CommandResult.Outcome == OutcomeKind.Success,
            $"{result.CommandResult.Message}: {result.CommandResult.Details}");
        Assert.True(result.Aggregate!.Succeeded);
        Assert.Equal(2, result.Aggregate.Attempts.Count);
        Assert.NotEmpty(result.Aggregate.Tests);
        Assert.Equal(2, result.Aggregate.Tests.Count(static test => test.Stability == TestStability.AlwaysPasses));
        Assert.Equal(2, result.Aggregate.Tests.Count(static test => test.Stability == TestStability.Skipped));
        Assert.All(result.Aggregate.Tests, test => Assert.True(test.Stability is TestStability.AlwaysPasses or TestStability.Skipped));
        Assert.All(result.Aggregate.Attempts, attempt => Assert.True(File.Exists(attempt.TrxPath)));
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal(RunState.Succeeded, manifest.State);
        Assert.Equal("11.0.100-preview.7.26381.103", manifest.ToolVersions["dotnet-sdk-requested"]);
        var selectedSdkOutput = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "sdk-version.stdout.log"),
            TestContext.Current.CancellationToken);
        Assert.Equal(selectedSdkOutput.Trim(), manifest.ToolVersions["dotnet-sdk-selected"]);
        Assert.Equal("latestPatch", manifest.ToolVersions["dotnet-sdk-roll-forward"]);
    }

    [Fact]
    public async Task RunsRealManagedChildWithEnvironmentOverrideWithoutPersistingValue()
    {
        var (checkout, configuration) = Load();
        var suite = new TestSuiteConfiguration("Managed DevOps", "src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj",
            "Mtp", ["net10.0"], null, false, ["Trx"], false, false, null);
        configuration = configuration with { TestSuites = configuration.TestSuites.SetItem("devops-probe", suite) };
        var runner = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(runner)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = ["devops-probe"], ContinuousIntegration = true,
                    Filter = "FullyQualifiedName~TestProcessEnvironmentBuilderTests.ManagedChildReceivesOverride"
                },
                Count = 1,
                Explicit = true,
                EnvironmentOverrides = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["LEANCORPUS_TEST_ENV_PROBE"] = "managed-child-probe"
                },
                HangTimeout = "off",
                ProcessTimeout = "1m"
            }, TestContext.Current.CancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var result = await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), runner,
            new DotNetSdkResolver(runner), new GitRepositoryProvenanceReader(runner))
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(TestExecutionOutcome.Passed, Assert.Single(result.Aggregate!.Attempts).Outcome);
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal("[\"LEANCORPUS_TEST_ENV_PROBE\"]", manifest.Inputs["environmentOverrideNames"]);
        Assert.DoesNotContain("managed-child-probe", JsonSerializer.Serialize(manifest));
    }

    [Fact(Explicit = true)]
    public async Task RealPassingOneOffMtpRunCapturesNamedCase()
    {
        var result = await RunOneOffMtpProbeAsync(failProbe: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        var attempt = Assert.Single(result.Aggregate!.Attempts);
        Assert.Equal(TestExecutionOutcome.Passed, attempt.Outcome);
        Assert.Equal(0, attempt.ExitCode);
        var test = Assert.Single(attempt.Tests);
        Assert.Contains("OneOffProbeCase", test.TestName, StringComparison.Ordinal);
        Assert.NotNull(attempt.TrxPath);
        Assert.True(File.Exists(attempt.TrxPath));
        Assert.Equal(RunState.Succeeded, Assert.IsType<RunManifest>(result.Manifest).State);
    }

    [Fact(Explicit = true)]
    public async Task RealFailingOneOffMtpRunPreservesNamedFailureAndExitCode()
    {
        const string failureProbeValue = "issue97-deliberate-failure-probe";
        var result = await RunOneOffMtpProbeAsync(failProbe: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        var attempt = Assert.Single(result.Aggregate!.Attempts);
        Assert.Equal(TestExecutionOutcome.AssertionFailure, attempt.Outcome);
        Assert.NotNull(attempt.ExitCode);
        Assert.NotEqual(0, attempt.ExitCode);
        Assert.Equal(attempt.ExitCode, result.CommandResult.RawChildExitCode);
        var test = Assert.Single(attempt.Tests);
        Assert.Equal(TestCaseOutcome.Failed, test.Outcome);
        Assert.Contains("OneOffProbeCase", test.TestName, StringComparison.Ordinal);
        Assert.Contains("Deliberate MTP evidence probe failure", test.ErrorMessage, StringComparison.Ordinal);
        Assert.NotNull(attempt.TrxPath);
        Assert.True(File.Exists(attempt.TrxPath));
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal(RunState.Failed, manifest.State);
        Assert.Contains(attempt.ExitCode!.Value, manifest.ChildExitCodes);
        Assert.DoesNotContain(failureProbeValue, JsonSerializer.Serialize(manifest), StringComparison.Ordinal);
    }

    [Fact(Explicit = true)]
    public async Task RealNoMatchOneOffMtpRunReportsDiscoveryFailure()
    {
        var result = await RunOneOffMtpProbeAsync(failProbe: false, cancellationToken: TestContext.Current.CancellationToken,
            filter: "FullyQualifiedName~Issue97.NoSuchMtpCase");

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        var attempt = Assert.Single(result.Aggregate!.Attempts);
        Assert.Equal(TestExecutionOutcome.DiscoveryFailure, attempt.Outcome);
        Assert.Empty(attempt.Tests);
        Assert.NotNull(attempt.TrxPath);
        Assert.True(File.Exists(attempt.TrxPath));
        Assert.Equal(RunState.Failed, Assert.IsType<RunManifest>(result.Manifest).State);
    }

    private static async Task<TestExecutorResult> RunOneOffMtpProbeAsync(bool failProbe, CancellationToken cancellationToken, string? filter = null)
    {
        var (checkout, configuration) = Load();
        var suite = new TestSuiteConfiguration("Managed DevOps", "src/devops/tooling/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj",
            "Mtp", ["net10.0"], null, false, ["Trx"], false, false, null);
        configuration = configuration with { TestSuites = configuration.TestSuites.SetItem("devops-probe", suite) };
        var runner = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(runner)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = ["devops-probe"],
                    Filter = filter ?? "FullyQualifiedName~MtpOneOffOutcomeProbeTests.OneOffProbeCase"
                },
                Count = 1,
                EnvironmentOverrides = failProbe
                    ? new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["LEANCORPUS_TEST_MTP_FAILURE_PROBE"] = "issue97-deliberate-failure-probe"
                    }
                    : new Dictionary<string, string?>(StringComparer.Ordinal),
                HangTimeout = "off",
                ProcessTimeout = "1m"
            }, cancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        return await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), runner,
            new DotNetSdkResolver(runner), new GitRepositoryProvenanceReader(runner))
            .RunAsync(request, cancellationToken);
    }

    [Theory(Explicit = true)]
    [InlineData("net10.0")]
    public async Task CapturesCoreMtpTelemetryInManagedReports(string framework)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var processes = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(processes)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = ["core"],
                    Framework = framework,
                    FrameworkExplicit = true,
                    Category = "Unit",
                    Filter = "FullyQualifiedName~TelemetryExecutionStoreTests.SummaryModeDoesNotPersistCleanPassingTest"
                },
                Count = 1,
                Flaky = true,
                HangTimeout = "off",
                ProcessTimeout = "5m"
            }, TestContext.Current.CancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var result = await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processes,
            new DotNetSdkResolver(processes), new GitRepositoryProvenanceReader(processes))
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.True(result.Aggregate!.Succeeded);
        Assert.Single(result.Aggregate.Attempts);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.json"),
            TestContext.Current.CancellationToken));
        var telemetry = report.RootElement.GetProperty("telemetry");
        Assert.Equal(1, telemetry.GetProperty("executions").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("summaryPaths").GetArrayLength());
        Assert.Empty(telemetry.GetProperty("errors").EnumerateArray());
        var testsStarted = telemetry.GetProperty("testsStarted").GetInt64();
        Assert.True(testsStarted > 0);
        Assert.Equal(testsStarted, telemetry.GetProperty("testsFinished").GetInt64());
        var markdown = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("## Telemetry summary", markdown, StringComparison.Ordinal);
        Assert.Contains($"Executions: 1; tests: {testsStarted} finished of {testsStarted} started", markdown, StringComparison.Ordinal);
    }

    private static (CheckoutIdentity Checkout, DevOpsConfiguration Configuration) Load()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        return (checkout, new ConfigurationLoader(checkout.RepositoryRoot).Load());
    }

    private sealed class RejectProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No process should run during explicit test planning.");
    }
}
