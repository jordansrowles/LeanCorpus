using System.Collections.Concurrent;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Infrastructure;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task DrainsHighVolumeStandardOutputAndStandardErrorConcurrently()
    {
        using var directory = TemporaryDirectory.Create("DevOps process output ");
        var result = await new ProcessRunner().RunAsync(CreateRequest(directory.Path, "noisy", "12000"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(0, result.RawExitCode);
        Assert.Equal(12_000, result.StandardOutputLineCount);
        Assert.Equal(12_000, result.StandardErrorLineCount);
        Assert.Contains("out-11999", result.StandardOutputTail);
        Assert.Contains("err-11999", result.StandardErrorTail);
        Assert.Equal(12_000, File.ReadLines(result.StandardOutputLogPath).Count());
        Assert.Equal(12_000, File.ReadLines(result.StandardErrorLogPath).Count());
    }

    [Fact]
    public async Task KeepsFullLongLineOnDiskAndBoundsReturnedTail()
    {
        using var directory = TemporaryDirectory.Create("DevOps long output ");
        var request = CreateRequest(directory.Path, "long-line", "131072") with { MaxTailCharacters = 1024 };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.True(new FileInfo(result.StandardOutputLogPath).Length >= 131_072);
        Assert.True(result.StandardOutputTail.Sum(line => line.Length) <= 1024);
    }

    [Fact]
    public async Task PreservesArgumentTokensAndWorkingDirectoryContainingSpaces()
    {
        using var directory = TemporaryDirectory.Create("DevOps child working directory ");
        var request = CreateRequest(directory.Path, "echo", "one argument", "path/with spaces", "λ-value");

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(2, result.StandardOutputTail.Count);
        var echoed = JsonSerializer.Deserialize<string[]>(result.StandardOutputTail[0]);
        Assert.NotNull(echoed);
        Assert.Equal(["one argument", "path/with spaces", "λ-value"], echoed!);
        Assert.Equal(Path.GetFullPath(directory.Path), result.StandardOutputTail[1]);
    }

    [Fact]
    public async Task AppliesScopedEnvironmentVariablesToChildProcess()
    {
        using var directory = TemporaryDirectory.Create("DevOps child environment ");
        var request = CreateRequest(directory.Path, "environment", "LEANCORPUS_DEVOPS_PROCESS_TEST") with
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LEANCORPUS_DEVOPS_PROCESS_TEST"] = "scoped-value"
            }
        };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal("scoped-value", Assert.Single(result.StandardOutputTail));
    }

    [Fact]
    public async Task ForwardsExplicitStandardInputText()
    {
        using var directory = TemporaryDirectory.Create("DevOps child stdin ");
        var request = CreateRequest(directory.Path, "stdin") with { StandardInputText = "remote script\nwith exact lines\n" };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(["remote script", "with exact lines"], result.StandardOutputTail);
    }

    [Fact]
    public async Task RetainsRawNonZeroChildExitCode()
    {
        using var directory = TemporaryDirectory.Create("DevOps child exit ");
        var result = await new ProcessRunner().RunAsync(CreateRequest(directory.Path, "exit", "23"), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.Outcome);
        Assert.Equal(23, result.RawExitCode);
    }

    [Fact]
    public async Task ClassifiesTimeoutAndTerminatesTheChild()
    {
        using var directory = TemporaryDirectory.Create("DevOps child timeout ");
        var request = CreateRequest(directory.Path, "sleep", "10000") with { Timeout = TimeSpan.FromMilliseconds(250) };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Timeout, result.Outcome);
        Assert.Null(result.RawExitCode);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ClassifiesCallerCancellationAndTerminatesTheChild()
    {
        using var directory = TemporaryDirectory.Create("DevOps child cancellation ");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, cancellation.Token);
        var result = await new ProcessRunner().RunAsync(CreateRequest(directory.Path, "sleep", "10000"), linked.Token);

        Assert.Equal(OutcomeKind.Cancelled, result.Outcome);
        Assert.Null(result.RawExitCode);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopsReadinessProbeChildAsACompletedSmokeRun()
    {
        using var directory = TemporaryDirectory.Create("DevOps readiness child ");
        var request = CreateRequest(directory.Path, "readiness", "10000") with
        {
            Timeout = TimeSpan.FromSeconds(5),
            StopOnStandardOutputPrefix = "Now listening: http://127.0.0.1:"
        };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.True(result.StoppedByCondition);
        Assert.Null(result.RawExitCode);
        Assert.Contains(result.StandardOutputTail, line => line.TrimStart().StartsWith("Now listening: http://127.0.0.1:", StringComparison.Ordinal));
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmitsBoundedHeartbeatProgressWhileChildIsSilent()
    {
        using var directory = TemporaryDirectory.Create("DevOps child heartbeat ");
        var events = new ConcurrentQueue<ProcessProgressEvent>();
        var request = CreateRequest(directory.Path, "silent", "700") with
        {
            ProgressInterval = TimeSpan.FromMilliseconds(100),
            Progress = events.Enqueue
        };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.InRange(events.Count, 4, 8);
        Assert.All(events, item => Assert.True(item.Elapsed > TimeSpan.Zero));
    }

    [Fact]
    public async Task ClassifiesInvalidLogPathAsValidationFailure()
    {
        using var directory = TemporaryDirectory.Create("DevOps invalid process path ");
        var request = CreateRequest(directory.Path, "exit", "0") with { StandardOutputLogPath = "\0invalid.log" };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.Outcome);
        Assert.Null(result.RawExitCode);
    }

    [Fact]
    public async Task ReportsProgressCallbackFailureAsInfrastructureFailureAndRetainsChildExit()
    {
        using var directory = TemporaryDirectory.Create("DevOps progress callback failure ");
        var request = CreateRequest(directory.Path, "silent", "300") with
        {
            ProgressInterval = TimeSpan.FromMilliseconds(50),
            Progress = _ => throw new InvalidOperationException("progress sink failed")
        };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.Outcome);
        Assert.Equal(0, result.RawExitCode);
        Assert.Contains("progress sink failed", result.Failure, StringComparison.Ordinal);
    }

    private static ProcessRequest CreateRequest(string workingDirectory, params string[] arguments)
    {
        var childPath = GetChildPath();
        return new ProcessRequest
        {
            ExecutablePath = childPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            StandardOutputLogPath = Path.Combine(workingDirectory, "stdout.log"),
            StandardErrorLogPath = Path.Combine(workingDirectory, "stderr.log"),
            MaxTailLines = 20,
            MaxTailCharacters = 4096,
            ProgressInterval = TimeSpan.FromSeconds(5)
        };
    }

    private static string GetChildPath()
    {
        var executableName = OperatingSystem.IsWindows() ? "devops-process-test-child.exe" : "devops-process-test-child";
        var configuration = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Rowles.LeanCorpus.DevOps.ProcessTestChild", configuration, executableName));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create(string prefix)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
