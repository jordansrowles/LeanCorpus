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

        Assert.Equal(TestExecutionOutcome.InfrastructureFailure, result);
    }

    [Fact]
    public void ValidEmptyTrxIsDiscoveryFailure()
    {
        var emptyResults = new TestResultData(true, true, 0, 0, 0, 0, [], null);

        var result = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.Success, 0), emptyResults);

        Assert.Equal(TestExecutionOutcome.DiscoveryFailure, result);
    }

    [Fact]
    public void MissingOrMalformedTrxCannotBeCalledDiscoveryFailure()
    {
        var missing = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), resultData: null);
        var malformed = TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1),
            new TestResultData(true, false, 1, 0, 0, 0, [], "malformed TRX"));

        Assert.Equal(TestExecutionOutcome.Incomplete, missing);
        Assert.Equal(TestExecutionOutcome.Incomplete, malformed);
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
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), resultData: null, diagnosticPaths: ["hangdump.dmp"]));
        Assert.Equal(TestExecutionOutcome.Crashed,
            TestOutcomeClassifier.Classify("Mtp", Process(OutcomeKind.CommandFailure, 1), resultData: null, diagnosticPaths: ["crashdump.dmp"]));
    }

    [Fact]
    public void NativeAotUsesItsProcessResultWithoutRequiringTrx()
    {
        Assert.Equal(TestExecutionOutcome.Passed,
            TestOutcomeClassifier.Classify("AotNative", Process(OutcomeKind.Success, 0), resultData: null));
        Assert.Equal(TestExecutionOutcome.AssertionFailure,
            TestOutcomeClassifier.Classify("AotNative", Process(OutcomeKind.CommandFailure, 1), resultData: null));
    }

    [Theory]
    [InlineData(3, (int)TestExecutionOutcome.Cancelled)]
    [InlineData(4, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(5, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(6, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(9, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(10, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(12, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(14, (int)TestExecutionOutcome.InfrastructureFailure)]
    [InlineData(7, (int)TestExecutionOutcome.Crashed)]
    [InlineData(11, (int)TestExecutionOutcome.Incomplete)]
    public void MtpSessionExitCodesAreNotUnixSignalNumbers(int exitCode, int expected)
    {
        Assert.Equal((TestExecutionOutcome)expected, TestOutcomeClassifier.Classify("Mtp",
            Process(OutcomeKind.CommandFailure, exitCode), MissingResults()));
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
