using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record DataForgeCommandRequest(CheckoutIdentity Checkout, IReadOnlyList<string> Arguments);

internal sealed record DataForgeCommandResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal sealed class DataForgeCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? outputProgress = null,
    Action<string>? errorOutput = null,
    string dotnetExecutable = "dotnet")
{
    public async Task<DataForgeCommandResult> RunAsync(DataForgeCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        var projectPath = Path.Combine(artifactPaths.RepositoryRoot, "src", "devops", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj");
        if (!File.Exists(projectPath))
        {
            return new DataForgeCommandResult(new CommandResult(OutcomeKind.ValidationFailure,
                $"The DataForge tool project was not found at '{projectPath}'."), null, null);
        }

        var manifest = await runStore.StartAsync("dataforge",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["arguments"] = JsonSerializer.Serialize(request.Arguments),
                ["project"] = Path.GetRelativePath(artifactPaths.RepositoryRoot, projectPath).Replace('\\', '/'),
                ["framework"] = "net10.0",
                ["configuration"] = "Release"
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("dataforge", manifest.RunId);

        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record DataForge source provenance.", git.Failure).ConfigureAwait(false);
            }
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, sdk.Outcome,
                    "Could not resolve the SDK selected by global.json.", sdk.Failure).ConfigureAwait(false);
            }
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, cancellationToken).ConfigureAwait(false);

            var arguments = BuildArguments(artifactPaths.RepositoryRoot, request.Arguments);
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effectiveArguments"] = JsonSerializer.Serialize(arguments)
                }).ConfigureAwait(false);

            var child = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = dotnetExecutable,
                Arguments = arguments,
                WorkingDirectory = artifactPaths.RepositoryRoot,
                StandardOutputLogPath = Path.Combine(runDirectory, "process.stdout.log"),
                StandardErrorLogPath = Path.Combine(runDirectory, "process.stderr.log"),
                ProgressInterval = TimeSpan.FromSeconds(30),
                Progress = progress => outputProgress?.Invoke(
                    $"DataForge is still running ({progress.Elapsed:hh\\:mm\\:ss}; {progress.StandardOutputLineCount} stdout and {progress.StandardErrorLineCount} stderr lines).")
            }, cancellationToken).ConfigureAwait(false);

            foreach (var line in child.StandardOutputTail)
                outputProgress?.Invoke(line);
            foreach (var line in child.StandardErrorTail)
                errorOutput?.Invoke(line);

            var terminalState = child.Outcome switch
            {
                OutcomeKind.Success => RunState.Succeeded,
                OutcomeKind.Cancelled => RunState.Cancelled,
                OutcomeKind.Timeout => RunState.TimedOut,
                _ => RunState.Failed
            };
            var artifacts = new[]
            {
                "process.stdout.log", "process.stderr.log",
                "sdk-version.stdout.log", "sdk-version.stderr.log", "sdk-list.stdout.log", "sdk-list.stderr.log",
                "git-commit.stdout.log", "git-commit.stderr.log", "git-branch.stdout.log", "git-branch.stderr.log",
                "git-status.stdout.log", "git-status.stderr.log"
            };
            var finished = await runStore.FinishAsync(manifest, terminalState, child.RawExitCode, artifacts,
                child.Failure, CancellationToken.None).ConfigureAwait(false);
            var outcome = child.Outcome == OutcomeKind.CommandFailure && child.RawExitCode == 2
                ? OutcomeKind.ValidationFailure
                : child.Outcome;
            var message = outcome switch
            {
                OutcomeKind.Success => "DataForge command completed.",
                OutcomeKind.ValidationFailure => "DataForge rejected the command arguments.",
                OutcomeKind.CommandFailure => $"DataForge exited with code {child.RawExitCode}.",
                OutcomeKind.Cancelled => "DataForge was cancelled.",
                OutcomeKind.Timeout => "DataForge timed out.",
                _ => "DataForge infrastructure failed."
            };
            return new DataForgeCommandResult(new CommandResult(outcome, message, child.RawExitCode,
                JoinDetails(runDirectory, child.Failure)), finished.RunId, runDirectory);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "DataForge was cancelled.", exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                "DataForge infrastructure failed.", exception.Message).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string repositoryRoot, IReadOnlyList<string> toolArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(toolArguments);
        var projectPath = Path.Combine(Path.GetFullPath(repositoryRoot), "src", "devops", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj");
        return ["run", "--project", projectPath, "--framework", "net10.0", "--configuration", "Release", "--", .. toolArguments];
    }

    private async Task<DataForgeCommandResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details)
    {
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        try
        {
            var finished = await runStore.FinishAsync(manifest, state, failure: details,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new DataForgeCommandResult(new CommandResult(outcome, message, Details: JoinDetails(runDirectory, details)),
                finished.RunId, runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new DataForgeCommandResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "DataForge failed and its run evidence could not be finalised.", Details: JoinDetails(details, exception.Message)),
                manifest.RunId, runDirectory);
        }
    }

    private static string? JoinDetails(params string?[] values)
    {
        var present = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }
}
