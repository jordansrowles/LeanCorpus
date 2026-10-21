using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestResultReaderTests
{
    [Fact]
    public async Task ParsesStableCaseIdsOutcomesSkipReasonsAndCounters()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "results.trx");
        await File.WriteAllTextAsync(path, """
            <TestRun>
              <TestDefinitions>
                <UnitTest id="stable-pass"><TestMethod className="Example.Tests" name="Passing" /></UnitTest>
                <UnitTest id="stable-skip"><TestMethod className="Example.Tests" name="Skipping" /></UnitTest>
              </TestDefinitions>
              <Results>
                <UnitTestResult testId="stable-pass" testName="Example.SameName" outcome="Passed" duration="00:00:00.010" />
                <UnitTestResult testId="stable-skip" testName="Example.SameName" outcome="NotRunnable" duration="00:00:00.000">
                  <Output><StdOut>Platform requirement not met</StdOut></Output>
                </UnitTestResult>
              </Results>
              <ResultSummary outcome="Completed"><Counters total="2" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="1" notExecuted="0" /></ResultSummary>
            </TestRun>
            """, TestContext.Current.CancellationToken);

        var result = await TestResultReader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.Valid, result.Error);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Passed);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(["case:Example.Tests|Passing|Example.SameName", "case:Example.Tests|Skipping|Example.SameName"], result.Tests.Select(static test => test.StableId));
        Assert.Equal("stable-pass", result.Tests[0].ReportId);
        Assert.Equal("Example.Tests", result.Tests[0].ClassName);
        Assert.Equal("Passing", result.Tests[0].MethodName);
        Assert.Equal("Platform requirement not met", result.Tests[1].SkipReason);
    }

    [Fact]
    public async Task RejectsCounterMismatchAndMalformedTrxAsIncompleteEvidence()
    {
        using var directory = TemporaryDirectory.Create();
        var mismatchPath = Path.Combine(directory.Path, "mismatch.trx");
        await File.WriteAllTextAsync(mismatchPath, """
            <TestRun><Results><UnitTestResult testId="one" testName="Example.One" outcome="Passed" /></Results>
            <ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" notExecuted="0" /></ResultSummary></TestRun>
            """, TestContext.Current.CancellationToken);
        var malformedPath = Path.Combine(directory.Path, "malformed.trx");
        await File.WriteAllTextAsync(malformedPath, "<TestRun><ResultSummary>", TestContext.Current.CancellationToken);

        var mismatch = await TestResultReader.ReadAsync(mismatchPath, TestContext.Current.CancellationToken);
        var malformed = await TestResultReader.ReadAsync(malformedPath, TestContext.Current.CancellationToken);

        Assert.False(mismatch.Valid);
        Assert.Contains("counter", mismatch.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(malformed.Valid);
        Assert.Contains("invalid", malformed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsNonTerminalSummaryAndMalformedPresentCounter()
    {
        using var directory = TemporaryDirectory.Create();
        var pendingPath = Path.Combine(directory.Path, "pending.trx");
        await File.WriteAllTextAsync(pendingPath, """
            <TestRun><Results><UnitTestResult testId="one" testName="Example.One" outcome="Passed" /></Results>
            <ResultSummary outcome="InProgress"><Counters total="1" passed="1" failed="0" /></ResultSummary></TestRun>
            """, TestContext.Current.CancellationToken);
        var invalidCounterPath = Path.Combine(directory.Path, "counter.trx");
        await File.WriteAllTextAsync(invalidCounterPath, """
            <TestRun><Results><UnitTestResult testId="one" testName="Example.One" outcome="Passed" /></Results>
            <ResultSummary outcome="Completed"><Counters total="1" passed="one" failed="0" /></ResultSummary></TestRun>
            """, TestContext.Current.CancellationToken);

        var pending = await TestResultReader.ReadAsync(pendingPath, TestContext.Current.CancellationToken);
        var invalidCounter = await TestResultReader.ReadAsync(invalidCounterPath, TestContext.Current.CancellationToken);

        Assert.False(pending.Valid);
        Assert.Contains("outcome", pending.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(invalidCounter.Valid);
        Assert.Contains("counter", invalidCounter.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptsTerminalFailedSummaryWhenTheIndividualResultsAndCountersAgree()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "failed.trx");
        await File.WriteAllTextAsync(path, """
            <TestRun><Results><UnitTestResult testId="one" testName="Example.One" outcome="Failed" /></Results>
            <ResultSummary outcome="Failed"><Counters total="1" executed="1" passed="0" failed="1" error="0" timeout="0" /></ResultSummary></TestRun>
            """, TestContext.Current.CancellationToken);

        var result = await TestResultReader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.Valid, result.Error);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task RejectsExternalEntitiesAndReportsMissingFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var secretPath = Path.Combine(directory.Path, "secret.txt");
        await File.WriteAllTextAsync(secretPath, "do not expand", TestContext.Current.CancellationToken);
        var dtdPath = Path.Combine(directory.Path, "external.trx");
        await File.WriteAllTextAsync(dtdPath,
            $"<!DOCTYPE TestRun [<!ENTITY xxe SYSTEM \"{new Uri(secretPath).AbsoluteUri}\">]><TestRun>&xxe;</TestRun>",
            TestContext.Current.CancellationToken);

        var external = await TestResultReader.ReadAsync(dtdPath, TestContext.Current.CancellationToken);
        var missing = await TestResultReader.ReadAsync(Path.Combine(directory.Path, "missing.trx"), TestContext.Current.CancellationToken);

        Assert.False(external.Valid);
        Assert.DoesNotContain("do not expand", external.Error, StringComparison.Ordinal);
        Assert.False(missing.Exists);
        Assert.Contains("missing", missing.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus TRX evidence " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
