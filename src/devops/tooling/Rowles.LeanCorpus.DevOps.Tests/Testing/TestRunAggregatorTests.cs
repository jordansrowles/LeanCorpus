using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestRunAggregatorTests
{
    [Fact]
    public void RetainsAttemptIdentityAndFindsFailThenPassInstability()
    {
        var attempts = new[]
        {
            Attempt(1, TestExecutionOutcome.AssertionFailure,
                Case("case-a", "Same display name", TestCaseOutcome.Failed),
                Case("case-b", "Same display name", TestCaseOutcome.Passed)),
            Attempt(2, TestExecutionOutcome.Passed,
                Case("case-a", "Same display name", TestCaseOutcome.Passed),
                Case("case-b", "Same display name", TestCaseOutcome.Passed))
        };

        var aggregate = TestRunAggregator.Aggregate(["core/net10.0"], expectedIterations: 2, attempts);

        Assert.False(aggregate.Succeeded);
        Assert.Equal(2, aggregate.Attempts.Count);
        Assert.Equal(2, aggregate.Tests.Count);
        var intermittent = Assert.Single(aggregate.Tests, static test => test.StableId == "case-a");
        Assert.Equal(TestStability.IntermittentFailure, intermittent.Stability);
        Assert.Equal([1, 2], intermittent.Attempts.Select(static attempt => attempt.Iteration));
        var alwaysPassed = Assert.Single(aggregate.Tests, static test => test.StableId == "case-b");
        Assert.Equal(TestStability.AlwaysPasses, alwaysPassed.Stability);
    }

    [Fact]
    public void MissingAndAllSkippedAttemptsAreDistinguished()
    {
        var aggregate = TestRunAggregator.Aggregate(["core/net10.0", "text/net10.0"], expectedIterations: 2,
        [
            Attempt(1, TestExecutionOutcome.Passed,
                Case("skip", "Skipped test", TestCaseOutcome.Skipped, "platform unavailable")),
            Attempt(2, TestExecutionOutcome.Passed,
                Case("skip", "Skipped test", TestCaseOutcome.Skipped, "platform unavailable"))
        ]);

        Assert.False(aggregate.Succeeded);
        Assert.Equal(TestStability.Skipped, Assert.Single(aggregate.Tests).Stability);
        Assert.Equal(2, Assert.Single(aggregate.Tests).ExpectedAttemptCount);
        Assert.All(Assert.Single(aggregate.Tests).Attempts,
            static attempt => Assert.Equal("platform unavailable", attempt.SkipReason));
        Assert.Equal(2, aggregate.UnscheduledAttemptCount);
    }

    [Fact]
    public void PassingTargetAttemptsCannotHideAQuieterSecondReport()
    {
        var aggregate = TestRunAggregator.Aggregate(["core/net10.0"], expectedIterations: 2,
        [
            Attempt(1, TestExecutionOutcome.Passed,
                Case("stable", "Stable", TestCaseOutcome.Passed),
                Case("missing-second", "Missing second observation", TestCaseOutcome.Passed)),
            Attempt(2, TestExecutionOutcome.Passed,
                Case("stable", "Stable", TestCaseOutcome.Passed))
        ]);

        Assert.False(aggregate.Succeeded);
        Assert.Equal(TestStability.Incomplete, Assert.Single(aggregate.Tests, static test => test.StableId == "missing-second").Stability);
    }

    private static TestExecutionAttempt Attempt(int iteration, TestExecutionOutcome outcome, params TestCaseResult[] tests)
        => new(new TestTarget("core/net10.0", "Core", "core", "Mtp", "src/core.csproj", "net10.0", "Release", "", "", [], false, [], []),
            iteration, outcome, tests, true, null);

    private static TestCaseResult Case(string stableId, string name, TestCaseOutcome outcome, string? reason = null)
        => new(stableId, name, outcome, reason, null, TimeSpan.FromMilliseconds(1));
}
