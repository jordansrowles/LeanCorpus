using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Workflows;

namespace Rowles.LeanCorpus.DevOps.Tests.Workflows;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task RunsOnlyRequestedTargetAndItsDependenciesInOrder()
    {
        var executed = new List<string>();
        var targets = new[]
        {
            Target("prepare", [], executed),
            Target("compile", ["prepare"], executed),
            Target("unrelated", [], executed)
        };

        var result = await new WorkflowRunner().RunAsync(targets, ["compile"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(["prepare", "compile"], executed);
        Assert.DoesNotContain("unrelated", executed);
        Assert.Contains(result.Events, item => item.Target == "prepare" && item.State == WorkflowTargetState.Succeeded);
    }

    [Fact]
    public async Task FailedDependencyPreventsDependentActionAndPreservesFailure()
    {
        var dependentRan = false;
        var targets = new[]
        {
            new WorkflowTarget("prepare", [], _ => Task.FromException(new InvalidOperationException("setup failed")), "prepare"),
            new WorkflowTarget("compile", ["prepare"], _ => { dependentRan = true; return Task.CompletedTask; }, "compile")
        };

        var result = await new WorkflowRunner().RunAsync(targets, ["compile"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.Outcome);
        Assert.False(dependentRan);
        Assert.Contains("prepare", result.FailedTargets);
        Assert.Contains("setup failed", result.Failure, StringComparison.Ordinal);
        Assert.Contains("compile", result.SkippedTargets);
    }

    [Fact]
    public async Task CancellationStopsTheActiveTargetAndMarksUnscheduledDependencySkipped()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var dependentRan = false;
        var targets = new[]
        {
            new WorkflowTarget("wait", [], async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, "wait"),
            new WorkflowTarget("finish", ["wait"], _ => { dependentRan = true; return Task.CompletedTask; }, "finish")
        };
        var run = new WorkflowRunner().RunAsync(targets, ["finish"], cancellationToken: cancellation.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var result = await run;

        Assert.Equal(OutcomeKind.Cancelled, result.Outcome);
        Assert.False(dependentRan);
        Assert.Contains("finish", result.SkippedTargets);
        Assert.Contains(result.Events, item => item.Target == "wait" && item.State == WorkflowTargetState.Cancelled);
    }

    [Fact]
    public async Task RejectsUnknownDependenciesAndCyclesBeforeRunningTargets()
    {
        var runner = new WorkflowRunner();
        var unknown = new[] { new WorkflowTarget("build", ["missing"], _ => Task.CompletedTask, "build") };
        var cycle = new[]
        {
            new WorkflowTarget("first", ["second"], _ => Task.CompletedTask, "first"),
            new WorkflowTarget("second", ["first"], _ => Task.CompletedTask, "second")
        };

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(unknown, ["build"], cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(cycle, ["first"], cancellationToken: TestContext.Current.CancellationToken));
    }

    private static WorkflowTarget Target(string name, IReadOnlyList<string> dependencies, List<string> executed)
        => new(name, dependencies, _ => { executed.Add(name); return Task.CompletedTask; }, name);
}
