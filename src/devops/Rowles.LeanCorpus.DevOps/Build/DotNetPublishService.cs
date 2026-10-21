using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed class DotNetPublishService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    string dotnetExecutable = "dotnet")
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<DotNetPublishResult> PublishAsync(DotNetPublishRequest request, CancellationToken cancellationToken = default)
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
            return Failure(OutcomeKind.Cancelled, "Publish was cancelled while waiting for the DevOps execution lease.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not acquire the shared publish lease.", exception.Message);
        }

        await using var executionLease = lease;
        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync("publish", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["project"] = request.ProjectPath,
                ["configuration"] = request.Configuration,
                ["framework"] = request.Framework ?? string.Empty,
                ["runtimeIdentifier"] = request.RuntimeIdentifier ?? string.Empty,
                ["output"] = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.OutputPath)),
                ["noRestore"] = request.NoRestore.ToString().ToLowerInvariant(),
                ["selfContained"] = request.SelfContained.ToString().ToLowerInvariant(),
                ["singleFile"] = request.SingleFile.ToString().ToLowerInvariant(),
                ["publish-aot"] = request.PublishAot.ToString().ToLowerInvariant(),
                ["publish-trimmed"] = request.PublishTrimmed.ToString().ToLowerInvariant()
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Publish was cancelled before its run record was created.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not create publish run evidence.", exception.Message);
        }

        var runDirectory = artifactPaths.GetBuildRunDirectory(manifest.RunId);
        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record source provenance for publish.", git.Failure).ConfigureAwait(false);
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

            var arguments = BuildArguments(request);
            manifest = await runStore.UpdateRunningAsync(
                manifest,
                git.Commit,
                git.Branch,
                git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effectiveArguments"] = System.Text.Json.JsonSerializer.Serialize(arguments)
                }).ConfigureAwait(false);
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
                OutcomeKind.Success => "Publish succeeded.",
                OutcomeKind.CommandFailure => $"dotnet publish failed with exit code {child.RawExitCode}.",
                OutcomeKind.Cancelled => "Publish was cancelled.",
                OutcomeKind.Timeout => "Publish timed out.",
                _ => "Publish infrastructure failed."
            };
            return new DotNetPublishResult(new CommandResult(child.Outcome, message, child.RawExitCode, child.Failure), manifest.RunId, runDirectory, arguments, manifest);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Publish was cancelled.", exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, "Publish infrastructure failed.", exception.Message).ConfigureAwait(false);
        }
    }

    private string? Validate(DotNetPublishRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }

        if (!PathsEqual(artifactPaths.RepositoryRoot, request.Checkout.RepositoryRoot))
        {
            return "The publish service artefact root does not match the selected LeanCorpus checkout.";
        }

        if (string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Contains('\n') || request.Configuration.Contains('\r'))
        {
            return "Publish configuration must be a non-empty single-line value.";
        }

        string projectPath;
        string outputPath;
        try
        {
            projectPath = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.ProjectPath));
            outputPath = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.OutputPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Publish path is invalid: {exception.Message}";
        }

        if (!IsWithin(request.Checkout.RepositoryRoot, projectPath) || !File.Exists(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return $"Project path '{request.ProjectPath}' must identify an existing project inside the selected checkout.";
        }

        if (!IsWithin(request.Checkout.RepositoryRoot, outputPath))
        {
            return "Publish output must be a directory inside the selected checkout.";
        }

        if (string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Any(char.IsControl))
        {
            return "Publish configuration must be a non-empty value without control characters.";
        }

        if (request.Framework is { } framework && !Regex.IsMatch(framework, "^net[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant))
        {
            return $"Target framework '{framework}' is not a supported framework moniker.";
        }

        if (request.RuntimeIdentifier is { } rid && !Regex.IsMatch(rid, "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant))
        {
            return $"Runtime identifier '{rid}' is invalid.";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildArguments(DotNetPublishRequest request)
    {
        var projectPath = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.ProjectPath));
        var outputPath = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.OutputPath));
        var arguments = new List<string> { "publish", projectPath, "-c", request.Configuration };
        if (request.Framework is not null)
        {
            arguments.AddRange(["-f", request.Framework]);
        }

        if (request.RuntimeIdentifier is not null)
        {
            arguments.AddRange(["-r", request.RuntimeIdentifier]);
        }

        arguments.AddRange(["-o", outputPath]);
        if (request.NoRestore)
        {
            arguments.Add("--no-restore");
        }

        arguments.AddRange(
        [
            "--self-contained", request.SelfContained ? "true" : "false",
            $"-p:PublishSingleFile={request.SingleFile.ToString().ToLowerInvariant()}",
            $"-p:PublishAot={request.PublishAot.ToString().ToLowerInvariant()}",
            $"-p:PublishTrimmed={request.PublishTrimmed.ToString().ToLowerInvariant()}",
            "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off"
        ]);
        return arguments;
    }

    private async Task<DotNetPublishResult> FinishEarlyAsync(RunManifest manifest, string runDirectory, OutcomeKind outcome, string message, string? details)
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
            return new DotNetPublishResult(new CommandResult(outcome, message, Details: details), final.RunId, runDirectory, [], final);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new DotNetPublishResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not finalise publish run evidence.", Details: $"{details} {exception.Message}"), manifest.RunId, runDirectory, [], null);
        }
    }

    private bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    private static DotNetPublishResult Failure(OutcomeKind outcome, string message, string? details = null)
        => new(new CommandResult(outcome, message, Details: details), null, null, [], null);
}
