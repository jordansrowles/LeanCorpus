using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Diagnostics;

internal sealed record DiagnosticsCommandRequest(CheckoutIdentity Checkout, DiagnosticsRequest Request);

internal sealed record DiagnosticsCommandResult(
    CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    RunManifest? Manifest);

internal sealed class DiagnosticsCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    Action<string>? errorOutput = null,
    string dotnetExecutable = "dotnet")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DiagnosticsCommandResult> RunAsync(DiagnosticsCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Checkout);
        ArgumentNullException.ThrowIfNull(request.Request);

        var validationFailure = Validate(request);
        if (validationFailure is not null)
            return Failure(OutcomeKind.ValidationFailure, validationFailure);

        var plannedTools = GetToolNames(request.Request.Operation);
        var toolVersions = ReadToolVersions(request.Checkout.RepositoryRoot, plannedTools, out var toolFailure);
        if (toolVersions is null)
            return Failure(OutcomeKind.ValidationFailure, toolFailure ?? "The pinned diagnostic tools are unavailable.");

        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync("diagnostics", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operation"] = request.Request.Operation.ToString(),
                ["processId"] = request.Request.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                ["duration"] = request.Request.Duration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                ["dumpType"] = request.Request.DumpType,
                ["artifact"] = request.Request.ArtifactPath ?? string.Empty,
                ["passThrough"] = JsonSerializer.Serialize(request.Request.PassThrough, JsonOptions),
                ["diagnosticTools"] = string.Join(',', plannedTools)
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Diagnostics were cancelled before the run record was created.", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not create diagnostics run evidence.", exception.Message);
        }

        var runDirectory = artifactPaths.GetRunDirectory("diagnostics", manifest.RunId);
        var invocations = BuildInvocations(request.Request, runDirectory);
        var artifacts = new HashSet<string>(StringComparer.Ordinal);
        var childExitCodes = new List<int>();
        var failures = new List<(OutcomeKind Outcome, string Message)>();
        var cancelled = false;

        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record diagnostics source provenance.", git.Failure).ConfigureAwait(false);

            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
                return await FinishEarlyAsync(manifest, runDirectory, sdk.Outcome,
                    "Could not resolve the SDK selected by global.json.", sdk.Failure).ConfigureAwait(false);

            var effectiveArguments = invocations.Select(invocation => new
            {
                tool = invocation.ToolName,
                arguments = new[] { "tool", "run", invocation.ToolName }.Concat(invocation.Arguments).ToArray()
            }).ToArray();
            var recordedToolVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
            };
            foreach (var (toolName, version) in toolVersions)
                recordedToolVersions[$"diagnostic-{toolName}"] = version;
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                recordedToolVersions,
                cancellationToken,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effectiveArguments"] = JsonSerializer.Serialize(effectiveArguments, JsonOptions)
                }).ConfigureAwait(false);

            foreach (var invocation in invocations)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var stdoutName = $"{invocation.ToolName}.stdout.log";
                var stderrName = $"{invocation.ToolName}.stderr.log";
                var stdoutPath = Path.Combine(runDirectory, stdoutName);
                var stderrPath = Path.Combine(runDirectory, stderrName);
                artifacts.Add(stdoutName);
                artifacts.Add(stderrName);
                if (invocation.OutputName is not null)
                    artifacts.Add(invocation.OutputName);

                var child = await processRunner.RunAsync(new ProcessRequest
                {
                    ExecutablePath = dotnetExecutable,
                    Arguments = ["tool", "run", invocation.ToolName, .. invocation.Arguments],
                    WorkingDirectory = request.Checkout.RepositoryRoot,
                    StandardOutputLogPath = stdoutPath,
                    StandardErrorLogPath = stderrPath,
                    Timeout = invocation.Timeout,
                    ProgressInterval = TimeSpan.FromSeconds(30),
                    Progress = progressEvent => progress?.Invoke(
                        $"{invocation.ToolName} is still running ({progressEvent.Elapsed:hh\\:mm\\:ss}; {progressEvent.StandardOutputLineCount} stdout and {progressEvent.StandardErrorLineCount} stderr lines).")
                }, cancellationToken).ConfigureAwait(false);

                foreach (var line in child.StandardOutputTail)
                    progress?.Invoke(line);
                foreach (var line in child.StandardErrorTail)
                    errorOutput?.Invoke(line);
                if (child.RawExitCode is { } childExitCode)
                    childExitCodes.Add(childExitCode);

                if (child.Outcome == OutcomeKind.Success && invocation.OutputName is not null &&
                    (!File.Exists(Path.Combine(runDirectory, invocation.OutputName)) || new FileInfo(Path.Combine(runDirectory, invocation.OutputName)).Length == 0))
                {
                    failures.Add((OutcomeKind.InfrastructureFailure,
                        $"{invocation.ToolName} exited successfully but did not produce '{invocation.OutputName}'."));
                }
                else if (child.Outcome != OutcomeKind.Success)
                {
                    if (child.Outcome == OutcomeKind.Cancelled && cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    failures.Add((child.Outcome,
                        child.Failure ?? $"{invocation.ToolName} did not complete successfully (exit code {child.RawExitCode})."));
                }

                if (failures.Count > 0 && request.Request.Operation != DiagnosticsOperation.Capture)
                    break;
            }

            foreach (var file in Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(runDirectory, file).Replace('\\', '/');
                if (relative is not ("run.json" or "manifest.json"))
                    artifacts.Add(relative);
            }

            var terminalState = cancelled
                ? RunState.Cancelled
                : failures.Any(failure => failure.Outcome == OutcomeKind.Timeout)
                    ? RunState.TimedOut
                    : failures.Count == 0 ? RunState.Succeeded : RunState.Failed;
            var failureText = failures.Count == 0 ? null : string.Join("; ", failures.Select(failure => failure.Message));
            var finished = await runStore.FinishAsync(manifest, terminalState,
                artifacts: artifacts,
                failure: failureText,
                cancellationToken: CancellationToken.None,
                childExitCodes: childExitCodes).ConfigureAwait(false);

            var outcome = cancelled
                ? OutcomeKind.Cancelled
                : failures.Count == 0 ? OutcomeKind.Success : CollapseFailures(failures);
            var message = outcome switch
            {
                OutcomeKind.Success => "Diagnostics completed.",
                OutcomeKind.Cancelled => "Diagnostics were cancelled.",
                OutcomeKind.Timeout => "Diagnostics timed out.",
                OutcomeKind.CommandFailure => "A diagnostic tool failed.",
                OutcomeKind.ValidationFailure => "A diagnostic tool rejected the request.",
                _ => "Diagnostics infrastructure failed."
            };
            return new DiagnosticsCommandResult(new CommandResult(outcome, message,
                GetCommandChildExitCode(outcome, childExitCodes),
                JoinDetails(runDirectory, failureText)), finished.RunId, runDirectory, finished);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Diagnostics were cancelled.", exception.Message,
                artifacts, childExitCodes).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                "Diagnostics infrastructure failed.", exception.Message, artifacts, childExitCodes).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<DiagnosticToolInvocation> BuildInvocations(DiagnosticsRequest request, string runDirectory)
    {
        var pid = request.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var tail = request.PassThrough;
        var tracePath = Path.Combine(runDirectory, "trace.nettrace");
        var counterPath = Path.Combine(runDirectory, "counters.json");
        var heapPath = Path.Combine(runDirectory, "heap.gcdump");
        var dumpPath = Path.Combine(runDirectory, "process.dmp");
        return request.Operation switch
        {
            DiagnosticsOperation.Counters =>
            [new("dotnet-counters", ["monitor", "--process-id", pid, "--refresh-interval", "1", .. tail], null, null)],
            DiagnosticsOperation.Trace =>
            [new("dotnet-trace", ["collect", "--process-id", pid, "--output", tracePath, .. tail], "trace.nettrace", null)],
            DiagnosticsOperation.GcDump =>
            [new("dotnet-gcdump", ["collect", "--process-id", pid, "--output", heapPath, .. tail], "heap.gcdump", null)],
            DiagnosticsOperation.Dump =>
            [new("dotnet-dump", ["collect", "--process-id", pid, "--output", dumpPath, "--type", request.DumpType, .. tail], "process.dmp", null)],
            DiagnosticsOperation.Symbols =>
            [new("dotnet-symbol", [request.ArtifactPath ?? string.Empty, "--output", runDirectory, .. tail], null, null)],
            DiagnosticsOperation.Capture => BuildCapture(request, pid, counterPath, tracePath, tail),
            _ => []
        };
    }

    private static IReadOnlyList<DiagnosticToolInvocation> BuildCapture(
        DiagnosticsRequest request,
        string pid,
        string countersPath,
        string tracePath,
        IReadOnlyList<string> passThrough)
    {
        var duration = request.Duration ?? TimeSpan.FromSeconds(5);
        var durationText = duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
        var timeout = duration.Add(TimeSpan.FromSeconds(30));
        return
        [
            new("dotnet-counters", ["collect", "--process-id", pid, "--duration", durationText, "--format", "json", "--output", countersPath, .. passThrough], "counters.json", timeout),
            new("dotnet-trace", ["collect", "--process-id", pid, "--duration", durationText, "--output", tracePath, .. passThrough], "trace.nettrace", timeout)
        ];
    }

    private static string[] GetToolNames(DiagnosticsOperation operation)
        => operation switch
        {
            DiagnosticsOperation.Counters => ["dotnet-counters"],
            DiagnosticsOperation.Trace => ["dotnet-trace"],
            DiagnosticsOperation.GcDump => ["dotnet-gcdump"],
            DiagnosticsOperation.Dump => ["dotnet-dump"],
            DiagnosticsOperation.Symbols => ["dotnet-symbol"],
            DiagnosticsOperation.Capture => ["dotnet-counters", "dotnet-trace"],
            _ => []
        };

    private static IReadOnlyDictionary<string, string>? ReadToolVersions(string repositoryRoot, IReadOnlyList<string> toolNames, out string? failure)
    {
        failure = null;
        var manifestPath = Path.Combine(repositoryRoot, ".config", "dotnet-tools.json");
        if (!File.Exists(manifestPath))
        {
            failure = $"The repository tool manifest is missing at '{manifestPath}'. Run 'pwsh ./devops.ps1 bootstrap' to restore repository tools.";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var tools = document.RootElement.GetProperty("tools");
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var toolName in toolNames)
            {
                JsonProperty? match = null;
                foreach (var property in tools.EnumerateObject())
                {
                    if (property.Value.GetProperty("commands").EnumerateArray().Any(command =>
                            string.Equals(command.GetString(), toolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        match = property;
                        break;
                    }
                }
                if (match is null)
                {
                    failure = $"The pinned diagnostic tool '{toolName}' is missing from '{manifestPath}'.";
                    return null;
                }
                versions.Add(toolName, match.Value.Value.GetProperty("version").GetString() ?? string.Empty);
            }
            return versions;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            failure = $"The repository diagnostic tool manifest could not be read: {exception.Message}";
            return null;
        }
    }

    private string? Validate(DiagnosticsCommandRequest request)
    {
        if (request.Request.Operation is DiagnosticsOperation.Help or DiagnosticsOperation.ProcessList)
            return "Help and process listing do not use the diagnostics run service.";
        if (!Directory.Exists(request.Checkout.RepositoryRoot) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactPaths.RepositoryRoot)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout.RepositoryRoot)), PathComparison))
            return "A valid LeanCorpus checkout is required.";
        if (request.Request.ProcessId is { } pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return $"Process {pid} could not be found or has exited.";
            }
            catch (ArgumentException exception)
            {
                return $"Process {pid} could not be found: {exception.Message}";
            }
            catch (InvalidOperationException exception)
            {
                return $"Process {pid} could not be found: {exception.Message}";
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                return $"Process {pid} could not be inspected: {exception.Message}";
            }
        }
        if (request.Request.Operation == DiagnosticsOperation.Symbols &&
            (request.Request.ArtifactPath is null || !File.Exists(request.Request.ArtifactPath)))
            return $"Diagnostic artifact was not found: {request.Request.ArtifactPath ?? string.Empty}";
        return null;
    }

    private static OutcomeKind CollapseFailures(IReadOnlyList<(OutcomeKind Outcome, string Message)> failures)
    {
        if (failures.Any(failure => failure.Outcome == OutcomeKind.InfrastructureFailure))
            return OutcomeKind.InfrastructureFailure;
        if (failures.Any(failure => failure.Outcome == OutcomeKind.Timeout))
            return OutcomeKind.Timeout;
        if (failures.Any(failure => failure.Outcome == OutcomeKind.ValidationFailure))
            return OutcomeKind.ValidationFailure;
        return OutcomeKind.CommandFailure;
    }

    private static int? GetCommandChildExitCode(OutcomeKind outcome, IReadOnlyList<int> childExitCodes)
    {
        if (outcome == OutcomeKind.Success)
            return childExitCodes.Count == 0 ? null : childExitCodes[^1];

        foreach (var exitCode in childExitCodes)
        {
            if (exitCode != 0)
                return exitCode;
        }
        return null;
    }

    private async Task<DiagnosticsCommandResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details,
        IEnumerable<string>? artifacts = null,
        IEnumerable<int>? childExitCodes = null)
    {
        var artifactSet = new HashSet<string>(artifacts ?? [], StringComparer.Ordinal);
        if (Directory.Exists(runDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(runDirectory, file).Replace('\\', '/');
                if (relative is not ("run.json" or "manifest.json"))
                    artifactSet.Add(relative);
            }
        }
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        var finished = await runStore.FinishAsync(manifest, state, artifacts: artifactSet, failure: details,
            cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
        return new DiagnosticsCommandResult(new CommandResult(outcome, message, Details: JoinDetails(runDirectory, details)), finished.RunId, runDirectory, finished);
    }

    private static DiagnosticsCommandResult Failure(OutcomeKind outcome, string message, string? details = null)
        => new(new CommandResult(outcome, message, Details: details), null, null, null);

    private static string JoinDetails(string? first, string? second)
        => string.Join(Environment.NewLine, new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal sealed record DiagnosticToolInvocation(string ToolName, IReadOnlyList<string> Arguments, string? OutputName, TimeSpan? Timeout);
