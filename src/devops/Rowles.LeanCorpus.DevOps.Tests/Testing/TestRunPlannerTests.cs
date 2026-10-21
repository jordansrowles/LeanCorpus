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
        Assert.False(ordinary.RunnerOptions.ArtifactsEnabled);
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
        var suite = new TestSuiteConfiguration("Managed DevOps", "src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj",
            "Mtp", ["net10.0"], null, false, ["Trx"], false, false, null);
        configuration = configuration with { TestSuites = configuration.TestSuites.SetItem("devops-probe", suite) };
        var runner = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(runner)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = ["devops-probe"], ContinuousIntegration = true,
                    Filter = "FullyQualifiedName~TestFilterBuilderTests"
                },
                Count = 2, HangTimeout = "off", ProcessTimeout = "1m"
            }, TestContext.Current.CancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var result = await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), runner,
            new GitRepositoryProvenanceReader(runner)).RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.CommandResult.Outcome == OutcomeKind.Success,
            $"{result.CommandResult.Message}: {result.CommandResult.Details}");
        Assert.True(result.Aggregate!.Succeeded);
        Assert.Equal(2, result.Aggregate.Attempts.Count);
        Assert.NotEmpty(result.Aggregate.Tests);
        Assert.All(result.Aggregate.Tests, test => Assert.Equal(TestStability.AlwaysPasses, test.Stability));
        Assert.All(result.Aggregate.Attempts, attempt => Assert.True(File.Exists(attempt.TrxPath)));
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
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
