using System.Collections.Immutable;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal sealed record CleanRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public string Target { get; init; } = "default";
}

internal sealed record CleanResult(
    CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> PreservedActiveRunPaths,
    RunManifest? Manifest);

internal enum CleanTargetKind
{
    Test,
    Build,
    Coverage,
    Docs,
    Diagnostics,
    Benchmark,
    Package,
    Cache,
    Default,
    All
}

internal sealed record CleanTargetDescriptor(
    CleanTargetKind Kind,
    string Id,
    ImmutableArray<string> RelativePaths,
    bool RemoveArtifactContents = false);

internal interface IArtifactPathDeleter
{
    void DeleteFile(string path);

    void DeleteDirectory(string path);
}

internal sealed class ArtifactPathDeleter : IArtifactPathDeleter
{
    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path) => Directory.Delete(path);
}

internal sealed class CleanService
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly TimeSpan LeaseWait = TimeSpan.FromSeconds(30);
    private static readonly ImmutableArray<CleanTargetDescriptor> OwnedTargets =
    [
        new(CleanTargetKind.Test, "test", ["test"]),
        new(CleanTargetKind.Build, "build", ["build"]),
        new(CleanTargetKind.Coverage, "coverage", ["coverage"]),
        new(CleanTargetKind.Docs, "docs", ["docs"]),
        new(CleanTargetKind.Diagnostics, "diagnostics", ["diagnostics"]),
        new(CleanTargetKind.Benchmark, "benchmark", ["benchmark/runs"]),
        new(CleanTargetKind.Package, "package", ["package"]),
        new(CleanTargetKind.Cache, "cache", ["benchmark/cache"]),
        new(CleanTargetKind.Default, "default", ["bin", "obj", "publish", "build", "test", "coverage", "diagnostics", "docs", "temp"]),
        new(CleanTargetKind.All, "all", [""], RemoveArtifactContents: true)
    ];
    private static readonly ImmutableDictionary<string, CleanTargetDescriptor> OwnedTargetsById
        = OwnedTargets.ToImmutableDictionary(static target => target.Id, StringComparer.OrdinalIgnoreCase);

    private readonly ArtifactPaths _paths;
    private readonly ArtifactRunStore _runStore;
    private readonly IArtifactPathDeleter _deleter;

    public CleanService(ArtifactPaths paths, ArtifactRunStore runStore, IArtifactPathDeleter? deleter = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        _deleter = deleter ?? new ArtifactPathDeleter();
    }

    public async Task<CleanResult> CleanAsync(CleanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request, out var targetPaths, out var removeArtifactContents);
        if (validation is not null)
        {
            return Failure(OutcomeKind.ValidationFailure, validation);
        }

        ExecutionLease lease;
        try
        {
            lease = await ExecutionLease.AcquireExclusiveAsync(_paths.RepositoryRoot, LeaseWait, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Clean was cancelled while waiting for active DevOps operations.");
        }
        catch (TimeoutException exception)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not acquire the exclusive clean lease.", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not acquire the exclusive clean lease.", exception.Message);
        }

        await using (lease.ConfigureAwait(false))
        {
            RunManifest manifest;
            try
            {
                manifest = await _runStore.StartAsync(
                    "clean",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = request.Target.ToLowerInvariant() },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure(OutcomeKind.Cancelled, "Clean was cancelled before its run record was created.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Failure(OutcomeKind.InfrastructureFailure, "Could not create clean run evidence.", exception.Message);
            }

            var runDirectory = _paths.GetBuildRunDirectory(manifest.RunId);
            var removed = new List<string>();
            var preserved = new HashSet<string>(PathComparer());
            try
            {
                var activeRuns = FindActiveRunDirectories(targetPaths, removeArtifactContents);
                foreach (var path in targetPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (removeArtifactContents)
                    {
                        foreach (var child in Directory.EnumerateFileSystemEntries(_paths.ArtifactRoot).ToArray())
                        {
                            if (string.Equals(Path.GetFileName(child), "devops", PathComparison))
                            {
                                continue;
                            }

                            DeleteOwnedTree(child, activeRuns, preserved, removed, cancellationToken);
                        }
                    }
                    else
                    {
                        DeleteOwnedTree(path, activeRuns, preserved, removed, cancellationToken);
                    }
                }

                manifest = await _runStore.UpdateRunningAsync(
                    manifest,
                    sourceCommit: null,
                    sourceBranch: null,
                    sourceDirty: false,
                    cancellationToken: cancellationToken,
                    additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["removed-path-count"] = removed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["preserved-active-run-count"] = preserved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }).ConfigureAwait(false);
                manifest = await _runStore.FinishAsync(manifest, RunState.Succeeded, artifacts: ["clean-summary"], cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return new CleanResult(
                    new CommandResult(OutcomeKind.Success, $"Cleaned target '{request.Target}'."),
                    manifest.RunId,
                    runDirectory,
                    removed,
                    preserved.Order(PathComparer()).Select(ToArtifactRelative).ToArray(),
                    manifest);
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                return await FinishFailureAsync(manifest, runDirectory, removed, preserved, OutcomeKind.Cancelled, "Clean was cancelled.", exception.Message).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            {
                return await FinishFailureAsync(manifest, runDirectory, removed, preserved, OutcomeKind.InfrastructureFailure, "Clean failed; the run was not marked successful.", exception.Message).ConfigureAwait(false);
            }
        }
    }

    private string? Validate(CleanRequest request, out string[] targetPaths, out bool removeArtifactContents)
    {
        targetPaths = [];
        removeArtifactContents = false;
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }

        if (!PathsEqual(_paths.RepositoryRoot, request.Checkout.RepositoryRoot))
        {
            return "The clean service artefact root does not match the selected LeanCorpus checkout.";
        }

        if (string.IsNullOrWhiteSpace(request.Target) || !OwnedTargetsById.TryGetValue(request.Target, out var target))
        {
            return $"Unknown clean target '{request.Target}'. Valid targets: {string.Join(", ", OwnedTargets.Select(static item => item.Id).Order(StringComparer.Ordinal))}.";
        }

        removeArtifactContents = target.RemoveArtifactContents;
        targetPaths = target.RelativePaths.Select(relative => string.IsNullOrEmpty(relative)
            ? _paths.ArtifactRoot
            : Path.GetFullPath(Path.Combine(_paths.ArtifactRoot, relative.Replace('/', Path.DirectorySeparatorChar)))).ToArray();

        foreach (var path in targetPaths)
        {
            if (!IsWithinOrEqual(_paths.ArtifactRoot, path) || (!removeArtifactContents && PathsEqual(_paths.ArtifactRoot, path)))
            {
                return $"Clean target '{request.Target}' resolves outside an owned artefact location.";
            }

            var link = FindExistingLinkComponent(_paths.ArtifactRoot, path);
            if (link is not null)
            {
                return $"Refusing to clean through link or reparse point '{link}'.";
            }
        }

        return null;
    }

    private HashSet<string> FindActiveRunDirectories(IReadOnlyList<string> targets, bool removeArtifactContents)
    {
        var knownRunRoots = ArtifactPaths.RunCategories.Select(category => _paths.GetRunRoot(category.Id));
        var runRoots = knownRunRoots.Where(runRoot => removeArtifactContents || targets.Any(target => IsWithinOrEqual(target, runRoot) || IsWithinOrEqual(runRoot, target)));
        var manifestPaths = runRoots.SelectMany(root => EnumerateManifestPaths(root, skipDevOpsRoot: false)).Distinct(PathComparer());
        var protectedRuns = new HashSet<string>(PathComparer());
        foreach (var manifestPath in manifestPaths)
        {
            var directory = Path.GetDirectoryName(manifestPath)!;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                if (!document.RootElement.TryGetProperty("state", out var state))
                {
                    protectedRuns.Add(directory);
                    continue;
                }

                var active = state.ValueKind switch
                {
                    JsonValueKind.String => state.GetString() is "created" or "running",
                    JsonValueKind.Number => state.TryGetInt32(out var numericState) && numericState is 0 or 1,
                    _ => true
                };
                if (active)
                {
                    protectedRuns.Add(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Unknown or unreadable run evidence is retained rather than removed.
                protectedRuns.Add(directory);
            }
        }

        return protectedRuns;
    }

    private static IEnumerable<string> EnumerateManifestPaths(string directory, bool skipDevOpsRoot)
    {
        if (!TryGetAttributes(directory, out var attributes) || (attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            yield break;
        }

        var canonical = Path.Combine(directory, "run.json");
        var historical = Path.Combine(directory, "manifest.json");
        if (File.Exists(canonical))
        {
            yield return canonical;
        }
        else if (File.Exists(historical))
        {
            yield return historical;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (skipDevOpsRoot && PathsEqual(directory, Path.GetDirectoryName(child)!) && string.Equals(Path.GetFileName(child), "devops", PathComparison))
            {
                continue;
            }

            foreach (var manifestPath in EnumerateManifestPaths(child, skipDevOpsRoot: false))
            {
                yield return manifestPath;
            }
        }
    }

    private void DeleteOwnedTree(
        string path,
        IReadOnlySet<string> activeRuns,
        HashSet<string> preserved,
        List<string> removed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (activeRuns.Contains(fullPath))
        {
            preserved.Add(fullPath);
            return;
        }

        if (!TryGetAttributes(fullPath, out var attributes))
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            DeleteLink(fullPath, attributes);
            removed.Add(ToArtifactRelative(fullPath));
            return;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            _deleter.DeleteFile(fullPath);
            removed.Add(ToArtifactRelative(fullPath));
            return;
        }

        foreach (var child in Directory.EnumerateFileSystemEntries(fullPath).ToArray())
        {
            DeleteOwnedTree(child, activeRuns, preserved, removed, cancellationToken);
        }

        if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            _deleter.DeleteDirectory(fullPath);
            removed.Add(ToArtifactRelative(fullPath));
        }
    }

    private void DeleteLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            _deleter.DeleteDirectory(path);
        }
        else
        {
            _deleter.DeleteFile(path);
        }
    }

    private string? FindExistingLinkComponent(string root, string target)
    {
        if (TryGetAttributes(root, out var rootAttributes) && (rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            return root;
        }

        var relative = Path.GetRelativePath(root, target);
        var current = root;
        if (relative == ".")
        {
            return null;
        }

        foreach (var component in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (TryGetAttributes(current, out var attributes) && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return current;
            }
        }

        return null;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private async Task<CleanResult> FinishFailureAsync(
        RunManifest manifest,
        string runDirectory,
        IReadOnlyList<string> removed,
        HashSet<string> preserved,
        OutcomeKind outcome,
        string message,
        string details)
    {
        var state = outcome == OutcomeKind.Cancelled ? RunState.Cancelled : RunState.Failed;
        try
        {
            var final = await _runStore.FinishAsync(manifest, state, artifacts: ["clean-summary"], failure: details, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new CleanResult(new CommandResult(outcome, message, Details: details), final.RunId, runDirectory, removed,
                preserved.Order(PathComparer()).Select(ToArtifactRelative).ToArray(), final);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new CleanResult(
                new CommandResult(OutcomeKind.InfrastructureFailure, "Clean failed and its run evidence could not be finalised.", Details: $"{details} {exception.Message}"),
                manifest.RunId,
                runDirectory,
                removed,
                preserved.Order(PathComparer()).Select(ToArtifactRelative).ToArray(),
                null);
        }
    }

    private string ToArtifactRelative(string path)
        => Path.GetRelativePath(_paths.ArtifactRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsWithinOrEqual(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(normalizedRoot, normalizedPath, PathComparison) || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static CleanResult Failure(OutcomeKind outcome, string message, string? details = null)
        => new(new CommandResult(outcome, message, Details: details), null, null, [], [], null);
}
