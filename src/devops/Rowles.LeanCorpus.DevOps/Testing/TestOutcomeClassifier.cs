using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal enum TestExecutionOutcome
{
    Passed,
    AssertionFailure,
    DiscoveryFailure,
    Crashed,
    Hung,
    Cancelled,
    Incomplete,
    InfrastructureFailure
}

internal static class TestOutcomeClassifier
{
    public static TestExecutionOutcome Classify(
        string runnerKind,
        ProcessResult process,
        TestResultData? resultData,
        bool resultsRequired = true,
        IReadOnlyList<string>? diagnosticPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerKind);
        ArgumentNullException.ThrowIfNull(process);

        if (process.Outcome == OutcomeKind.Cancelled)
        {
            return TestExecutionOutcome.Cancelled;
        }
        if (process.Outcome == OutcomeKind.Timeout)
        {
            return TestExecutionOutcome.Hung;
        }
        if (process.Outcome is OutcomeKind.InfrastructureFailure or OutcomeKind.ValidationFailure)
        {
            return TestExecutionOutcome.InfrastructureFailure;
        }
        if (IsCrashExitCode(process.RawExitCode))
        {
            return TestExecutionOutcome.Crashed;
        }

        if (runnerKind.Equals("AotNative", StringComparison.OrdinalIgnoreCase))
        {
            return process.RawExitCode switch
            {
                0 when process.Outcome == OutcomeKind.Success => TestExecutionOutcome.Passed,
                null => TestExecutionOutcome.Incomplete,
                _ when process.Outcome == OutcomeKind.CommandFailure => TestExecutionOutcome.AssertionFailure,
                _ => TestExecutionOutcome.InfrastructureFailure
            };
        }
        if (!runnerKind.Equals("Mtp", StringComparison.OrdinalIgnoreCase))
        {
            return TestExecutionOutcome.InfrastructureFailure;
        }

        if (!resultsRequired && resultData is null)
        {
            return process.RawExitCode switch
            {
                0 when process.Outcome == OutcomeKind.Success => TestExecutionOutcome.Passed,
                null => TestExecutionOutcome.Incomplete,
                _ when process.Outcome == OutcomeKind.CommandFailure => TestExecutionOutcome.DiscoveryFailure,
                _ => TestExecutionOutcome.InfrastructureFailure
            };
        }

        if (resultData is null || !resultData.Exists || !resultData.Valid || resultData.Total == 0 || resultData.Tests.Count != resultData.Total)
        {
            foreach (var path in diagnosticPaths ?? [])
            {
                var name = Path.GetFileName(path);
                if (name.Contains("hang", StringComparison.OrdinalIgnoreCase))
                {
                    return TestExecutionOutcome.Hung;
                }
                if (name.Contains("crash", StringComparison.OrdinalIgnoreCase))
                {
                    return TestExecutionOutcome.Crashed;
                }
            }
            return TestExecutionOutcome.Incomplete;
        }

        if (resultData.Tests.Any(static test => test.Outcome is TestCaseOutcome.Failed or TestCaseOutcome.Error or TestCaseOutcome.Timeout))
        {
            return TestExecutionOutcome.AssertionFailure;
        }

        if (resultData.Tests.Any(static test => test.Outcome is TestCaseOutcome.NotExecuted or TestCaseOutcome.Aborted or TestCaseOutcome.Inconclusive))
        {
            return TestExecutionOutcome.Incomplete;
        }

        return process.RawExitCode switch
        {
            0 when process.Outcome == OutcomeKind.Success => TestExecutionOutcome.Passed,
            null => TestExecutionOutcome.Incomplete,
            _ when process.Outcome == OutcomeKind.CommandFailure => TestExecutionOutcome.DiscoveryFailure,
            _ => TestExecutionOutcome.InfrastructureFailure
        };
    }

    private static bool IsCrashExitCode(int? exitCode)
    {
        if (exitCode is null)
        {
            return false;
        }

        if (exitCode is 6 or 11 or 134 or 137 or 139)
        {
            return true;
        }

        var unsignedExitCode = unchecked((uint)exitCode.Value);
        return unsignedExitCode is 3221225477 or 3221225786 or 3221226505;
    }
}
