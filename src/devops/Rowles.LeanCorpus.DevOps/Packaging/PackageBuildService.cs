using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed class PackageBuildService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    string dotnetExecutable = "dotnet")
{
    public async Task<PackageBuildResult> PackAsync(PackageBuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return new PackageBuildResult(new CommandResult(OutcomeKind.ValidationFailure, validation), null, null, null);
        }

        PackagePolicy policy;
        Dictionary<string, PackageProjectMetadata> projectMetadata;
        string policyPath;
        try
        {
            policyPath = Path.Combine(artifactPaths.RepositoryRoot, "scripts", "devops", "packaging", "package-policy.json");
            policy = PackagePolicyReader.Read(policyPath, artifactPaths.RepositoryRoot);
            var selected = SelectPackages(policy, request);
            ValidateCompressionSelection(selected, policy.CompressionContract);
            projectMetadata = selected.ToDictionary(
                static entry => entry.PackageId,
                entry =>
                {
                    var projectPath = Path.Combine(artifactPaths.RepositoryRoot, entry.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
                    var metadata = PackageProjectInspector.ReadMetadata(projectPath);
                    if (!string.Equals(metadata.PackageId, entry.PackageId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Policy package ID '{entry.PackageId}' does not match project package ID '{metadata.PackageId}' in {projectPath}.");
                    }
                    if (string.IsNullOrWhiteSpace(metadata.Version))
                    {
                        throw new InvalidDataException($"Project has no explicit Version for {entry.PackageId}.");
                    }
                    return metadata;
                }, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Xml.XmlException)
        {
            return new PackageBuildResult(new CommandResult(OutcomeKind.ValidationFailure, "Package policy validation failed.", Details: exception.Message), null, null, null);
        }

        ExecutionLease lease;
        try
        {
            lease = await ExecutionLease.AcquireSharedAsync(artifactPaths.RepositoryRoot, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PackageBuildResult(new CommandResult(OutcomeKind.Cancelled, "Package generation was cancelled while waiting for the shared build lease."), null, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return new PackageBuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not acquire the shared build lease.", Details: exception.Message), null, null, null);
        }

        await using var executionLease = lease;
        var selectedEntries = SelectPackages(policy, request);
        var outputDirectory = Path.Combine(artifactPaths.ArtifactRoot, "package", "release");
        if (request.Clean)
        {
            try
            {
                DeleteOwnedOutput(outputDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new PackageBuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not clean the owned package release directory.", Details: exception.Message), null, null, null);
            }
        }
        Directory.CreateDirectory(outputDirectory);

        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync("pack", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["configuration"] = request.Configuration,
                ["compressionOnly"] = request.CompressionOnly.ToString().ToLowerInvariant(),
                ["noBuild"] = request.NoBuild.ToString().ToLowerInvariant(),
                ["clean"] = request.Clean.ToString().ToLowerInvariant(),
                ["packageCount"] = selectedEntries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PackageBuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not create package run evidence.", Details: exception.Message), null, null, null);
        }

        var runDirectory = artifactPaths.GetRunDirectory("package", manifest.RunId);
        var childExitCodes = new List<int>();
        var artefacts = new List<string>();
        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record package source provenance.", git.Failure, childExitCodes, artefacts).ConfigureAwait(false);
            }
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, sdk.Outcome, "Could not resolve the SDK selected by global.json.", sdk.Failure, childExitCodes, artefacts).ConfigureAwait(false);
            }
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, cancellationToken).ConfigureAwait(false);

            var expectedPackages = new HashSet<string>(StringComparer.Ordinal);
            var expectedSymbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in selectedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = projectMetadata[entry.PackageId];
                var packageName = $"{entry.PackageId}.{metadata.Version}.nupkg";
                expectedPackages.Add(packageName);
                if (entry.RequireSymbols)
                {
                    expectedSymbols.Add($"{entry.PackageId}.{metadata.Version}.snupkg");
                }

                var projectPath = Path.Combine(artifactPaths.RepositoryRoot, entry.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
                var propsPath = CreateValidationProps(entry, Path.GetFileNameWithoutExtension(projectPath), runDirectory);
                try
                {
                    var arguments = BuildPackArguments(projectPath, outputDirectory, request, entry, propsPath);
                    manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                        cancellationToken: cancellationToken,
                        additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [$"package.{entry.PackageId}.arguments"] = JsonSerializer.Serialize(arguments)
                        }).ConfigureAwait(false);
                    var child = await processRunner.RunAsync(new ProcessRequest
                    {
                        ExecutablePath = dotnetExecutable,
                        Arguments = arguments,
                        WorkingDirectory = artifactPaths.RepositoryRoot,
                        StandardOutputLogPath = Path.Combine(runDirectory, $"pack-{SafeFileName(entry.PackageId)}.stdout.log"),
                        StandardErrorLogPath = Path.Combine(runDirectory, $"pack-{SafeFileName(entry.PackageId)}.stderr.log"),
                        Timeout = TimeSpan.FromMinutes(60),
                        ProgressInterval = TimeSpan.FromSeconds(10)
                    }, cancellationToken).ConfigureAwait(false);
                    if (child.RawExitCode is { } exitCode)
                    {
                        childExitCodes.Add(exitCode);
                    }
                    artefacts.Add(Path.GetFileName(child.StandardOutputLogPath));
                    artefacts.Add(Path.GetFileName(child.StandardErrorLogPath));
                    if (child.Outcome != OutcomeKind.Success)
                    {
                        return await FinishEarlyAsync(manifest, runDirectory, child.Outcome,
                            $"dotnet pack failed for {entry.PackageId}.", child.Failure, childExitCodes, artefacts, child.RawExitCode).ConfigureAwait(false);
                    }

                    var packagePath = Path.Combine(outputDirectory, packageName);
                    if (!File.Exists(packagePath))
                    {
                        return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.CommandFailure,
                            $"Packing {entry.ProjectPath} did not produce the expected {entry.PackageId} {metadata.Version} package.", null, childExitCodes, artefacts, child.RawExitCode).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (propsPath is not null)
                    {
                        File.Delete(propsPath);
                    }
                }
            }

            ValidateOutputSet(outputDirectory, expectedPackages, expectedSymbols);
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            var packageRows = new List<PackageManifestEntry>();
            foreach (var entry in selectedEntries)
            {
                var metadata = projectMetadata[entry.PackageId];
                var packageName = $"{entry.PackageId}.{metadata.Version}.nupkg";
                var packagePath = Path.Combine(outputDirectory, packageName);
                var symbolName = expectedSymbols.Contains($"{entry.PackageId}.{metadata.Version}.snupkg")
                    ? $"{entry.PackageId}.{metadata.Version}.snupkg"
                    : null;
                var projectPath = Path.Combine(artifactPaths.RepositoryRoot, entry.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
                var dependencyIds = PackageProjectInspector.ReadDependencyIds(projectPath, artifactPaths.RepositoryRoot);
                PackageArchiveInspector.Validate(packagePath,
                    symbolName is null ? null : Path.Combine(outputDirectory, symbolName), entry, metadata, dependencyIds);
                if (policy.CompressionContract.CodecPackageIds.Contains(entry.PackageId, StringComparer.Ordinal))
                {
                    PackageArchiveInspector.ValidateCompressionDependencyRange(packagePath, entry, policy.CompressionContract);
                }
                packageRows.Add(new PackageManifestEntry(entry.PackageId, metadata.Version, packageName,
                    new FileInfo(packagePath).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packagePath)))));
                artefacts.Add(Path.GetRelativePath(artifactPaths.ArtifactRoot, packagePath).Replace('\\', '/'));
                if (symbolName is not null)
                {
                    var symbolPath = Path.Combine(outputDirectory, symbolName);
                    packageRows.Add(new PackageManifestEntry(entry.PackageId, metadata.Version, symbolName,
                        new FileInfo(symbolPath).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(symbolPath)))));
                    artefacts.Add(Path.GetRelativePath(artifactPaths.ArtifactRoot, symbolPath).Replace('\\', '/'));
                }
            }

            var packageManifest = new PackageManifestDocument(1, DateTimeOffset.UtcNow, git.Commit, request.Configuration, packageRows);
            await new AtomicFileWriter().WriteReplaceAsync(manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(packageManifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }), cancellationToken).ConfigureAwait(false);
            artefacts.Add(Path.GetRelativePath(artifactPaths.ArtifactRoot, manifestPath).Replace('\\', '/'));

            manifest = await runStore.FinishAsync(manifest, RunState.Succeeded, artifacts: artefacts, cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
            return new PackageBuildResult(new CommandResult(OutcomeKind.Success, $"Packages written to: {outputDirectory}."), manifest.RunId, runDirectory, manifest);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Package generation was cancelled.", exception.Message, childExitCodes, artefacts).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, "Package generation or archive validation failed.", exception.Message, childExitCodes, artefacts).ConfigureAwait(false);
        }
    }

    private string? Validate(PackageBuildRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }
        if (!PathsEqual(artifactPaths.RepositoryRoot, request.Checkout.RepositoryRoot))
        {
            return "The package service artefact root does not match the selected LeanCorpus checkout.";
        }
        if (string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Contains('\n') || request.Configuration.Contains('\r'))
        {
            return "Package configuration must be a non-empty single-line value.";
        }
        return null;
    }

    private static IReadOnlyList<PackagePolicyEntry> SelectPackages(PackagePolicy policy, PackageBuildRequest request)
        => (request.CompressionOnly
                ? policy.Packages.Where(entry => entry.PackageGroups.Contains(policy.CompressionContract.PackageGroup, StringComparer.Ordinal))
                : policy.Packages)
            .ToArray();

    private static void ValidateCompressionSelection(IReadOnlyList<PackagePolicyEntry> selected, CompressionPackageContract contract)
    {
        var selectedIds = selected.Select(static entry => entry.PackageId).ToHashSet(StringComparer.Ordinal);
        var missing = contract.RequiredPackageIds.Where(id => !selectedIds.Contains(id)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException($"Package policy selection omitted required compression package(s): {string.Join(", ", missing)}.");
        }
    }

    private IReadOnlyList<string> BuildPackArguments(string projectPath, string outputDirectory, PackageBuildRequest request, PackagePolicyEntry entry, string? propsPath)
    {
        var arguments = new List<string>
        {
            "pack", projectPath, "-c", request.Configuration, "--output", outputDirectory,
            "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off",
            $"-p:IncludeSymbols={entry.RequireSymbols.ToString().ToLowerInvariant()}",
            "-p:SymbolPackageFormat=snupkg"
        };
        if (propsPath is not null)
        {
            arguments.Add($"-p:CustomAfterMicrosoftCommonProps={propsPath}");
        }
        if (request.NoBuild)
        {
            arguments.Add("--no-build");
        }
        return arguments;
    }

    private static string? CreateValidationProps(PackagePolicyEntry entry, string projectName, string runDirectory)
    {
        if (!entry.EnableApiCompatibility || entry.Kind is "analyzer" or "application")
        {
            return null;
        }
        var baseline = string.IsNullOrWhiteSpace(entry.BaselineVersion)
            ? string.Empty
            : $"<PackageValidationBaselineVersion>{System.Security.SecurityElement.Escape(entry.BaselineVersion)}</PackageValidationBaselineVersion>";
        var strict = entry.StrictBaseline ? "<EnableStrictModeForBaselineValidation>true</EnableStrictModeForBaselineValidation>" : string.Empty;
        var xml = $"<Project><PropertyGroup Condition=\"'$(MSBuildProjectName)' == '{System.Security.SecurityElement.Escape(projectName)}'\"><EnablePackageValidation>true</EnablePackageValidation>{strict}{baseline}</PropertyGroup></Project>";
        var propsPath = Path.Combine(runDirectory, $"package-validation-{Guid.NewGuid():N}.props");
        File.WriteAllText(propsPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return propsPath;
    }

    private void ValidateOutputSet(string outputDirectory, HashSet<string> expectedPackages, HashSet<string> expectedSymbols)
    {
        var actualPackages = Directory.EnumerateFiles(outputDirectory, "*.nupkg").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var actualSymbols = Directory.EnumerateFiles(outputDirectory, "*.snupkg").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var unexpected = actualPackages.Except(expectedPackages, StringComparer.Ordinal).Concat(actualSymbols.Except(expectedSymbols, StringComparer.Ordinal)).ToArray();
        var missing = expectedPackages.Except(actualPackages, StringComparer.Ordinal).Concat(expectedSymbols.Except(actualSymbols, StringComparer.Ordinal)).ToArray();
        if (unexpected.Length > 0)
        {
            throw new InvalidDataException($"Unrepresented package or symbol archive(s) were produced: {string.Join(", ", unexpected)}.");
        }
        if (missing.Length > 0)
        {
            throw new InvalidDataException($"Policy package or symbol archive(s) were not emitted: {string.Join(", ", missing)}.");
        }
    }

    private void DeleteOwnedOutput(string outputDirectory)
    {
        var relative = Path.GetRelativePath(artifactPaths.ArtifactRoot, outputDirectory);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Package release output resolved outside the artefact root.");
        }

        var current = artifactPaths.ArtifactRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Package release path contains a link and cannot be cleaned: {current}");
            }
        }
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
        else if (File.Exists(outputDirectory))
        {
            throw new InvalidOperationException($"Package release output is not a directory: {outputDirectory}");
        }
    }

    private async Task<PackageBuildResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? failure,
        IReadOnlyList<int> childExitCodes,
        IReadOnlyList<string> artifacts,
        int? childExitCode = null)
    {
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        var finished = await runStore.FinishAsync(manifest, state, childExitCode, artifacts, failure,
            CancellationToken.None, childExitCodes).ConfigureAwait(false);
        return new PackageBuildResult(new CommandResult(outcome, message, childExitCode, failure), manifest.RunId, runDirectory, finished);
    }

    private static string SafeFileName(string value)
        => string.Concat(value.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_'));

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison);
    }

    private sealed record PackageManifestDocument(int SchemaVersion, DateTimeOffset GeneratedAtUtc, string? SourceCommit, string Configuration, IReadOnlyList<PackageManifestEntry> Packages);

    private sealed record PackageManifestEntry(string PackageId, string Version, string Path, long Size, string Sha256);
}
