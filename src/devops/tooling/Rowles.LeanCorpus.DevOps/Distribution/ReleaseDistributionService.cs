using System.Reflection;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Distribution;

internal sealed record ReleaseDistributionRequest
{
    public required CheckoutIdentity Checkout { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public string Configuration { get; init; } = "Release";
    public bool SelfContained { get; init; }
}

internal sealed record ReleaseDistributionResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory, ReleaseArchiveResult? Archive);

internal sealed class ReleaseDistributionService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader)
{
    private static readonly HashSet<string> SupportedRuntimeIdentifiers = new(StringComparer.Ordinal)
    {
        "win-x64", "linux-x64", "osx-arm64", "linux-arm64"
    };

    public async Task<ReleaseDistributionResult> BuildAsync(ReleaseDistributionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return Failure(OutcomeKind.ValidationFailure, validation);
        }

        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync("distribute", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtimeIdentifier"] = request.RuntimeIdentifier,
                ["configuration"] = request.Configuration,
                ["framework"] = "net10.0",
                ["singleFile"] = "true",
                ["selfContained"] = request.SelfContained.ToString().ToLowerInvariant()
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not create distribution run evidence.", exception.Message);
        }

        var runDirectory = artifactPaths.GetRunDirectory("distribution", manifest.RunId);
        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(git.Commit) || string.IsNullOrWhiteSpace(git.Branch))
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome == OutcomeKind.Success ? OutcomeKind.InfrastructureFailure : git.Outcome,
                    "Could not record release source identity.", git.Failure, null, null).ConfigureAwait(false);
            }
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);

            var publishDirectory = Path.Combine(runDirectory, "publish");
            var publish = await new DotNetPublishService(
                artifactPaths,
                new ArtifactRunStore(artifactPaths, runCategory: "build"),
                processRunner,
                sdkResolver,
                provenanceReader).PublishAsync(new DotNetPublishRequest
                {
                    Checkout = request.Checkout,
                    ProjectPath = RepositoryProjectCatalog.GetRelativePath(RepositoryProjectRole.DevOpsHost),
                    Configuration = request.Configuration,
                    Framework = "net10.0",
                    RuntimeIdentifier = request.RuntimeIdentifier,
                    OutputPath = Path.GetRelativePath(artifactPaths.RepositoryRoot, publishDirectory),
                    SelfContained = request.SelfContained,
                    SingleFile = true,
                    IncludeNativeLibrariesForSelfExtract = true,
                    SuppressDebugSymbols = true,
                    RestoreLockedMode = true,
                    ReleaseArchiveProfile = true
                }, cancellationToken).ConfigureAwait(false);

            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["publishRunId"] = publish.RunId?.ToString("D") ?? string.Empty,
                    ["publishRunDirectory"] = publish.RunDirectory ?? string.Empty,
                    ["productVersion"] = GetProductVersion()
                }).ConfigureAwait(false);
            if (publish.CommandResult.Outcome != OutcomeKind.Success || publish.RunId is null)
            {
                return await FinishEarlyAsync(manifest, runDirectory, publish.CommandResult.Outcome,
                    "Release executable publish failed.", publish.CommandResult.Details, publish.CommandResult.RawChildExitCode, publish.RunId).ConfigureAwait(false);
            }

            var projectRoot = Path.Combine(artifactPaths.RepositoryRoot, "src", "devops", "Rowles.LeanCorpus.DevOps");
            var archive = await new ReleaseArchiveBuilder().BuildAsync(new ReleaseArchiveRequest
            {
                RepositoryRoot = artifactPaths.RepositoryRoot,
                ProjectRoot = projectRoot,
                PublishDirectory = publishDirectory,
                OutputDirectory = Path.Combine(runDirectory, "release"),
                ProductVersion = GetProductVersion(),
                SourceCommit = git.Commit,
                SourceBranch = git.Branch,
                SourceDirty = git.Dirty,
                RuntimeIdentifier = request.RuntimeIdentifier,
                SelfContained = request.SelfContained,
                PublishRunId = publish.RunId.Value
            }, cancellationToken).ConfigureAwait(false);

            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                toolVersions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DevOps"] = GetProductVersion() },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            manifest = await runStore.FinishAsync(manifest, RunState.Succeeded, 0,
            [
                Path.GetRelativePath(runDirectory, archive.ArchivePath),
                Path.GetRelativePath(runDirectory, archive.ChecksumPath),
                Path.Combine("release", Path.GetFileName(archive.BundleDirectory), "distribution-manifest.json"),
                Path.Combine("release", Path.GetFileName(archive.BundleDirectory), "SHA256SUMS")
            ], cancellationToken: CancellationToken.None).ConfigureAwait(false);

            return new ReleaseDistributionResult(
                new CommandResult(OutcomeKind.Success, "Release archive created.", Details: $"{archive.ArchivePath}{Environment.NewLine}SHA-256: {archive.ArchiveSha256}"),
                manifest.RunId,
                runDirectory,
                archive);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Release distribution was cancelled.", exception.Message, null, null).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, "Could not create the release archive.", exception.Message, null, null).ConfigureAwait(false);
        }
    }

    private string? Validate(ReleaseDistributionRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }
        if (!string.Equals(Path.GetFullPath(request.Checkout.RepositoryRoot), artifactPaths.RepositoryRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return "The distribution artefact root does not match the selected checkout.";
        }
        if (!SupportedRuntimeIdentifiers.Contains(request.RuntimeIdentifier))
        {
            return $"Release distribution is qualified only for: {string.Join(", ", SupportedRuntimeIdentifiers.Order(StringComparer.Ordinal))}.";
        }
        if (string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Any(char.IsControl))
        {
            return "Distribution configuration must be a non-empty single-line value.";
        }
        return null;
    }

    private async Task<ReleaseDistributionResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details,
        int? childExitCode,
        Guid? publishRunId)
    {
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        try
        {
            var inputs = manifest.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (publishRunId is not null)
            {
                inputs["publishRunId"] = publishRunId.Value.ToString("D");
            }
            var failed = manifest with { Inputs = inputs };
            manifest = await runStore.FinishAsync(failed, state, childExitCode, ["run.json"], details, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            details = string.Join(Environment.NewLine, new[] { details, exception.Message }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        }
        return new ReleaseDistributionResult(new CommandResult(outcome, message, childExitCode, details), manifest.RunId, runDirectory, null);
    }

    private static string GetProductVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var semVer = informationalVersion?.Split('+', 2)[0];
        if (semVer is not null && Regex.IsMatch(semVer, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant))
        {
            return semVer;
        }

        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static ReleaseDistributionResult Failure(OutcomeKind outcome, string message, string? details = null)
        => new(new CommandResult(outcome, message, Details: details), null, null, null);
}
