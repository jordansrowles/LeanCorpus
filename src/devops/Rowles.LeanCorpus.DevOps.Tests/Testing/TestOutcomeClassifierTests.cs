using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestOutcomeClassifierTests
{
    [Fact]
    public void MtpRequiresCompleteTrxEvenWhenTheChildExitsZero()
    {
        var result = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.Success, 0), MissingResults());

        Assert.Equal(TestExecutionOutcome.Incomplete, result);
    }

    [Fact]
    public void NonZeroMtpExitCannotBeOverruledByPassingTrx()
    {
        var result = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), Results(TestCaseOutcome.Passed));

        Assert.Equal(TestExecutionOutcome.DiscoveryFailure, result);
    }

    [Fact]
    public void FailedCasesRemainAssertionFailuresWhenTheChildExitIsNonZero()
    {
        var result = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), Results(TestCaseOutcome.Failed));

        Assert.Equal(TestExecutionOutcome.AssertionFailure, result);
    }

    [Fact]
    public void CrashTimeoutAndCancellationAreNotReportedAsAssertions()
    {
        Assert.Equal(TestExecutionOutcome.Crashed,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 139), MissingResults()));
        Assert.Equal(TestExecutionOutcome.Hung,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.Timeout, null), MissingResults()));
        Assert.Equal(TestExecutionOutcome.Cancelled,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.Cancelled, null), MissingResults()));
    }

    [Fact]
    public void MissingTrxWithRetainedHangOrCrashDumpKeepsTheDiagnosticOutcome()
    {
        Assert.Equal(TestExecutionOutcome.Hung,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), MissingResults(), diagnosticPaths: ["hangdump.dmp"]));
        Assert.Equal(TestExecutionOutcome.Crashed,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), MissingResults(), diagnosticPaths: ["crashdump.dmp"]));
    }

    [Fact]
    public void NativeAotUsesItsProcessResultWithoutRequiringTrx()
    {
        Assert.Equal(TestExecutionOutcome.Passed,
            TestOutcomeClassifier.Classify("AotNative", Process(OutcomeKind.Success, 0), resultData: null));
        Assert.Equal(TestExecutionOutcome.AssertionFailure,
            TestOutcomeClassifier.Classify("AotNative", Process(OutcomeKind.CommandFailure, 1), resultData: null));
    }

    private static ProcessResult Process(OutcomeKind outcome, int? exitCode)
        => new(outcome, exitCode, [], [], 0, 0, TimeSpan.Zero, "stdout.log", "stderr.log", null);

    private static TestResultData MissingResults()
        => new(false, false, 0, 0, 0, 0, [], "missing TRX");

    private static TestResultData Results(TestCaseOutcome outcome)
    {
        var test = new TestCaseResult("stable-id", "Example.Test", outcome, null, null, TimeSpan.Zero);
        var failed = outcome is TestCaseOutcome.Failed or TestCaseOutcome.Error or TestCaseOutcome.Timeout ? 1 : 0;
        var skipped = outcome is TestCaseOutcome.Skipped or TestCaseOutcome.NotExecuted ? 1 : 0;
        var passed = outcome == TestCaseOutcome.Passed ? 1 : 0;
        return new TestResultData(true, true, 1, passed, failed, skipped, [test], null);
    }
}
