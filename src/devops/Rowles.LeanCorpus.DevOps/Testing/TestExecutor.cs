using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestExecutorRequest
{
    public required CheckoutIdentity Checkout { get; init; }
    public required IReadOnlyList<TestTarget> Targets { get; init; }
    public required TestRunnerOptions RunnerOptions { get; init; }
    public AffectedTestSelection? AffectedSelection { get; init; }
    public string CommandLine { get; init; } = "devops test";
    public int Count { get; init; } = 1;
    public bool FailFast { get; init; }
    public bool NoRestore { get; init; }
    public TimeSpan? ProcessTimeout { get; init; }
}

internal sealed record TestExecutorResult(
    CommandResult CommandResult,
    string RunDirectory,
    TestRunAggregate? Aggregate,
    RunManifest? Manifest);

internal enum TestEventKind
{
    RunStarted,
    TargetsPrepared,
    TargetDiscovered,
    Checkpoint,
    TargetStarted,
    TargetHeartbeat,
    TargetCompleted,
    RunWarning,
    RunError,
    RunCompleted
}

internal sealed record TestProgressEvent(
    Guid RunId,
    TestEventKind Kind,
    DateTimeOffset TimestampUtc,
    string? TargetKey = null,
    int? Iteration = null,
    TestExecutionOutcome? Outcome = null,
    string? Message = null,
    int CompletedExecutions = 0,
    int RequestedExecutions = 0,
    TimeSpan? ProcessElapsed = null,
    int? StandardOutputLineCount = null,
    int? StandardErrorLineCount = null,
    string? Framework = null,
    IReadOnlyList<string>? Areas = null);

