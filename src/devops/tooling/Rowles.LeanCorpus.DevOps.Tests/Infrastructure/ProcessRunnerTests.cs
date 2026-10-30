using System.Collections.Concurrent;
using System.Diagnostics;
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
        var expectedWorkingDirectoryName = Path.GetFileName(Path.GetFullPath(directory.Path));
        var childWorkingDirectory = result.StandardOutputTail[1];
        Assert.True(Directory.Exists(childWorkingDirectory));
        Assert.Equal(expectedWorkingDirectoryName, Path.GetFileName(childWorkingDirectory));
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
    public async Task PreservesNulDelimitedUtf8OutputInTheRawLog()
    {
        using var directory = TemporaryDirectory.Create("DevOps raw process output ");
        var result = await new ProcessRunner().RunAsync(
            CreateRequest(directory.Path, "nul-output", "src/core/Ångström parser.cs", "docs/space name.md"),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes("src/core/Ångström parser.cs\0docs/space name.md\0"),
            await File.ReadAllBytesAsync(result.StandardOutputLogPath, TestContext.Current.CancellationToken));
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
    public async Task TimeoutTerminatesTheEntireChildProcessTree()
    {
        using var directory = TemporaryDirectory.Create("DevOps child tree timeout ");
        var pidPath = Path.Combine(directory.Path, "descendant.pid");
        var result = await new ProcessRunner().RunAsync(
            CreateRequest(directory.Path, "spawn-descendant", pidPath) with { Timeout = TimeSpan.FromSeconds(1) },
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Timeout, result.Outcome);
        await AssertDescendantStoppedAsync(pidPath);
    }

    [Fact]
    public async Task CallerCancellationTerminatesTheEntireChildProcessTree()
    {
        using var directory = TemporaryDirectory.Create("DevOps child tree cancellation ");
        var pidPath = Path.Combine(directory.Path, "descendant.pid");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, cancellation.Token);

        var result = await new ProcessRunner().RunAsync(
            CreateRequest(directory.Path, "spawn-descendant", pidPath),
            linked.Token);

        Assert.Equal(OutcomeKind.Cancelled, result.Outcome);
        await AssertDescendantStoppedAsync(pidPath);
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
        var progressEvents = events.ToArray();
        Assert.NotEmpty(progressEvents);
        // Hosted timer resolution can round a nominal interval slightly early.
        var timerResolutionTolerance = TimeSpan.FromMilliseconds(5);
        for (var index = 0; index < progressEvents.Length; index++)
        {
            var earliestExpected = TimeSpan.FromTicks(request.ProgressInterval.Ticks * (index + 1));
            var minimumElapsed = earliestExpected - timerResolutionTolerance;
            Assert.True(progressEvents[index].Elapsed >= minimumElapsed,
                $"Heartbeat {index + 1} arrived at {progressEvents[index].Elapsed:c}; expected no earlier than {minimumElapsed:c} after timer-resolution tolerance.");
            Assert.True(progressEvents[index].Elapsed <= result.Duration,
                $"Heartbeat {index + 1} arrived at {progressEvents[index].Elapsed:c}, after the child duration {result.Duration:c}.");
        }

        Assert.All(progressEvents, item =>
        {
            Assert.True(item.Elapsed > TimeSpan.Zero);
            Assert.Equal(0, item.StandardOutputLineCount);
            Assert.Equal(0, item.StandardErrorLineCount);
            Assert.Null(item.LastStandardOutputLine);
            Assert.Null(item.LastStandardErrorLine);
        });
    }

    [Fact]
    public async Task IncludesLatestChildOutputInProgressHeartbeat()
    {
        using var directory = TemporaryDirectory.Create("DevOps child output heartbeat ");
        var events = new ConcurrentQueue<ProcessProgressEvent>();
        var releasePath = Path.Combine(directory.Path, "heartbeat-observed");
        var request = CreateRequest(directory.Path, "output-handshake", releasePath) with
        {
            Timeout = TimeSpan.FromSeconds(10),
            ProgressInterval = TimeSpan.FromMilliseconds(50),
            Progress = heartbeat =>
            {
                events.Enqueue(heartbeat);
                // Release the child only after the periodic snapshot observes both pipes.
                if (heartbeat.LastStandardOutputLine == "out-ready" &&
                    heartbeat.LastStandardErrorLine == "err-ready")
                {
                    File.WriteAllText(releasePath, "observed");
                }
            }
        };

        var result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.Outcome);
        Assert.Contains(events, heartbeat =>
            heartbeat.LastStandardOutputLine == "out-ready" &&
            heartbeat.LastStandardErrorLine == "err-ready");
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
        var releasePath = Path.Combine(directory.Path, "progress-callback-observed");
        var request = CreateRequest(directory.Path, "output-handshake", releasePath) with
        {
            Timeout = TimeSpan.FromSeconds(5),
            ProgressInterval = TimeSpan.FromMilliseconds(50),
            Progress = _ =>
            {
                File.WriteAllText(releasePath, "observed");
                throw new InvalidOperationException("progress sink failed");
            }
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

    private static async Task AssertDescendantStoppedAsync(string pidPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!File.Exists(pidPath) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(pidPath), "The child did not start its descendant before cancellation.");

        var processId = int.Parse(await File.ReadAllTextAsync(pidPath, TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            while (IsProcessAlive(processId) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.False(IsProcessAlive(processId), $"Descendant process {processId} survived parent termination.");
        }
        finally
        {
            if (IsProcessAlive(processId))
            {
                using var descendant = Process.GetProcessById(processId);
                descendant.Kill(entireProcessTree: true);
                await descendant.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
