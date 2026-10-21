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