internal sealed class TestExecutor(
    ArtifactPaths artifactPaths,
    ArtifactRunStore artifactRunStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<TestProgressEvent>? progress = null)
{
    private readonly TestArtifactService _artifacts = new();

    public async Task<TestExecutorResult> RunAsync(TestExecutorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Targets.Count == 0)
        {
            return Validation("No test targets were selected.");
        }
        if (request.Count < 1)
        {
            return Validation("Test repetition count must be a positive integer.");
        }
        if (request.RunnerOptions.ArtifactsEnabled && request.Targets.Any(static target =>
                target.RunnerKind == "Mtp" && !target.Capabilities.Contains("Trx", StringComparer.OrdinalIgnoreCase)))
        {
            return Validation("Every MTP target must declare the TRX capability when result artefacts are enabled.");
        }

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["targets"] = string.Join(',', request.Targets.Select(static target => target.Key)),
            ["count"] = request.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failFast"] = request.FailFast ? "true" : "false",
            ["ci"] = request.RunnerOptions.Ci ? "true" : "false",
            ["noRestore"] = request.NoRestore ? "true" : "false",
            ["diagnostics"] = request.RunnerOptions.Diagnostics ? "true" : "false",
            ["coverage"] = request.RunnerOptions.CollectCoverage ? "true" : "false",
            ["flaky"] = request.RunnerOptions.Flaky ? "true" : "false",
            ["failWarnings"] = request.RunnerOptions.FailWarnings ? "true" : "false",
            ["artifacts"] = request.RunnerOptions.ArtifactsEnabled ? "true" : "false",
            ["parallelProfile"] = request.RunnerOptions.ParallelProfile,
            ["explicitMode"] = request.RunnerOptions.ExplicitMode,
            ["hangTimeout"] = request.RunnerOptions.HangTimeout,
            ["processTimeout"] = request.ProcessTimeout?.ToString() ?? "off"
        };
        if (request.AffectedSelection is { } affectedSelection)
        {
            inputs["affectedMode"] = affectedSelection.Mode;
            inputs["affectedCommit"] = affectedSelection.ResolvedCommit;
            inputs["affectedFirstParent"] = affectedSelection.FirstParent;
            inputs["affectedHead"] = affectedSelection.Head;
            inputs["affectedPaths"] = JsonSerializer.Serialize(affectedSelection.Paths);
        }
        var manifest = await artifactRunStore.StartAsync("test", inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("test", manifest.RunId);
        var attempts = new List<TestExecutionAttempt>();
        IReadOnlyList<TestPreparationTiming> preparationTimings = [];
        var requestedExecutionCount = checked(request.Targets.Count * request.Count);
        string? activeTarget = null;
        int? activeIteration = null;
        TestRunAggregate? aggregate = null;
        CommandResult commandResult;
        RunManifest? finishedManifest = null;

        try
        {
            Directory.CreateDirectory(runDirectory);
            await _artifacts.WriteSelectionAsync(runDirectory, request, cancellationToken).ConfigureAwait(false);
            await _artifacts.WriteStateAsync(runDirectory, manifest.RunId, "running", request.Targets, request.Count,
                attempts, activeTarget, activeIteration, cancellationToken).ConfigureAwait(false);
            await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.RunStarted,
                DateTimeOffset.UtcNow, RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
            await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.Checkpoint,
                DateTimeOffset.UtcNow, Message: "Run state checkpointed before preparation.",
                RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);

            var provenance = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(provenance.Commit))
            {
                throw new TestExecutionException(OutcomeKind.InfrastructureFailure,
                    provenance.Failure ?? "Git source provenance could not be read.");
            }
            manifest = await artifactRunStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch,
                provenance.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
            {
                throw new TestExecutionException(sdk.Outcome,
                    sdk.Failure ?? "The SDK selected by global.json could not be resolved.");
            }

            manifest = await artifactRunStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch,
                provenance.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, cancellationToken).ConfigureAwait(false);

            var preparation = await new TestPreparationService(processRunner).PrepareAsync(new TestPreparationRequest
            {
                Checkout = request.Checkout,
                Targets = request.Targets,
                Ci = request.RunnerOptions.Ci,
                NoRestore = request.NoRestore
            }, runDirectory, cancellationToken).ConfigureAwait(false);
            preparationTimings = preparation.Timings;
            if (preparation.Outcome != OutcomeKind.Success)
            {
                commandResult = PreparationCommandResult(preparation);
                aggregate = TestRunAggregator.Aggregate(request.Targets.Select(static target => target.Key).ToArray(), request.Count, attempts);
                await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.RunError,
                    DateTimeOffset.UtcNow, Message: preparation.Failure,
                    RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.TargetsPrepared,
                    DateTimeOffset.UtcNow, Message: string.Join(',', preparation.Targets.Select(static target => target.Target.Key)),
                    RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
                foreach (var preparedTarget in preparation.Targets)
                {
                    await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.TargetDiscovered,
                        DateTimeOffset.UtcNow, preparedTarget.Target.Key,
                        Message: preparedTarget.Target.Name,
                        RequestedExecutions: requestedExecutionCount,
                        Framework: preparedTarget.Target.Framework,
                        Areas: preparedTarget.Target.Areas), cancellationToken).ConfigureAwait(false);
                }
                var stopScheduling = false;
                foreach (var iteration in Enumerable.Range(1, request.Count))
                {
                    foreach (var preparedTarget in preparation.Targets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (stopScheduling)
                        {
                            break;
                        }

                        activeTarget = preparedTarget.Target.Key;
                        activeIteration = iteration;
                        var attemptDirectory = _artifacts.GetAttemptDirectory(runDirectory, preparedTarget.Target, iteration);
                        await _artifacts.WriteStateAsync(runDirectory, manifest.RunId, "running", request.Targets,
                            request.Count, attempts, activeTarget, activeIteration, cancellationToken).ConfigureAwait(false);
                        await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.Checkpoint,
                            DateTimeOffset.UtcNow, activeTarget, activeIteration,
                            Message: "Target start checkpointed.", CompletedExecutions: attempts.Count,
                            RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
                        await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.TargetStarted,
                            DateTimeOffset.UtcNow, activeTarget, iteration, CompletedExecutions: attempts.Count,
                            RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);

                        var attempt = await ExecuteAttemptAsync(manifest.RunId, preparedTarget, iteration, attemptDirectory,
                            request, runDirectory, cancellationToken).ConfigureAwait(false);
                        attempts.Add(attempt);
                        activeTarget = null;
                        activeIteration = null;
                        await _artifacts.WriteStateAsync(runDirectory, manifest.RunId, "running", request.Targets,
                            request.Count, attempts, activeTarget, activeIteration, cancellationToken).ConfigureAwait(false);
                        await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.Checkpoint,
                            DateTimeOffset.UtcNow, attempt.Target.Key, attempt.Iteration,
                            Message: "Target completion checkpointed.", CompletedExecutions: attempts.Count,
                            RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
                        await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.TargetCompleted,
                            DateTimeOffset.UtcNow, attempt.Target.Key, attempt.Iteration, attempt.Outcome,
                            attempt.Failure, attempts.Count, requestedExecutionCount), cancellationToken).ConfigureAwait(false);

                        if (request.FailFast && attempt.Outcome != TestExecutionOutcome.Passed)
                        {
                            stopScheduling = true;
                            await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.RunWarning,
                                DateTimeOffset.UtcNow, Message: "Fail-fast stopped scheduling future target executions.",
                                CompletedExecutions: attempts.Count, RequestedExecutions: requestedExecutionCount), cancellationToken).ConfigureAwait(false);
                            break;
                        }
                    }
                    if (stopScheduling)
                    {
                        break;
                    }
                }

                aggregate = TestRunAggregator.Aggregate(request.Targets.Select(static target => target.Key).ToArray(), request.Count, attempts);
                commandResult = ToCommandResult(aggregate);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RecordInterruptedExecution(TestExecutionOutcome.Cancelled, "Test execution was cancelled before its target completed.");
            commandResult = new CommandResult(OutcomeKind.Cancelled, "Test run was cancelled.");
            aggregate = TestRunAggregator.Aggregate(request.Targets.Select(static target => target.Key).ToArray(), request.Count, attempts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or TestExecutionException)
        {
            RecordInterruptedExecution(TestExecutionOutcome.Incomplete, exception.Message);
            commandResult = exception is TestExecutionException testException
                ? new CommandResult(testException.Outcome, "Managed test execution could not start.", Details: exception.Message)
                : new CommandResult(OutcomeKind.InfrastructureFailure, "Managed test execution failed.", Details: exception.Message);
            aggregate ??= TestRunAggregator.Aggregate(request.Targets.Select(static target => target.Key).ToArray(), request.Count, attempts);
            try
            {
                await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.RunError,
                    DateTimeOffset.UtcNow, Message: exception.Message, CompletedExecutions: attempts.Count,
                    RequestedExecutions: requestedExecutionCount), CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        var terminalStatus = commandResult.Outcome switch
        {
            OutcomeKind.Success => RunState.Succeeded,
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        try
        {
            await _artifacts.WriteReportAsync(runDirectory, manifest.RunId, request, aggregate, commandResult,
                manifest, preparationTimings, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
            await _artifacts.WriteStateAsync(runDirectory, manifest.RunId, terminalStatus.ToString().ToLowerInvariant(),
                request.Targets, request.Count, attempts, null, null, CancellationToken.None).ConfigureAwait(false);
            await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.Checkpoint,
                DateTimeOffset.UtcNow, Message: $"Terminal state checkpointed as {terminalStatus}.",
                CompletedExecutions: attempts.Count, RequestedExecutions: requestedExecutionCount), CancellationToken.None).ConfigureAwait(false);
            await EmitAsync(runDirectory, new TestProgressEvent(manifest.RunId, TestEventKind.RunCompleted,
                DateTimeOffset.UtcNow, Outcome: ToProgressOutcome(commandResult, aggregate),
                Message: commandResult.Message, CompletedExecutions: attempts.Count,
                RequestedExecutions: requestedExecutionCount), CancellationToken.None).ConfigureAwait(false);
            var artifactFiles = Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(runDirectory, path).Replace('\\', '/'))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            finishedManifest = await artifactRunStore.FinishAsync(manifest, terminalStatus,
                childExitCode: null, artifacts: artifactFiles,
                failure: commandResult.Outcome == OutcomeKind.Success ? null : commandResult.Message,
                cancellationToken: CancellationToken.None,
                childExitCodes: attempts.Where(static attempt => attempt.ExitCode.HasValue).Select(static attempt => attempt.ExitCode!.Value))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                "Test run evidence could not be finalised.", Details: exception.Message);
            try
            {
                await artifactRunStore.FinishAsync(manifest, RunState.Failed, failure: exception.Message,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The first finalisation may have reached disk before the failure surfaced.
            }
        }

        var finalMessage = commandResult.Outcome == OutcomeKind.Success
            ? $"Test run passed. Run evidence: {runDirectory}"
            : commandResult.Message;
        commandResult = commandResult with
        {
            Message = finalMessage,
            Details = commandResult.Details is null && commandResult.Outcome != OutcomeKind.Success
                ? runDirectory
                : commandResult.Details
        };
        return new TestExecutorResult(commandResult, runDirectory, aggregate, finishedManifest);

        void RecordInterruptedExecution(TestExecutionOutcome outcome, string failure)
        {
            if (activeTarget is null || activeIteration is null)
            {
                return;
            }

            var target = request.Targets.FirstOrDefault(candidate => candidate.Key == activeTarget);
            if (target is null)
            {
                return;
            }

            var artifactDirectory = _artifacts.GetAttemptDirectory(runDirectory, target, activeIteration.Value);
            attempts.Add(new TestExecutionAttempt(target, activeIteration.Value, outcome, [], false, failure)
            {
                StartedUtc = DateTimeOffset.UtcNow,
                ArtifactDirectory = artifactDirectory,
                StandardOutputPath = Path.Combine(artifactDirectory, "stdout.log"),
                StandardErrorPath = Path.Combine(artifactDirectory, "stderr.log"),
                TrxPath = target.RunnerKind.Equals("Mtp", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(artifactDirectory, "results.trx")
                    : null
            });
            activeTarget = null;
            activeIteration = null;
        }
    }

    private async Task<TestExecutionAttempt> ExecuteAttemptAsync(
        Guid runId,
        PreparedTestTarget preparedTarget,
        int iteration,
        string attemptDirectory,
        TestExecutorRequest request,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(attemptDirectory);
        var diagnosticsDirectory = Path.Combine(attemptDirectory, "diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);
        var target = preparedTarget.Target;
        var stdoutPath = Path.Combine(attemptDirectory, "stdout.log");
        var stderrPath = Path.Combine(attemptDirectory, "stderr.log");
        ITestTargetRunner runner = target.RunnerKind switch
        {
            "Mtp" => new MtpExecutableTestRunner(processRunner),
            "AotNative" => new NativeAotTestRunner(processRunner),
            _ => throw new InvalidOperationException($"Unsupported test runner '{target.RunnerKind}'.")
        };
        var execution = await runner.RunAsync(new TestRunnerContext(runId, preparedTarget, request.RunnerOptions,
            request.Checkout.RepositoryRoot, attemptDirectory, iteration, request.ProcessTimeout,
            processProgress => progress?.Invoke(new TestProgressEvent(runId,
                TestEventKind.TargetHeartbeat, DateTimeOffset.UtcNow, target.Key, iteration,
                Message: "Test process is still running.", ProcessElapsed: processProgress.Elapsed,
                StandardOutputLineCount: processProgress.StandardOutputLineCount,
                StandardErrorLineCount: processProgress.StandardErrorLineCount))), cancellationToken).ConfigureAwait(false);
        var process = execution.Process;
        var resultData = execution.Results;
        var diagnosticPaths = GetDiagnosticPaths(attemptDirectory);
        var outcome = TestOutcomeClassifier.Classify(target.RunnerKind, process, resultData,
            resultsRequired: target.RunnerKind.Equals("Mtp", StringComparison.OrdinalIgnoreCase) && request.RunnerOptions.ArtifactsEnabled,
            diagnosticPaths: diagnosticPaths);
        var failure = resultData is { Valid: false } ? resultData.Error : process.Failure;
        return new TestExecutionAttempt(target, iteration, outcome, resultData?.Tests ?? [], true, failure)
        {
            ExitCode = process.RawExitCode,
            StartedUtc = execution.StartedUtc,
            EndedUtc = execution.EndedUtc,
            Duration = process.Duration,
            ArtifactDirectory = attemptDirectory,
            StandardOutputPath = stdoutPath,
            StandardErrorPath = stderrPath,
            TrxPath = execution.TrxPath,
            DiagnosticPaths = diagnosticPaths
        };
    }

    private async Task EmitAsync(string runDirectory, TestProgressEvent progressEvent, CancellationToken cancellationToken)
    {
        await _artifacts.AppendEventAsync(runDirectory, progressEvent, cancellationToken).ConfigureAwait(false);
        progress?.Invoke(progressEvent);
    }

    private static IReadOnlyList<string> GetDiagnosticPaths(string attemptDirectory)
    {
        var extensions = new HashSet<string>([".dmp", ".diag", ".nettrace", ".gcdump"], StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(attemptDirectory, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)) ||
                Path.GetRelativePath(attemptDirectory, path).Replace('\\', '/').StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetRelativePath(attemptDirectory, path).Replace('\\', '/').StartsWith("telemetry/", StringComparison.OrdinalIgnoreCase) ||
                Path.GetRelativePath(attemptDirectory, path).Replace('\\', '/').StartsWith("runtime/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static CommandResult ToCommandResult(TestRunAggregate aggregate)
    {
        if (aggregate.Succeeded)
        {
            return new CommandResult(OutcomeKind.Success, "Test run passed.");
        }
        var outcomes = aggregate.Attempts.Select(static attempt => attempt.Outcome).ToHashSet();
        if (outcomes.Contains(TestExecutionOutcome.Cancelled))
        {
            return new CommandResult(OutcomeKind.Cancelled, "Test run was cancelled.");
        }
        if (outcomes.Contains(TestExecutionOutcome.Hung))
        {
            return new CommandResult(OutcomeKind.Timeout, "A test target timed out.");
        }
        if (outcomes.Overlaps([TestExecutionOutcome.InfrastructureFailure, TestExecutionOutcome.Incomplete, TestExecutionOutcome.DiscoveryFailure]))
        {
            return new CommandResult(OutcomeKind.InfrastructureFailure, "Test run did not produce complete passing evidence.");
        }
        if (outcomes.Overlaps([TestExecutionOutcome.AssertionFailure, TestExecutionOutcome.Crashed]))
        {
            return new CommandResult(OutcomeKind.CommandFailure, "One or more test targets failed.",
                aggregate.Attempts.LastOrDefault(static attempt => attempt.ExitCode != 0)?.ExitCode);
        }
        return new CommandResult(OutcomeKind.InfrastructureFailure, "Test run did not execute every requested target iteration.");
    }

    private static CommandResult PreparationCommandResult(TestPreparationResult result)
        => result.Outcome switch
        {
            OutcomeKind.Cancelled => new CommandResult(OutcomeKind.Cancelled, result.Failure ?? "Test preparation was cancelled."),
            OutcomeKind.Timeout => new CommandResult(OutcomeKind.Timeout, result.Failure ?? "Test preparation timed out."),
            OutcomeKind.ValidationFailure => new CommandResult(OutcomeKind.ValidationFailure, result.Failure ?? "Test target validation failed."),
            OutcomeKind.CommandFailure => new CommandResult(OutcomeKind.CommandFailure, result.Failure ?? "Test target preparation failed."),
            _ => new CommandResult(OutcomeKind.InfrastructureFailure, result.Failure ?? "Test target preparation failed.")
        };

    private static TestExecutionOutcome ToProgressOutcome(CommandResult result, TestRunAggregate aggregate)
        => result.Outcome switch
        {
            OutcomeKind.Success => TestExecutionOutcome.Passed,
            OutcomeKind.CommandFailure => aggregate.Attempts.Any(static attempt => attempt.Outcome == TestExecutionOutcome.Crashed)
                ? TestExecutionOutcome.Crashed
                : TestExecutionOutcome.AssertionFailure,
            OutcomeKind.Timeout => TestExecutionOutcome.Hung,
            OutcomeKind.Cancelled => TestExecutionOutcome.Cancelled,
            _ => TestExecutionOutcome.Incomplete
        };

    private static TestExecutorResult Validation(string message)
        => new(new CommandResult(OutcomeKind.ValidationFailure, message), string.Empty, null, null);

    private sealed class TestExecutionException(OutcomeKind outcome, string message) : Exception(message)
    {
        public OutcomeKind Outcome { get; } = outcome;
    }
}

internal sealed class TestArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly AtomicFileWriter _writer = new();

    public string GetAttemptDirectory(string runDirectory, TestTarget target, int iteration)
    {
        var display = new string(target.Key.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
            ? character
            : '-').Take(48).ToArray()).Trim('-', '.');
        if (display.Length == 0)
        {
            display = "target";
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.Key))).ToLowerInvariant()[..8];
        var path = Path.Combine(runDirectory, "targets", $"{display}-{fingerprint}", iteration.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    public Task WriteStateAsync(
        string runDirectory,
        Guid runId,
        string status,
        IReadOnlyList<TestTarget> targets,
        int count,
        IReadOnlyList<TestExecutionAttempt> attempts,
        string? activeTarget,
        int? activeIteration,
        CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(runDirectory, "state.json"), new
        {
            schemaVersion = 1,
            runId,
            status,
            updatedAtUtc = DateTimeOffset.UtcNow,
            requestedExecutions = targets.Count * count,
            completedExecutions = attempts.Count,
            activeTarget,
            activeIteration,
            attempts = attempts.Select(ToAttemptDocument).ToArray()
        }, cancellationToken);

    public async Task WriteReportAsync(
        string runDirectory,
        Guid runId,
        TestExecutorRequest request,
        TestRunAggregate? aggregate,
        CommandResult commandResult,
        RunManifest manifest,
        IReadOnlyList<TestPreparationTiming> preparationTimings,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(Path.Combine(runDirectory, "report.json"), new
        {
            schemaVersion = 1,
            runId,
            command = "test",
            outcome = commandResult.Outcome.ToString(),
            succeeded = aggregate?.Succeeded == true,
            message = commandResult.Message,
            requestedExecutions = request.Targets.Count * request.Count,
            completedExecutions = aggregate?.Attempts.Count ?? 0,
            unscheduledExecutions = aggregate?.UnscheduledAttemptCount ?? request.Targets.Count * request.Count,
            error = commandResult.Details,
            selection = GetSelectionDocument(request),
            selectedTargets = request.Targets,
            unscheduledTargets = GetUnscheduledTargets(request, aggregate),
            cancelledTargets = aggregate?.Attempts
                .Where(static attempt => attempt.Outcome == TestExecutionOutcome.Cancelled)
                .Select(static attempt => new { target = attempt.Target.Key, iteration = attempt.Iteration })
                .ToArray() ?? [],
            attempts = aggregate?.Attempts.Select(ToAttemptDocument).ToArray() ?? [],
            tests = aggregate?.Tests ?? []
        }, cancellationToken).ConfigureAwait(false);
        await new TestReportWriter(_writer).WriteSupplementaryReportsAsync(runDirectory, manifest, request, aggregate,
            commandResult, preparationTimings, completedUtc, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteSelectionAsync(string runDirectory, TestExecutorRequest request, CancellationToken cancellationToken)
        => WriteJsonAsync(Path.Combine(runDirectory, "selection.json"), GetSelectionDocument(request), cancellationToken);

    private static object GetSelectionDocument(TestExecutorRequest request)
    {
        if (request.AffectedSelection is not { } affected)
        {
            return new
            {
                mode = "explicit",
                suppliedCommit = string.Empty,
                suppliedRange = string.Empty,
                resolvedCommit = string.Empty,
                firstParent = string.Empty,
                head = string.Empty,
                changedPaths = Array.Empty<string>(),
                affectedTargets = Array.Empty<string>(),
                areasBySuite = new Dictionary<string, IReadOnlyList<string>>(),
                selectedTargets = request.Targets
            };
        }

        return new
        {
            mode = affected.Mode,
            suppliedCommit = affected.SuppliedCommit,
            suppliedRange = affected.SuppliedRange,
            resolvedCommit = affected.ResolvedCommit,
            firstParent = affected.FirstParent,
            head = affected.Head,
            changedPaths = affected.Paths,
            affectedTargets = affected.Targets,
            areasBySuite = affected.AreasBySuite,
            selectedTargets = request.Targets
        };
    }

    private static object[] GetUnscheduledTargets(TestExecutorRequest request, TestRunAggregate? aggregate)
    {
        var scheduled = aggregate?.Attempts
            .Select(static attempt => (attempt.Target.Key, attempt.Iteration))
            .ToHashSet() ?? new HashSet<(string Key, int Iteration)>();
        return request.Targets
            .SelectMany(target => Enumerable.Range(1, request.Count)
                .Where(iteration => !scheduled.Contains((target.Key, iteration)))
                .Select(iteration => new { target = target.Key, iteration }))
            .ToArray();
    }

    public async Task AppendEventAsync(string runDirectory, TestProgressEvent progressEvent, CancellationToken cancellationToken)
    {
        var path = Path.Combine(runDirectory, "events.ndjson");
        Directory.CreateDirectory(runDirectory);
        var line = JsonSerializer.Serialize(progressEvent, JsonOptions) + Environment.NewLine;
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(line);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
        => _writer.WriteReplaceAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), cancellationToken);

    private static object ToAttemptDocument(TestExecutionAttempt attempt)
        => new
        {
            target = attempt.Target.Key,
            suite = attempt.Target.Suite,
            framework = attempt.Target.Framework,
            runner = attempt.Target.RunnerKind,
            iteration = attempt.Iteration,
            outcome = attempt.Outcome.ToString(),
            completed = attempt.Completed,
            exitCode = attempt.ExitCode,
            startedUtc = attempt.StartedUtc,
            endedUtc = attempt.EndedUtc,
            durationMs = Math.Round(attempt.Duration.TotalMilliseconds, 3),
            artifactDirectory = attempt.ArtifactDirectory,
            standardOutputPath = attempt.StandardOutputPath,
            standardErrorPath = attempt.StandardErrorPath,
            trxPath = attempt.TrxPath,
            diagnosticPaths = attempt.DiagnosticPaths,
            failure = attempt.Failure,
            tests = attempt.Tests
        };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
