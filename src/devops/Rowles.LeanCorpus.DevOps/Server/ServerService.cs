using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Server;

internal sealed class ServerService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    string dotnetExecutable = "dotnet")
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string LocalServerProject => RepositoryProjectCatalog.GetRelativePath(RepositoryProjectRole.ServerLocal);

    public async Task<ServerStartResult> StartAsync(ServerStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return Failure(OutcomeKind.ValidationFailure, validation);
        }

        ExecutionLease lease;
        try
        {
            lease = await ExecutionLease.AcquireSharedAsync(artifactPaths.RepositoryRoot, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Server start was cancelled while waiting for the DevOps execution lease.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not acquire the shared server lease.", exception.Message);
        }

        await using var executionLease = lease;
        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync("server", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project"] = LocalServerProject,
                ["framework"] = request.Framework,
                ["configuration"] = request.Configuration,
                ["noBuild"] = request.NoBuild.ToString().ToLowerInvariant(),
                ["noRestore"] = request.NoRestore.ToString().ToLowerInvariant(),
                ["external"] = request.External.ToString().ToLowerInvariant(),
                ["applicationArguments"] = System.Text.Json.JsonSerializer.Serialize(request.ApplicationArguments)
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Server start was cancelled before its run record was created.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not create server run evidence.", exception.Message);
        }

        var runDirectory = artifactPaths.GetBuildRunDirectory(manifest.RunId);
        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record source provenance for server start.", git.Failure).ConfigureAwait(false);
            }

            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);
            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, sdk.Outcome, "Could not resolve the SDK selected by global.json.", sdk.Failure).ConfigureAwait(false);
            }

            manifest = await runStore.UpdateRunningAsync(
                manifest,
                git.Commit,
                git.Branch,
                git.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                },
                cancellationToken).ConfigureAwait(false);

            var arguments = BuildArguments(request, ResolveProjectPath(request.Checkout));
            manifest = await runStore.UpdateRunningAsync(
                manifest,
                git.Commit,
                git.Branch,
                git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal) { ["effectiveArguments"] = System.Text.Json.JsonSerializer.Serialize(arguments) }).ConfigureAwait(false);
            var child = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = dotnetExecutable,
                Arguments = arguments,
                WorkingDirectory = request.Checkout.RepositoryRoot,
                StandardOutputLogPath = Path.Combine(runDirectory, "process.stdout.log"),
                StandardErrorLogPath = Path.Combine(runDirectory, "process.stderr.log"),
                ProgressInterval = TimeSpan.FromSeconds(5)
            }, cancellationToken).ConfigureAwait(false);

            var terminalState = child.Outcome switch
            {
                OutcomeKind.Success => RunState.Succeeded,
                OutcomeKind.Cancelled => RunState.Cancelled,
                OutcomeKind.Timeout => RunState.TimedOut,
                _ => RunState.Failed
            };
            var artifacts = new List<string>
            {
                "process.stdout.log", "process.stderr.log", "sdk-version.stdout.log", "sdk-version.stderr.log", "sdk-list.stdout.log", "sdk-list.stderr.log",
                "git-commit.stdout.log", "git-commit.stderr.log", "git-branch.stdout.log", "git-branch.stderr.log", "git-status.stdout.log", "git-status.stderr.log"
            };
            manifest = await runStore.FinishAsync(manifest, terminalState, child.RawExitCode, artifacts, child.Failure, CancellationToken.None).ConfigureAwait(false);
            var message = child.Outcome switch
            {
                OutcomeKind.Success => "Community Server stopped with exit code 0.",
                OutcomeKind.CommandFailure => $"Community Server exited with code {child.RawExitCode}.",
                OutcomeKind.Cancelled => "Community Server was stopped.",
                OutcomeKind.Timeout => "Community Server timed out.",
                _ => "Community Server process infrastructure failed."
            };
            return new ServerStartResult(new CommandResult(child.Outcome, message, child.RawExitCode, child.Failure), manifest.RunId, runDirectory, arguments, manifest);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Community Server was stopped.", exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, "Community Server infrastructure failed.", exception.Message).ConfigureAwait(false);
        }
    }

    private string? Validate(ServerStartRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }

        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactPaths.RepositoryRoot)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout.RepositoryRoot)), PathComparison))
        {
            return "The server service artefact root does not match the selected LeanCorpus checkout.";
        }

        if (request.Framework is not ("net10.0" or "net11.0"))
        {
            return $"Unknown framework '{request.Framework}'. Valid: net10.0, net11.0.";
        }

        if (request.Configuration is not ("Debug" or "Release"))
        {
            return $"Unknown configuration '{request.Configuration}'. Valid: Debug, Release.";
        }

        var projectPath = ResolveProjectPath(request.Checkout);
        if (!projectPath.StartsWith(Path.GetFullPath(request.Checkout.RepositoryRoot) + Path.DirectorySeparatorChar, PathComparison) || !File.Exists(projectPath))
        {
            return $"Community Server project '{LocalServerProject}' is missing from the selected checkout.";
        }

        if (request.ApplicationArguments.Any(argument => argument is null || argument.Any(char.IsControl)))
        {
            return "Server application arguments cannot contain nulls or control characters.";
        }

        return null;
    }

    private static string ResolveProjectPath(CheckoutIdentity checkout)
        => RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.ServerLocal);

    private static IReadOnlyList<string> BuildArguments(ServerStartRequest request, string projectPath)
    {
        var arguments = new List<string> { "run", "--project", projectPath, "--framework", request.Framework, "--configuration", request.Configuration };
        if (request.NoBuild)
        {
            arguments.Add("--no-build");
        }

        if (request.NoRestore)
        {
            arguments.Add("--no-restore");
        }

        var applicationArguments = new List<string>();
        if (request.External)
        {
            applicationArguments.AddRange(["--urls=http://0.0.0.0:5080", "--AllowedHosts=*"]);
        }

        applicationArguments.AddRange(request.ApplicationArguments);
        if (applicationArguments.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(applicationArguments);
        }

        return arguments;
    }

    private async Task<ServerStartResult> FinishEarlyAsync(RunManifest manifest, string runDirectory, OutcomeKind outcome, string message, string? details)
    {
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        try
        {
            var final = await runStore.FinishAsync(manifest, state, failure: details, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new ServerStartResult(new CommandResult(outcome, message, Details: details), final.RunId, runDirectory, [], final);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ServerStartResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not finalise server run evidence.", Details: $"{details} {exception.Message}"), manifest.RunId, runDirectory, [], null);
        }
    }

    private ServerStartResult Failure(OutcomeKind outcome, string message, string? details = null)
        => new(new CommandResult(outcome, message, Details: details), null, null, [], null);
}
