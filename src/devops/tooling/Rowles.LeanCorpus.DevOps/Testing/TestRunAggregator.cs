namespace Rowles.LeanCorpus.DevOps.Testing;

internal enum TestStability
{
    AlwaysPasses,
    AlwaysFails,
    IntermittentFailure,
    Skipped,
    Incomplete
}

internal sealed record TestCaseAttempt(
    int Iteration,
    TestCaseOutcome Outcome,
    TimeSpan Duration,
    string? SkipReason,
    string? ErrorMessage);

internal sealed record TestCaseAggregate(
    string TargetKey,
    string Suite,
    string Framework,
    string StableId,
    string TestName,
    int ExpectedAttemptCount,
    IReadOnlyList<TestCaseAttempt> Attempts,
    TestStability Stability);

internal sealed record TestExecutionAttempt(
    TestTarget Target,
    int Iteration,
    TestExecutionOutcome Outcome,
    IReadOnlyList<TestCaseResult> Tests,
    bool Completed,
    string? Failure)
{
    public int? ExitCode { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ArtifactDirectory { get; init; }
    public string? StandardOutputPath { get; init; }
    public string? StandardErrorPath { get; init; }
    public string? TrxPath { get; init; }
    public IReadOnlyList<string> DiagnosticPaths { get; init; } = [];
}

internal sealed record TestRunAggregate(
    IReadOnlyList<TestExecutionAttempt> Attempts,
    IReadOnlyList<TestCaseAggregate> Tests,
    int RequestedAttemptCount,
    int UnscheduledAttemptCount,
    bool Succeeded);

internal static class TestRunAggregator
{
    public static TestRunAggregate Aggregate(
        IReadOnlyList<string> targetKeys,
        int expectedIterations,
        IReadOnlyList<TestExecutionAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(targetKeys);
        ArgumentNullException.ThrowIfNull(attempts);
        if (targetKeys.Count == 0)
        {
            throw new ArgumentException("At least one target is required.", nameof(targetKeys));
        }
        if (expectedIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedIterations), "Expected iterations must be positive.");
        }

        var expectedTargets = targetKeys.ToHashSet(StringComparer.Ordinal);
        if (expectedTargets.Count != targetKeys.Count)
        {
            throw new ArgumentException("Target keys must be unique.", nameof(targetKeys));
        }

        var seenAttempts = new HashSet<(string Target, int Iteration)>();
        foreach (var attempt in attempts)
        {
            if (!expectedTargets.Contains(attempt.Target.Key) || attempt.Iteration < 1 || attempt.Iteration > expectedIterations ||
                !seenAttempts.Add((attempt.Target.Key, attempt.Iteration)))
            {
                throw new ArgumentException("Attempt identities must reference one requested target and unique requested iteration.", nameof(attempts));
            }
        }

        var aggregates = new List<TestCaseAggregate>();
        foreach (var targetKey in targetKeys)
        {
            var targetAttempts = attempts.Where(attempt => attempt.Target.Key == targetKey).ToArray();
            var testGroups = targetAttempts
                .SelectMany(attempt => attempt.Tests.Select(test => (Attempt: attempt, Test: test)))
                .GroupBy(item => item.Test.StableId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);

            foreach (var group in testGroups)
            {
                var observations = group
                    .Select(item => new TestCaseAttempt(item.Attempt.Iteration, item.Test.Outcome, item.Test.Duration,
                        item.Test.SkipReason, item.Test.ErrorMessage))
                    .OrderBy(static item => item.Iteration)
                    .ToArray();
                var sample = group.First();
                aggregates.Add(new TestCaseAggregate(targetKey, sample.Attempt.Target.Suite, sample.Attempt.Target.Framework,
                    group.Key, sample.Test.TestName, expectedIterations, observations,
                    Classify(observations, expectedIterations)));
            }
        }

        var requested = checked(targetKeys.Count * expectedIterations);
        var testEvidenceSucceeded = aggregates.All(static test => test.Stability is TestStability.AlwaysPasses or TestStability.Skipped);
        var succeeded = attempts.Count == requested &&
            attempts.All(static attempt => attempt.Completed && attempt.Outcome == TestExecutionOutcome.Passed) &&
            testEvidenceSucceeded;
        return new TestRunAggregate(attempts.ToArray(), aggregates, requested, requested - attempts.Count, succeeded);
    }

    private static TestStability Classify(IReadOnlyList<TestCaseAttempt> attempts, int expectedCount)
    {
        if (attempts.Count < expectedCount || attempts.Count == 0)
        {
            return TestStability.Incomplete;
        }

        if (attempts.All(static attempt => attempt.Outcome is TestCaseOutcome.Skipped or TestCaseOutcome.NotRunnable))
        {
            return TestStability.Skipped;
        }

        if (attempts.Any(static attempt => attempt.Outcome is not (TestCaseOutcome.Passed or TestCaseOutcome.Failed or TestCaseOutcome.Error or TestCaseOutcome.Timeout)))
        {
            return TestStability.Incomplete;
        }

        var failed = attempts.Count(static attempt => attempt.Outcome is TestCaseOutcome.Failed or TestCaseOutcome.Error or TestCaseOutcome.Timeout);
        var passed = attempts.Count(static attempt => attempt.Outcome == TestCaseOutcome.Passed);
        if (failed > 0 && passed > 0)
        {
            return TestStability.IntermittentFailure;
        }
        return failed == attempts.Count ? TestStability.AlwaysFails : TestStability.AlwaysPasses;
    }
}
