using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestExecutorTests
{
    [Fact]
    public async Task RunsPreparedMtpApphostSeriallyAndWritesAttemptReports()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = checkout.CreateTarget("core") with { RequiresBrowser = true };
        var processRunner = checkout.CreateRunner();
        var progressEvents = new List<TestProgressEvent>();
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader(), progressEvents.Add);

        var result = await service.RunAsync(Request(checkout, [target], count: 2), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.True(result.Aggregate!.Succeeded);
        Assert.Equal(2, result.Aggregate.Attempts.Count);
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
        var diagnosticPaths = result.Aggregate.Attempts[0].DiagnosticPaths;
        Assert.Contains(diagnosticPaths, path => path.EndsWith(Path.Combine("telemetry", "summary.json"), StringComparison.Ordinal));
        Assert.Contains(diagnosticPaths, path => path.EndsWith(Path.Combine("telemetry", "tests.ndjson"), StringComparison.Ordinal));
        Assert.DoesNotContain(diagnosticPaths, path => Path.GetFileName(path) == "results.ctrf.json");
        Assert.Equal(2, processRunner.TestInvocations.Count);
        Assert.All(processRunner.TestInvocations, invocation => Assert.Equal(checkout.AppHost("core"), invocation.ExecutablePath));
        Assert.All(processRunner.TestInvocations, invocation => Assert.Contains("--report-xunit-trx", invocation.Arguments));
        var runStarted = Assert.Single(progressEvents, static item => item.Kind == TestEventKind.RunStarted);
        Assert.Contains("core/net10.0 (runner=Mtp, browserRequired=true)", runStarted.Message, StringComparison.Ordinal);
        var manifest = Assert.IsType<RunManifest>(result.Manifest);
        Assert.Equal("10.0.100", manifest.ToolVersions["dotnet-sdk-requested"]);
        Assert.Equal("10.0.103", manifest.ToolVersions["dotnet-sdk-selected"]);
        Assert.Equal("latestPatch", manifest.ToolVersions["dotnet-sdk-roll-forward"]);
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "report.json")));
        var markdown = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("# Test run summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Example\\|Test, \"Quoted\"", markdown, StringComparison.Ordinal);
        Assert.Contains("## Telemetry summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Executions: 2; tests: 2 finished of 2 started (2 passed, 0 failed, 0 skipped).", markdown, StringComparison.Ordinal);
        Assert.Contains("Activities: 4; metrics: 2; swallowed exceptions: 0; orphaned activities: 0; telemetry errors: 0.", markdown, StringComparison.Ordinal);
        Assert.Contains("Retained: `Telemetry test` (Passed).", markdown, StringComparison.Ordinal);
        var timings = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "timings.csv"), TestContext.Current.CancellationToken);
        Assert.StartsWith("\"recordType\",\"iteration\",\"suite\",\"framework\",\"target\",\"test\",\"outcome\",\"durationMs\"\n", timings, StringComparison.Ordinal);
        Assert.Contains("\"Target\",\"1\",\"core\",\"net10.0\",\"core/net10.0\",\"\",\"Passed\"", timings, StringComparison.Ordinal);
        Assert.Contains("\"Test\",\"1\",\"core\",\"net10.0\",\"core/net10.0\",\"Example|Test, \"\"Quoted\"\"\",\"Passed\"", timings, StringComparison.Ordinal);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.json"), TestContext.Current.CancellationToken));
        var telemetry = report.RootElement.GetProperty("telemetry");
        var selectedTarget = Assert.Single(report.RootElement.GetProperty("selectedTargets").EnumerateArray().ToArray());
        Assert.True(selectedTarget.GetProperty("requiresBrowser").GetBoolean());
        Assert.Equal(2, telemetry.GetProperty("executions").GetInt32());
        Assert.Equal(2, telemetry.GetProperty("testsStarted").GetInt64());
        Assert.Equal(4, telemetry.GetProperty("activities").GetInt64());
        Assert.Equal(2, telemetry.GetProperty("retainedTests").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "state.json")));
        var events = await File.ReadAllLinesAsync(Path.Combine(result.RunDirectory, "events.ndjson"), TestContext.Current.CancellationToken);
        Assert.Contains(events, line => line.Contains("targetDiscovered", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(events, line => line.Contains("checkpoint", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, events.Count(line => line.Contains("targetStarted", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, events.Count(line => line.Contains("targetCompleted", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task DoesNotPrepareTestsWhenSdkPolicyCannotBeResolved()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = checkout.CreateTarget("core");
        var processRunner = checkout.CreateRunner();
        checkout.DeleteSdkPolicy();
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader());

        var result = await service.RunAsync(Request(checkout, [target]), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.Empty(result.Aggregate!.Attempts);
        Assert.Empty(processRunner.TestInvocations);
        Assert.DoesNotContain(processRunner.Requests,
            request => request.Arguments.Contains("-getProperty:TargetPath", StringComparer.Ordinal));
        Assert.Equal(RunState.Failed, result.Manifest!.State);
    }

    [Fact]
    public async Task RestoreFailureWithNoCompletedExecutionsIsReportedAsIncomplete()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = checkout.CreateTarget("core");
        var processRunner = checkout.CreateRunner(failRestore: true);
        var progressEvents = new List<TestProgressEvent>();
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader(), progressEvents.Add);
        var request = Request(checkout, [target]) with
        {
            RunnerOptions = new TestRunnerOptions(checkout.Root)
            {
                Ci = false,
                ParallelProfile = "integration",
                ArtifactsEnabled = true,
                HangTimeout = "off"
            }
        };

        var result = await service.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Empty(result.Aggregate!.Attempts);
        Assert.Empty(processRunner.TestInvocations);
        var completedEvent = Assert.Single(progressEvents, item => item.Kind == TestEventKind.RunCompleted);
        Assert.Equal(TestExecutionOutcome.Incomplete, completedEvent.Outcome);
    }

    [Fact]
    public async Task RejectsOverridesOfDevOpsOwnedEnvironmentBeforeCreatingRun()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = checkout.CreateTarget("core");
        var processRunner = checkout.CreateRunner();
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader());
        var request = Request(checkout, [target]) with
        {
            RunnerOptions = new TestRunnerOptions(checkout.Root)
            {
                Ci = true,
                ParallelProfile = "integration",
                ArtifactsEnabled = true,
                EnvironmentOverrides = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["LEANCORPUS_RUN_ID"] = "caller-value"
                }
            }
        };

        var result = await service.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.Null(result.Manifest);
        Assert.Null(result.Aggregate);
        Assert.Empty(processRunner.TestInvocations);
        Assert.False(Directory.Exists(paths.GetRunRoot("test")));
    }

    [Fact]
    public async Task FailFastPreservesFailedAttemptAndRecordsUnscheduledTargets()
    {
        using var checkout = TemporaryCheckout.Create();
        var first = checkout.CreateTarget("core");
        var second = checkout.CreateTarget("text");
        var processRunner = checkout.CreateRunner(failingSuites: new HashSet<string>(["core"], StringComparer.Ordinal));
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader());

        var result = await service.RunAsync(Request(checkout, [first, second], failFast: true), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(TestExecutionOutcome.AssertionFailure, Assert.Single(result.Aggregate!.Attempts).Outcome);
        Assert.Equal(1, result.Aggregate.UnscheduledAttemptCount);
        Assert.Single(processRunner.TestInvocations);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.json"), TestContext.Current.CancellationToken));
        Assert.Equal(1, report.RootElement.GetProperty("unscheduledExecutions").GetInt32());
        var unscheduled = Assert.Single(report.RootElement.GetProperty("unscheduledTargets").EnumerateArray());
        Assert.Equal("text/net10.0", unscheduled.GetProperty("target").GetString());
        Assert.Equal(1, unscheduled.GetProperty("iteration").GetInt32());
        var markdown = await File.ReadAllTextAsync(Path.Combine(result.RunDirectory, "report.md"), TestContext.Current.CancellationToken);
        Assert.Contains("- Result: **Not passed** (CommandFailure)", markdown, StringComparison.Ordinal);
        Assert.Contains("## Unscheduled targets", markdown, StringComparison.Ordinal);
        Assert.Contains("- iteration 1: `text/net10.0`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroExitWithoutRequestedTrxIsIncompleteEvidence()
    {
        using var checkout = TemporaryCheckout.Create();
        var target = checkout.CreateTarget("core");
        var processRunner = checkout.CreateRunner(writeTrx: false);
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader());

        var result = await service.RunAsync(Request(checkout, [target]), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(TestExecutionOutcome.Incomplete, Assert.Single(result.Aggregate!.Attempts).Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
    }

    [Fact]
    public async Task RunsNativeAotBinaryDirectlyWithoutRequiringTrx()
    {
        using var checkout = TemporaryCheckout.Create();
        var project = Path.Combine("src", "devops", "Rowles.LeanCorpus.Tests.AOTSmoke", "Rowles.LeanCorpus.Tests.AOTSmoke.csproj");
        var projectPath = Path.Combine(checkout.Root, project);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project />");
        var target = new TestTarget("aot/net10.0/linux-x64", "AOT", "aot", "AotNative", project,
            "net10.0", "Release", "linux-x64", "", [], false, [], ["--filter", "Area=Search"]);
        var processRunner = new AotRecordingProcessRunner(checkout.Root);
        var paths = new ArtifactPaths(checkout.Root);
        var service = new TestExecutor(paths, new ArtifactRunStore(paths, runCategory: "test"), processRunner,
            new DotNetSdkResolver(processRunner), new FixedProvenanceReader());

        var result = await service.RunAsync(Request(checkout, [target]), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.True(result.Aggregate!.Succeeded);
        Assert.Equal(4, processRunner.Requests.Count);
        Assert.Equal("--version", processRunner.Requests[0].Arguments[0]);
        Assert.Equal("--list-sdks", processRunner.Requests[1].Arguments[0]);
        var publishInvocation = processRunner.Requests[2];
        Assert.Equal("publish", publishInvocation.Arguments[0]);
        Assert.Contains("--self-contained", publishInvocation.Arguments);
        Assert.Contains("true", publishInvocation.Arguments);
        var nativeInvocation = processRunner.Requests[3];
        Assert.DoesNotContain("dotnet", nativeInvocation.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["--filter", "Area=Search"], nativeInvocation.Arguments);
        Assert.Null(Assert.Single(result.Aggregate.Attempts).TrxPath);
    }

    private static TestExecutorRequest Request(TemporaryCheckout checkout, IReadOnlyList<TestTarget> targets,
        int count = 1, bool failFast = false)
        => new()
        {
            Checkout = checkout.Identity,
            Targets = targets,
            RunnerOptions = new TestRunnerOptions(checkout.Root)
            {
                Ci = true,
                ParallelProfile = "integration",
                ArtifactsEnabled = true,
                HangTimeout = "off"
            },
            Count = count,
            FailFast = failFast
        };

    private sealed class FixedProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "a".PadRight(40, 'a'), "issue-97", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private readonly Dictionary<string, string> _targetPaths = new(StringComparer.Ordinal);
        private readonly List<string> _appHosts = [];

        private TemporaryCheckout(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "global.json"), """{"sdk":{"version":"10.0.100","rollForward":"latestPatch"}}""");
            Directory.CreateDirectory(Path.Combine(SdkBaseDirectory, SelectedSdkVersion));
            File.WriteAllText(Path.Combine(SdkBaseDirectory, SelectedSdkVersion, "MSBuild.dll"), "test SDK marker");
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        private const string SelectedSdkVersion = "10.0.103";

        public string Root { get; }
        public CheckoutIdentity Identity { get; }
        private string SdkBaseDirectory => Path.Combine(Root, "test-sdks");

        public string AppHost(string suite)
            => _appHosts.Single(path => Path.GetFileName(path).StartsWith(suite, StringComparison.Ordinal));

        public TestTarget CreateTarget(string suite)
        {
            var project = Path.Combine("src", "tests", suite + ".csproj");
            var projectPath = Path.Combine(Root, project);
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, "<Project />");
            var output = Path.Combine(Root, "artifacts", "bin", suite, "release", "net10.0");
            Directory.CreateDirectory(output);
            var assemblyPath = Path.Combine(output, suite + ".dll");
            var appHost = Path.Combine(output, OperatingSystem.IsWindows() ? suite + ".exe" : suite);
            File.WriteAllText(assemblyPath, "fake assembly");
            File.WriteAllText(appHost, "fake apphost");
            _appHosts.Add(appHost);
            _targetPaths.Add(Path.GetFullPath(projectPath), assemblyPath);
            return new TestTarget(suite + "/net10.0", suite, suite, "Mtp", project, "net10.0", "Release", "", "",
                [], false, ["Trx"], []);
        }

        public RecordingProcessRunner CreateRunner(IReadOnlySet<string>? failingSuites = null, bool writeTrx = true,
            bool failRestore = false)
            => new(_targetPaths, failingSuites ?? new HashSet<string>(StringComparer.Ordinal), writeTrx,
                SelectedSdkVersion, SdkBaseDirectory, failRestore);

        public void DeleteSdkPolicy()
            => File.Delete(Path.Combine(Root, "global.json"));

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LeanCorpus managed test executor " + Guid.NewGuid().ToString("N"));
            return new TemporaryCheckout(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingProcessRunner(
        IReadOnlyDictionary<string, string> targetPaths,
        IReadOnlySet<string> failingSuites,
        bool writeTrx,
        string selectedSdkVersion,
        string sdkBaseDirectory,
        bool failRestore) : IProcessRunner
    {
        public List<ProcessRequest> TestInvocations { get; } = [];
        public List<ProcessRequest> Requests { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardErrorLogPath)!);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, string.Empty, cancellationToken);
            if (request.ExecutablePath == "dotnet" && request.Arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
            {
                await File.WriteAllTextAsync(request.StandardOutputLogPath, selectedSdkVersion, cancellationToken);
                return Process(request, OutcomeKind.Success, 0, [selectedSdkVersion]);
            }
            if (request.ExecutablePath == "dotnet" && request.Arguments.SequenceEqual(["--list-sdks"], StringComparer.Ordinal))
            {
                var line = $"{selectedSdkVersion} [{sdkBaseDirectory}]";
                await File.WriteAllTextAsync(request.StandardOutputLogPath, line, cancellationToken);
                return Process(request, OutcomeKind.Success, 0, [line]);
            }
            if (failRestore && request.ExecutablePath == "dotnet" && request.Arguments.FirstOrDefault() == "restore")
            {
                await File.WriteAllTextAsync(request.StandardErrorLogPath, "simulated restore failure", cancellationToken);
                return Process(request, OutcomeKind.CommandFailure, 1);
            }
            if (request.ExecutablePath == "dotnet" && request.Arguments.Contains("-getProperty:TargetPath", StringComparer.Ordinal))
            {
                var project = Path.GetFullPath(request.Arguments[1]);
                await File.WriteAllTextAsync(request.StandardOutputLogPath, targetPaths[project], cancellationToken);
                return Process(request, OutcomeKind.Success, 0);
            }

            TestInvocations.Add(request);
            await File.WriteAllTextAsync(request.StandardOutputLogPath, "test child", cancellationToken);
            var suite = Path.GetFileName(request.ExecutablePath).Split('.')[0];
            var failing = failingSuites.Contains(suite);
            var resultDirectoryIndex = request.Arguments.ToList().IndexOf("--results-directory");
            if (writeTrx && resultDirectoryIndex >= 0)
            {
                var resultDirectory = request.Arguments[resultDirectoryIndex + 1];
                Directory.CreateDirectory(resultDirectory);
                var outcome = failing ? "Failed" : "Passed";
                var passed = failing ? "0" : "1";
                var failed = failing ? "1" : "0";
                var trx = $"""
                    <TestRun><Results><UnitTestResult testId="stable" testName="Example|Test, &quot;Quoted&quot;" outcome="{outcome}" duration="00:00:00.010" /></Results>
                    <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="{passed}" failed="{failed}" error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" disconnected="0" passedButRunAborted="0" inProgress="0" pending="0" /></ResultSummary></TestRun>
                    """;
                await File.WriteAllTextAsync(Path.Combine(resultDirectory, "results.trx"), trx, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(resultDirectory, "results.ctrf.json"), "{}", cancellationToken);
                if (request.EnvironmentVariables is { } variables &&
                    variables.TryGetValue("LEANCORPUS_ARTIFACT_DIR", out var artifactDirectory) &&
                    !string.IsNullOrWhiteSpace(artifactDirectory))
                {
                    var telemetryDirectory = Path.Combine(artifactDirectory, "telemetry");
                    Directory.CreateDirectory(telemetryDirectory);
                    var telemetrySummary = new
                    {
                        mode = "summary",
                        testsStarted = 1,
                        testsFinished = 1,
                        testsPassed = 1,
                        testsFailed = 0,
                        testsSkipped = 0,
                        retainedTestSummaries = 1,
                        activities = 2,
                        metrics = 1,
                        swallowedExceptions = 0,
                        orphanedActivities = 0,
                        telemetryErrors = 0,
                        streams = new
                        {
                            tests = new { recordsWritten = 1, recordsDropped = 0, bytesWritten = 64, truncated = false },
                            activities = new { recordsWritten = 0, recordsDropped = 0, bytesWritten = 0, truncated = false },
                            metrics = new { recordsWritten = 0, recordsDropped = 0, bytesWritten = 0, truncated = false },
                            runtimeCounters = new { recordsWritten = 0, recordsDropped = 0, bytesWritten = 0, truncated = false }
                        }
                    };
                    await File.WriteAllTextAsync(Path.Combine(telemetryDirectory, "summary.json"),
                        JsonSerializer.Serialize(telemetrySummary), cancellationToken);
                    await File.WriteAllTextAsync(Path.Combine(telemetryDirectory, "tests.ndjson"),
                        "{\"testName\":\"Telemetry test\",\"result\":\"Passed\"}\n", cancellationToken);
                }
            }

            return Process(request, failing ? OutcomeKind.CommandFailure : OutcomeKind.Success, failing ? 1 : 0);
        }

        private static ProcessResult Process(ProcessRequest request, OutcomeKind outcome, int exitCode,
            IReadOnlyList<string>? standardOutputTail = null)
            => new(outcome, exitCode, standardOutputTail ?? [], [], standardOutputTail?.Count ?? 0, 0, TimeSpan.FromMilliseconds(10), request.StandardOutputLogPath,
                request.StandardErrorLogPath, null);
    }

    private sealed class AotRecordingProcessRunner(string repositoryRoot) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            await File.WriteAllTextAsync(request.StandardOutputLogPath, string.Empty, cancellationToken);
            await File.WriteAllTextAsync(request.StandardErrorLogPath, string.Empty, cancellationToken);
            if (request.ExecutablePath == "dotnet" && request.Arguments.SequenceEqual(["--version"], StringComparer.Ordinal))
            {
                await File.WriteAllTextAsync(request.StandardOutputLogPath, "10.0.103", cancellationToken);
                return new ProcessResult(OutcomeKind.Success, 0, ["10.0.103"], [], 1, 0, TimeSpan.FromMilliseconds(10),
                    request.StandardOutputLogPath, request.StandardErrorLogPath, null);
            }
            if (request.ExecutablePath == "dotnet" && request.Arguments.SequenceEqual(["--list-sdks"], StringComparer.Ordinal))
            {
                var line = $"10.0.103 [{Path.Combine(repositoryRoot, "test-sdks")}]";
                Directory.CreateDirectory(Path.Combine(repositoryRoot, "test-sdks", "10.0.103"));
                await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "test-sdks", "10.0.103", "MSBuild.dll"), "test SDK marker", cancellationToken);
                await File.WriteAllTextAsync(request.StandardOutputLogPath, line, cancellationToken);
                return new ProcessResult(OutcomeKind.Success, 0, [line], [], 1, 0, TimeSpan.FromMilliseconds(10),
                    request.StandardOutputLogPath, request.StandardErrorLogPath, null);
            }
            if (request.ExecutablePath == "dotnet" && request.Arguments[0] == "publish")
            {
                var projectPath = request.Arguments[1];
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                var publishDirectory = Path.Combine(repositoryRoot, "artifacts", "publish", projectName,
                    "release_net10.0_linux-x64");
                Directory.CreateDirectory(publishDirectory);
                await File.WriteAllTextAsync(Path.Combine(publishDirectory, projectName), "native binary", cancellationToken);
            }
            return new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.FromMilliseconds(10),
                request.StandardOutputLogPath, request.StandardErrorLogPath, null);
        }
    }
}
