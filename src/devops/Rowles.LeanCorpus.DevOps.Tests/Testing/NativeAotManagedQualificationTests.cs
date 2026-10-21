using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class NativeAotManagedQualificationTests
{
    [Theory(Explicit = true)]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public async Task PublishesAndExecutesNativeAotWithManagedServices(string framework)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var processes = new ProcessRunner();
        var request = await new TestRunPlanner(configuration, new AffectedSelectionService(processes)).PlanAsync(checkout,
            new TestRunOptions
            {
                Selection = new TestTargetRequest { Suites = ["aot"], Framework = framework, FrameworkExplicit = true },
                UiEvents = true,
                ProcessTimeout = "5m"
            }, TestContext.Current.CancellationToken);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var result = await new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processes,
            new DotNetSdkResolver(processes), new GitRepositoryProvenanceReader(processes))
            .RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.CommandResult.Outcome == OutcomeKind.Success,
            $"{result.CommandResult.Message}: {result.CommandResult.Details}");
        var attempt = Assert.Single(result.Aggregate!.Attempts);
        Assert.Equal("AotNative", attempt.Target.RunnerKind);
        Assert.Equal(0, attempt.ExitCode);
        Assert.Equal(TestExecutionOutcome.Passed, attempt.Outcome);
        Assert.Null(attempt.TrxPath);
        Assert.Empty(attempt.Tests);
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
    }
}
