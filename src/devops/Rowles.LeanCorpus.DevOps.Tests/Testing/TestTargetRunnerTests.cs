using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestTargetRunnerTests
{
    [Fact]
    public async Task RejectsWrongAdapterBeforeStartingAProcess()
    {
        var context = CreateContext(Path.GetTempPath(), ["exit", "0"]);
        var runner = new RejectProcessRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MtpExecutableTestRunner(runner)
            .RunAsync(context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MtpAdapterAddsOnlyPerTestConsoleSkipReasonsMissingFromTrx()
    {
        using var directory = new TemporaryDirectory();
        var trx = """
            <TestRun>
              <TestDefinitions>
                <UnitTest id="explicit"><TestMethod className="Example.Tests" name="ExplicitCase" /></UnitTest>
                <UnitTest id="unknown"><TestMethod className="Example.Tests" name="UnknownCase" /></UnitTest>
                <UnitTest id="trx"><TestMethod className="Example.Tests" name="TrxReasonCase" /></UnitTest>
                <UnitTest id="ambiguous"><TestMethod className="Example.Tests" name="AmbiguousCase" /></UnitTest>
                <UnitTest id="incomplete-duplicate"><TestMethod className="Example.Tests" name="IncompleteDuplicateCase" /></UnitTest>
              </TestDefinitions>
              <Results>
                <UnitTestResult testId="explicit" testName="Example.Tests.ExplicitCase" outcome="NotRunnable" />
                <UnitTestResult testId="unknown" testName="Example.Tests.UnknownCase" outcome="NotRunnable" />
                <UnitTestResult testId="trx" testName="Example.Tests.TrxReasonCase" outcome="NotRunnable">
                  <Output><StdOut>Reason from TRX</StdOut></Output>
                </UnitTestResult>
                <UnitTestResult testId="ambiguous" testName="Example.Tests.AmbiguousCase" outcome="NotRunnable" />
                <UnitTestResult testId="incomplete-duplicate" testName="Example.Tests.IncompleteDuplicateCase" outcome="NotRunnable" />
              </Results>
              <ResultSummary outcome="Completed"><Counters total="5" executed="0" passed="0" failed="0" error="0" timeout="0" notRunnable="5" notExecuted="0" /></ResultSummary>
            </TestRun>
            """;
        const string standardOutput = """
            skipped Example.Tests.ExplicitCase (0ms)
              Not run (due to explicit test filtering)
            skipped Example.Tests.UnrelatedCase (0ms)
              This reason belongs to another test
            skipped Example.Tests.TrxReasonCase (0ms)
              Console reason must not replace the TRX value
            skipped Example.Tests.AmbiguousCase (0ms)
              First conflicting reason
            skipped Example.Tests.AmbiguousCase (1ms)
              Second conflicting reason
            skipped Example.Tests.IncompleteDuplicateCase (0ms)
              Only one duplicate has a reason
            skipped Example.Tests.IncompleteDuplicateCase (0ms)

            """;
        var runner = new WritingMtpProcessRunner(trx, standardOutput);
        var context = CreateMtpContext(directory.Path);

        var result = await new MtpExecutableTestRunner(runner).RunAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Results!.Valid, result.Results.Error);
        Assert.Equal("Not run (due to explicit test filtering)", result.Results.Tests[0].SkipReason);
        Assert.Null(result.Results.Tests[1].SkipReason);
        Assert.Equal("Reason from TRX", result.Results.Tests[2].SkipReason);
        Assert.Null(result.Results.Tests[3].SkipReason);
        Assert.Null(result.Results.Tests[4].SkipReason);
    }

    [Fact]
    public async Task NativeAdapterPreservesRealProcessFailureWithoutTrx()
    {
        using var directory = new TemporaryDirectory();
        var result = await new NativeAotTestRunner(new ProcessRunner()).RunAsync(
            CreateContext(directory.Path, ["exit", "7"]), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.Process.Outcome);
        Assert.Equal(7, result.Process.RawExitCode);
        Assert.Null(result.Results);
        Assert.Null(result.TrxPath);
        Assert.Equal(TestExecutionOutcome.AssertionFailure,
            TestOutcomeClassifier.Classify("AotNative", result.Process, result.Results));
    }

    [Fact]
    public async Task NativeAdapterHonoursProcessTimeout()
    {
        using var directory = new TemporaryDirectory();
        var context = CreateContext(directory.Path, ["sleep", "30000"]) with { Timeout = TimeSpan.FromMilliseconds(200) };
        var result = await new NativeAotTestRunner(new ProcessRunner()).RunAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Timeout, result.Process.Outcome);
        Assert.Equal(TestExecutionOutcome.Hung, TestOutcomeClassifier.Classify("AotNative", result.Process, null));
        Assert.True(File.Exists(result.Process.StandardOutputLogPath));
    }

    [Fact]
    public async Task NativeAdapterPreservesCancellation()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(500));
        var result = await new NativeAotTestRunner(new ProcessRunner()).RunAsync(
            CreateContext(directory.Path, ["sleep", "30000"]), cancellation.Token);

        Assert.Equal(OutcomeKind.Cancelled, result.Process.Outcome);
        Assert.Equal(TestExecutionOutcome.Cancelled, TestOutcomeClassifier.Classify("AotNative", result.Process, null));
    }

    private static TestRunnerContext CreateContext(string directory, IReadOnlyList<string> arguments)
    {
        // A regular process fixture exercises the adapter lifecycle; this does not qualify a Native AOT publication.
        var executableName = OperatingSystem.IsWindows() ? "devops-process-test-child.exe" : "devops-process-test-child";
        var configuration = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var child = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..",
            "Rowles.LeanCorpus.DevOps.ProcessTestChild", configuration, executableName));
        var target = new TestTarget("native-fixture/net10.0", "Process fixture", "native-fixture", "AotNative",
            "fixture.csproj", "net10.0", "Release", "", "", [], false, [], arguments);
        return new TestRunnerContext(Guid.NewGuid(), new PreparedTestTarget(target, "fixture.csproj", child),
            new TestRunnerOptions(directory), directory, directory, 1, null);
    }

    private static TestRunnerContext CreateMtpContext(string directory)
    {
        var target = new TestTarget("core/net10.0", "Core", "core", "Mtp", "core.csproj", "net10.0",
            "Release", string.Empty, string.Empty, [], false, [], []);
        return new TestRunnerContext(Guid.NewGuid(), new PreparedTestTarget(target, "core.csproj", "core-apphost"),
            new TestRunnerOptions(directory) { ArtifactsEnabled = true, ExplicitMode = "off" }, directory, directory, 1, null);
    }

    private sealed class WritingMtpProcessRunner(string trx, string standardOutput) : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            await File.WriteAllTextAsync(request.StandardOutputLogPath, standardOutput, cancellationToken);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, string.Empty, cancellationToken);
            var reportDirectoryIndex = request.Arguments.ToList().IndexOf("--results-directory");
            Assert.True(reportDirectoryIndex >= 0);
            var reportDirectory = request.Arguments[reportDirectoryIndex + 1];
            await File.WriteAllTextAsync(System.IO.Path.Combine(reportDirectory, "results.trx"), trx, cancellationToken);
            return new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.FromMilliseconds(5),
                request.StandardOutputLogPath, request.StandardErrorLogPath, null);
        }
    }

    private sealed class RejectProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Unexpected process invocation.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LeanCorpus test adapter " + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
