using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record HistoricalFixtureGenerationRequest(
    CheckoutIdentity Checkout,
    string Version,
    string Scenario,
    string PackageSource,
    string? OutputDirectory);

internal sealed record HistoricalFixtureGenerationResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal sealed class HistoricalFixtureGenerationService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    string dotnetExecutable = "dotnet")
{
    private const string DefaultPackageSource = "https://api.nuget.org/v3/index.json";
    private const string FixtureTree = HistoricalFixtureCatalog.FixtureTree;
    private const string FixtureRoot = "src/devops/tests/Rowles.LeanCorpus.Tests.Shared/Fixtures/Indexes";

    public async Task<HistoricalFixtureGenerationResult> GenerateAsync(
        HistoricalFixtureGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? sourceError = ValidatePackageSource(request.PackageSource);
        if (sourceError is not null)
            return new HistoricalFixtureGenerationResult(new CommandResult(OutcomeKind.ValidationFailure, sourceError), null, null);

        string repositoryRoot = request.Checkout.RepositoryRoot;
        HistoricalFixtureCatalog catalog;
        HistoricalFixtureVersion version;
        HistoricalFixtureScenario scenario;
        try
        {
            catalog = HistoricalFixtureCatalog.Load(repositoryRoot);
            version = catalog.GetVersion(request.Version);
            scenario = catalog.GetScenario(request.Version, request.Scenario);
        }
        catch (KeyNotFoundException exception)
        {
            return new HistoricalFixtureGenerationResult(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            return new HistoricalFixtureGenerationResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not load or validate the historical fixture catalog.", Details: exception.Message), null, null);
        }

        var start = await runStore.StartAsync("data fixtures",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packageSource"] = SafePackageSource(request.PackageSource),
                ["outputDirectory"] = request.OutputDirectory ?? "<run-evidence>/generated",
                ["fixtureVersion"] = version.Version,
                ["scenario"] = scenario.Id,
                ["packageSha256"] = version.PackageSha256,
                ["packageAssemblySha256"] = version.PackageAssemblySha256
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        string runDirectory = artifactPaths.GetRunDirectory("data", start.RunId);
        string outputDirectory = Path.GetFullPath(request.OutputDirectory is null
            ? Path.Combine(runDirectory, "generated")
            : Path.IsPathRooted(request.OutputDirectory)
                ? request.OutputDirectory
                : Path.Combine(repositoryRoot, request.OutputDirectory));
        string generationRoot = Path.Combine(artifactPaths.ArtifactRoot, "temp", "historical-fixtures", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(generationRoot, "source");
        string packageRoot = Path.Combine(generationRoot, "packages");
        string generatedRoot = Path.Combine(generationRoot, "generated");
        RunManifest manifest = start;
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();

        try
        {
            if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
                return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.ValidationFailure,
                    "Historical fixture output directory already exists; select a new empty path.", outputDirectory).ConfigureAwait(false);

            GitProvenance git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome,
                    "Could not record historical fixture source provenance.", git.Failure).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(generationRoot);
            Directory.CreateDirectory(generatedRoot);
            CopyTree(HistoricalFixtureCatalog.ResolveCatalogPath(repositoryRoot, FixtureTree), sourceRoot);
            string projectRelativePath = version.ProjectPath.StartsWith(FixtureTree + "/", StringComparison.Ordinal)
                ? version.ProjectPath[(FixtureTree.Length + 1)..]
                : throw new InvalidDataException($"Historical project path '{version.ProjectPath}' is outside the fixture tree.");
            string projectPath = SafeChildPath(sourceRoot, projectRelativePath);
            string versionDirectory = Path.GetDirectoryName(projectPath)!;
            string nugetConfigPath = Path.Combine(sourceRoot, "NuGet.Config");
            await WriteNuGetConfigAsync(nugetConfigPath, request.PackageSource, cancellationToken).ConfigureAwait(false);

            string isolatedPackageRoot = Path.Combine(packageRoot, "global-packages");
            ProcessResult restore = await RunChildAsync(
                BuildRestoreArguments(projectPath, isolatedPackageRoot, nugetConfigPath),
                versionDirectory, runDirectory, "restore", "Restoring the exact historical package from the isolated source.", cancellationToken).ConfigureAwait(false);
            AddChild(restore, childExitCodes, artifacts, "restore");
            if (restore.Outcome != OutcomeKind.Success)
                return await FinishProcessFailureAsync(manifest, runDirectory, restore, childExitCodes, artifacts,
                    "Could not restore the pinned historical fixture project.", generationRoot).ConfigureAwait(false);

            string packagePath = Path.Combine(isolatedPackageRoot,
                version.PackageId.ToLowerInvariant(), version.PackageVersion.ToLowerInvariant(),
                $"{version.PackageId.ToLowerInvariant()}.{version.PackageVersion.ToLowerInvariant()}.nupkg");
            if (!File.Exists(packagePath) || !string.Equals(Hash(File.ReadAllBytes(packagePath)), version.PackageSha256, StringComparison.Ordinal))
                return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                    "The isolated restore did not provide the catalogued package archive.", packagePath,
                    artifacts, childExitCodes, generationRoot).ConfigureAwait(false);

            ProcessResult build = await RunChildAsync(
                BuildArguments(projectPath), versionDirectory, runDirectory, "build",
                "Building the isolated historical fixture project.", cancellationToken).ConfigureAwait(false);
            AddChild(build, childExitCodes, artifacts, "build");
            if (build.Outcome != OutcomeKind.Success)
                return await FinishProcessFailureAsync(manifest, runDirectory, build, childExitCodes, artifacts,
                    "The pinned historical fixture project did not build.", generationRoot).ConfigureAwait(false);

            string generatorPath = Path.Combine(versionDirectory, "bin", "Release", version.TargetFramework, "Generator.dll");
            ProcessResult generate = await RunChildAsync(
                [generatorPath, scenario.Id, generatedRoot, packagePath, version.PackageSha256,
                    version.PackageAssemblySha256, version.AssemblyVersion],
                versionDirectory, runDirectory, "generator", $"Generating historical {scenario.Id} fixtures with the pinned package.", cancellationToken)
                .ConfigureAwait(false);
            AddChild(generate, childExitCodes, artifacts, "generator");
            if (generate.Outcome != OutcomeKind.Success)
                return await FinishProcessFailureAsync(manifest, runDirectory, generate, childExitCodes, artifacts,
                    $"Historical fixture scenario '{scenario.Id}' failed.", generationRoot).ConfigureAwait(false);

            var generatedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (HistoricalFixtureOutput output in scenario.Outputs)
            {
                string manifestPath = SafeChildPath(generatedRoot, output.ManifestFile);
                string archiveRoot = output.ArchiveRoot == "." ? generatedRoot : SafeChildPath(generatedRoot, output.ArchiveRoot);
                byte[]? canonicalArchive = LoadCanonicalArchive(repositoryRoot, output);
                IReadOnlyDictionary<string, ArchiveEntryData>? canonicalEntries = canonicalArchive is null
                    ? null
                    : ReadArchiveEntries(canonicalArchive);
                ArchiveBuildResult built = CreateArchive(archiveRoot, manifestPath, canonicalEntries);
                byte[] archive = built.Archive;

                bool reusedCanonicalArchive = canonicalArchive is not null && ArchiveEntriesMatch(archive, canonicalArchive);
                if (reusedCanonicalArchive)
                    archive = canonicalArchive!;

                JsonObject archiveManifest = JsonNode.Parse(built.Manifest) as JsonObject
                    ?? throw new InvalidDataException($"Generated fixture manifest '{manifestPath}' is not a JSON object.");
                archiveManifest["ArchiveSha256"] = Hash(archive);
                byte[] finalManifest = Encoding.UTF8.GetBytes(archiveManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                if (reusedCanonicalArchive && output.BaselineManifestSha256 is not null)
                    finalManifest = LoadCanonicalManifest(repositoryRoot, output, finalManifest);

                generatedFiles.Add(output.ArchiveFile, Encoding.ASCII.GetBytes(EncodeBase64Lines(archive)));
                generatedFiles.Add(output.ManifestFile, finalManifest);
            }

            PromoteOutputDirectory(outputDirectory, generatedFiles);
            Directory.Delete(generationRoot, recursive: true);
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: true, CancellationToken.None).ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            artifacts.Add("generated-files.json");
            await WriteGeneratedFilesReceiptAsync(runDirectory, outputDirectory, generatedFiles, CancellationToken.None).ConfigureAwait(false);
            RunManifest completed = await runStore.FinishAsync(manifest, RunState.Succeeded, artifacts: artifacts,
                cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
            return new HistoricalFixtureGenerationResult(new CommandResult(OutcomeKind.Success,
                $"Historical {scenario.Id} fixtures for LeanCorpus {version.Version} were generated in '{outputDirectory}'.",
                Details: Path.Combine(runDirectory, "run.json")), completed.RunId, runDirectory);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                "Generation was cancelled; temporary project and package files were retained.").ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            return await FinishEarlyAsync(start, runDirectory, OutcomeKind.Cancelled,
                "Historical fixture generation was cancelled.", exception.Message, artifacts, childExitCodes, generationRoot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or FormatException or System.ComponentModel.Win32Exception)
        {
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                "Generation did not complete; temporary project and package files were retained.").ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            return await FinishEarlyAsync(start, runDirectory, OutcomeKind.InfrastructureFailure,
                "Could not generate or publish historical fixture archives.", exception.Message, artifacts, childExitCodes, generationRoot).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> BuildRestoreArguments(string projectPath, string packageRoot, string nugetConfigPath)
        => ["restore", projectPath, "--locked-mode", "--packages", packageRoot, "--configfile", nugetConfigPath,
            "--disable-build-servers", "-p:NuGetAudit=false"];

    internal static IReadOnlyList<string> BuildArguments(string projectPath)
        => ["build", projectPath, "--configuration", "Release", "--no-restore", "--disable-build-servers"];

    internal static XDocument BuildNuGetConfigDocument(string packageSource)
        => new(new XElement("configuration",
            new XElement("packageSources",
                new XElement("clear"),
                new XElement("add", new XAttribute("key", "pinned-release"), new XAttribute("value", packageSource)))));

    internal static ArchiveBuildResult CreateArchive(
        string sourceRoot,
        string manifestPath,
        IReadOnlyDictionary<string, ArchiveEntryData>? canonicalEntries = null)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath)) as JsonObject
            ?? throw new InvalidDataException($"Fixture manifest '{manifestPath}' is not a JSON object.");
        if (manifest["Files"] is not JsonArray files || files.Count == 0)
            throw new InvalidDataException($"Fixture manifest '{manifestPath}' does not contain a non-empty Files array.");

        string fullRoot = Path.GetFullPath(sourceRoot);
        string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var entries = new Dictionary<string, ArchiveEntryData>(StringComparer.Ordinal);
        foreach (JsonNode? fileNode in files)
        {
            if (fileNode is not JsonObject file)
                throw new InvalidDataException($"Fixture manifest '{manifestPath}' contains a non-object file entry.");
            string name = file["Name"]?.GetValue<string>() ?? string.Empty;
            if (!IsSafeArchivePath(name) || entries.ContainsKey(name))
                throw new InvalidDataException($"Fixture manifest '{manifestPath}' contains an unsafe or duplicated file name.");
            string filePath = Path.GetFullPath(Path.Combine(fullRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!filePath.StartsWith(rootPrefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException($"Fixture manifest '{manifestPath}' escapes the archive root.");
            byte[] bytes = File.ReadAllBytes(filePath);
            long expectedLength = file["Length"]?.GetValue<long>() ?? -1;
            string expectedHash = file["Sha256"]?.GetValue<string>() ?? string.Empty;
            if (bytes.LongLength != expectedLength || !string.Equals(Hash(bytes), expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Fixture file '{name}' does not match its manifest length and SHA-256.");

            if (canonicalEntries is not null && canonicalEntries.TryGetValue(name, out ArchiveEntryData? canonical) &&
                name.EndsWith(".seg", StringComparison.Ordinal) && JsonContentsEqual(bytes, canonical.Data))
                bytes = canonical.Data;
            file["Length"] = bytes.LongLength;
            file["Sha256"] = Hash(bytes);
            entries.Add(name, new ArchiveEntryData(name, TarEntryType.RegularFile, bytes));
        }

        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in entries.Keys)
        {
            int slash = name.LastIndexOf('/');
            while (slash >= 0)
            {
                directories.Add(name[..slash]);
                slash = name.LastIndexOf('/', slash - 1);
            }
        }

        using var tarStream = new MemoryStream();
        using (var writer = new TarWriter(tarStream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            var allEntries = directories.Select(name => new ArchiveEntryData(name, TarEntryType.Directory, []))
                .Concat(entries.Values)
                .OrderBy(static entry => entry.Name, StringComparer.Ordinal);
            foreach (ArchiveEntryData archiveEntry in allEntries)
            {
                if (archiveEntry.Type == TarEntryType.Directory)
                {
                    writer.WriteEntry(new UstarTarEntry(TarEntryType.Directory, archiveEntry.Name)
                    {
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
                        Uid = 0,
                        Gid = 0,
                        UserName = string.Empty,
                        GroupName = string.Empty,
                        ModificationTime = DateTimeOffset.UnixEpoch
                    });
                    continue;
                }

                var entry = new UstarTarEntry(TarEntryType.RegularFile, archiveEntry.Name)
                {
                    DataStream = new MemoryStream(archiveEntry.Data, writable: false),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    Uid = 0,
                    Gid = 0,
                    UserName = string.Empty,
                    GroupName = string.Empty,
                    ModificationTime = DateTimeOffset.UnixEpoch
                };
                writer.WriteEntry(entry);
                entry.DataStream?.Dispose();
            }
        }

        const int tarRecordSize = 10_240;
        int tarLength = checked((int)tarStream.Length);
        int recordPadding = (tarRecordSize - tarLength % tarRecordSize) % tarRecordSize;
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(tarStream.GetBuffer(), 0, tarLength);
            if (recordPadding != 0)
                gzip.Write(new byte[recordPadding]);
        }
        byte[] archive = compressed.ToArray();
        archive[9] = byte.MaxValue;
        byte[] serializedManifest = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return new ArchiveBuildResult(archive, serializedManifest);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static byte[] PreferCanonicalEncoding(byte[] generatedArchive, byte[]? canonicalArchive)
    {
        ArgumentNullException.ThrowIfNull(generatedArchive);
        if (canonicalArchive is null || !ArchivePayloadsMatch(generatedArchive, canonicalArchive))
            return generatedArchive;
        return canonicalArchive;
    }

    internal static IReadOnlyDictionary<string, ArchiveEntryData> ReadArchiveEntries(byte[] archive)
    {
        var entries = new Dictionary<string, ArchiveEntryData>(StringComparer.Ordinal);
        using var archiveStream = new MemoryStream(archive, writable: false);
        using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            string name = entry.EntryType == TarEntryType.Directory ? entry.Name.TrimEnd('/') : entry.Name;
            if (!IsSafeArchivePath(name) || entries.ContainsKey(name))
                throw new InvalidDataException("Historical fixture archive contains an unsafe or duplicated entry.");
            byte[] bytes = [];
            if (entry.EntryType == TarEntryType.RegularFile)
            {
                using var content = new MemoryStream();
                entry.DataStream?.CopyTo(content);
                bytes = content.ToArray();
            }
            else if (entry.EntryType != TarEntryType.Directory)
            {
                throw new InvalidDataException($"Historical fixture archive entry '{entry.Name}' is not a file or directory.");
            }
            entries.Add(name, new ArchiveEntryData(name, entry.EntryType, bytes));
        }
        return entries;
    }

    internal static bool ArchiveEntriesMatch(byte[] first, byte[] second)
    {
        IReadOnlyDictionary<string, ArchiveEntryData> left = ReadArchiveEntries(first);
        IReadOnlyDictionary<string, ArchiveEntryData> right = ReadArchiveEntries(second);
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out ArchiveEntryData? other) &&
            pair.Value.Type == other.Type && pair.Value.Data.AsSpan().SequenceEqual(other.Data));
    }

    private async Task<ProcessResult> RunChildAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string runDirectory,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        progress?.Invoke(description);
        return await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            StandardOutputLogPath = Path.Combine(runDirectory, name + ".stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, name + ".stderr.log"),
            Timeout = TimeSpan.FromHours(2),
            ProgressInterval = TimeSpan.FromSeconds(30),
            Progress = value => progress?.Invoke(
                $"{description} Still running ({value.Elapsed:hh\\:mm\\:ss}; {value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines).")
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void AddChild(ProcessResult child, ICollection<int> exits, ICollection<string> artifacts, string name)
    {
        if (child.RawExitCode is int exitCode)
            exits.Add(exitCode);
        artifacts.Add(name + ".stdout.log");
        artifacts.Add(name + ".stderr.log");
    }

    internal static string? ValidatePackageSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return $"Package source must be an HTTPS URI without embedded credentials or an existing local directory (default: {DefaultPackageSource}).";
        if (Directory.Exists(source))
            return null;
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsWellFormedOriginalString())
        {
            if (uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                return null;
            if (uri.IsFile && Directory.Exists(uri.LocalPath))
                return null;
        }
        return "Package source must be an HTTPS URI without embedded credentials or an existing local directory.";
    }

    private static string SafePackageSource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) ? uri.GetLeftPart(UriPartial.Path) : "local-directory";

    private static string SafeChildPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\') ||
            relativePath.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException($"Historical fixture child path '{relativePath}' is unsafe.");
        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Historical fixture child path '{relativePath}' escapes its root.");
        return path;
    }

    private static bool IsSafeArchivePath(string name)
        => !string.IsNullOrWhiteSpace(name) && name[0] != '/' &&
           !name.Contains('\\') && name.Split('/').All(static part => part.Length > 0 && part is not "." and not "..");

    private static async Task WriteNuGetConfigAsync(string path, string packageSource, CancellationToken cancellationToken)
    {
        XDocument document = BuildNuGetConfigDocument(packageSource);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static byte[]? LoadCanonicalArchive(string repositoryRoot, HistoricalFixtureOutput output)
    {
        string path = HistoricalFixtureCatalog.ResolveCatalogPath(repositoryRoot, output.BaselinePath);
        byte[] archive = Convert.FromBase64String(File.ReadAllText(path));
        if (!string.Equals(Hash(archive), output.BaselineSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Canonical fixture archive '{path}' does not match its catalogued hash.");
        _ = ReadArchiveEntries(archive);
        return archive;
    }

    private static byte[] LoadCanonicalManifest(string repositoryRoot, HistoricalFixtureOutput output, byte[] generatedManifest)
    {
        if (output.BaselineManifestSha256 is null)
            return generatedManifest;
        string archivePath = HistoricalFixtureCatalog.ResolveCatalogPath(repositoryRoot, output.BaselinePath);
        string manifestPath = Path.Combine(Path.GetDirectoryName(archivePath)!, output.ManifestFile);
        byte[] canonicalManifest = File.ReadAllBytes(manifestPath);
        if (!string.Equals(Hash(canonicalManifest), output.BaselineManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"Canonical fixture manifest '{manifestPath}' does not match its catalogued hash.");

        JsonObject generated = JsonNode.Parse(generatedManifest) as JsonObject
            ?? throw new InvalidDataException($"Generated fixture manifest '{output.ManifestFile}' is invalid JSON.");
        JsonObject canonical = JsonNode.Parse(canonicalManifest) as JsonObject
            ?? throw new InvalidDataException($"Canonical fixture manifest '{manifestPath}' is invalid JSON.");
        generated.Remove("Runtime");
        canonical.Remove("Runtime");
        if (!JsonNodesEqual(generated, canonical))
            throw new InvalidDataException($"Generated fixture manifest '{output.ManifestFile}' differs from its pinned canonical manifest.");
        return canonicalManifest;
    }

    private static bool JsonNodesEqual(JsonNode? first, JsonNode? second)
    {
        if (first is JsonObject firstObject && second is JsonObject secondObject)
            return firstObject.Count == secondObject.Count && firstObject.All(pair => secondObject.TryGetPropertyValue(pair.Key, out JsonNode? value) && JsonNodesEqual(pair.Value, value));
        if (first is JsonArray firstArray && second is JsonArray secondArray)
            return firstArray.Count == secondArray.Count && firstArray.Zip(secondArray).All(pair => JsonNodesEqual(pair.First, pair.Second));
        return JsonNode.DeepEquals(first, second);
    }

    private static bool JsonContentsEqual(byte[] first, byte[] second)
    {
        using JsonDocument left = JsonDocument.Parse(StripUtf8Bom(first));
        using JsonDocument right = JsonDocument.Parse(StripUtf8Bom(second));
        return JsonElementsEqual(left.RootElement, right.RootElement);
    }

    private static ReadOnlyMemory<byte> StripUtf8Bom(byte[] bytes)
        => bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()) ? bytes.AsMemory(Encoding.UTF8.GetPreamble().Length) : bytes;

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                JsonProperty[] leftProperties = left.EnumerateObject().ToArray();
                JsonProperty[] rightProperties = right.EnumerateObject().ToArray();
                return leftProperties.Length == rightProperties.Length && leftProperties.All(property =>
                    right.TryGetProperty(property.Name, out JsonElement value) && JsonElementsEqual(property.Value, value));
            case JsonValueKind.Array:
                JsonElement[] leftValues = left.EnumerateArray().ToArray();
                JsonElement[] rightValues = right.EnumerateArray().ToArray();
                return leftValues.Length == rightValues.Length && leftValues.Zip(rightValues).All(pair => JsonElementsEqual(pair.First, pair.Second));
            case JsonValueKind.String:
                return left.GetString() == right.GetString();
            case JsonValueKind.Number:
                return left.GetRawText() == right.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;
            default:
                return false;
        }
    }

    private static bool ArchivePayloadsMatch(byte[] first, byte[] second)
    {
        using var firstStream = new MemoryStream(first, writable: false);
        using var firstGzip = new GZipStream(firstStream, CompressionMode.Decompress);
        using var firstPayload = new MemoryStream();
        firstGzip.CopyTo(firstPayload);
        using var secondStream = new MemoryStream(second, writable: false);
        using var secondGzip = new GZipStream(secondStream, CompressionMode.Decompress);
        using var secondPayload = new MemoryStream();
        secondGzip.CopyTo(secondPayload);
        return firstPayload.ToArray().AsSpan().SequenceEqual(secondPayload.ToArray());
    }

    private static string EncodeBase64Lines(byte[] bytes)
    {
        string text = Convert.ToBase64String(bytes);
        var result = new StringBuilder(text.Length + text.Length / 76 + 1);
        for (int offset = 0; offset < text.Length; offset += 76)
            result.Append(text, offset, Math.Min(76, text.Length - offset)).Append('\n');
        return result.ToString();
    }

    private static void PromoteOutputDirectory(string outputDirectory, IReadOnlyDictionary<string, byte[]> files)
    {
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException($"Historical fixture output directory '{outputDirectory}' already exists.");
        string parent = Path.GetDirectoryName(outputDirectory)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".historical-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach ((string fileName, byte[] bytes) in files)
            {
                if (!IsSafeArchivePath(fileName) || fileName.Contains('/'))
                    throw new InvalidDataException($"Historical fixture output name '{fileName}' is unsafe.");
                File.WriteAllBytes(Path.Combine(staging, fileName), bytes);
            }
            Directory.Move(staging, outputDirectory);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task WriteGeneratedFilesReceiptAsync(
        string runDirectory,
        string outputDirectory,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken)
    {
        var receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            outputDirectory,
            files = files.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(pair => new
            {
                name = pair.Key,
                length = pair.Value.LongLength,
                sha256 = Hash(pair.Value)
            })
        }, new JsonSerializerOptions { WriteIndented = true });
        string path = Path.Combine(runDirectory, "generated-files.json");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(receipt, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteCleanupReceiptAsync(
        string runDirectory,
        string generationRoot,
        bool removed,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        var receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            path = generationRoot,
            removed,
            detail,
            recordedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true });
        string path = Path.Combine(runDirectory, "cleanup.json");
        string temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                         4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(receipt, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private async Task<HistoricalFixtureGenerationResult> FinishProcessFailureAsync(
        RunManifest manifest,
        string runDirectory,
        ProcessResult child,
        IReadOnlyList<int> childExitCodes,
        IEnumerable<string> artifacts,
        string message,
        string generationRoot)
    {
        await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
            "The failed generator state was retained for diagnosis.").ConfigureAwait(false);
        var terminalState = child.Outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        RunManifest finished = await runStore.FinishAsync(manifest, terminalState, child.RawExitCode,
            artifacts.Append("cleanup.json"), child.Failure, CancellationToken.None, childExitCodes).ConfigureAwait(false);
        return new HistoricalFixtureGenerationResult(new CommandResult(child.Outcome, message, child.RawExitCode,
            Details: JoinDetails(child.Failure, string.Join(Environment.NewLine, child.StandardErrorTail), runDirectory, generationRoot)),
            finished.RunId, runDirectory);
    }

    private async Task<HistoricalFixtureGenerationResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details,
        IEnumerable<string>? artifacts = null,
        IEnumerable<int>? childExitCodes = null,
        string? generationRoot = null)
    {
        if (generationRoot is not null && !(artifacts?.Contains("cleanup.json", StringComparer.Ordinal) ?? false))
        {
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                "Generation did not complete; temporary files were retained.").ConfigureAwait(false);
            artifacts = (artifacts ?? []).Append("cleanup.json");
        }
        RunState terminalState = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            OutcomeKind.ValidationFailure => RunState.Failed,
            _ => RunState.Failed
        };
        try
        {
            RunManifest finished = await runStore.FinishAsync(manifest, terminalState,
                artifacts: artifacts, failure: details, cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
            return new HistoricalFixtureGenerationResult(new CommandResult(outcome, message, Details: JoinDetails(runDirectory, details, generationRoot)),
                finished.RunId, runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new HistoricalFixtureGenerationResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Historical fixture generation failed and its run evidence could not be finalised.", Details: JoinDetails(details, exception.Message)),
                manifest.RunId, runDirectory);
        }
    }

    private static string? JoinDetails(params string?[] values)
    {
        string[] present = values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }
}

internal sealed record ArchiveBuildResult(byte[] Archive, byte[] Manifest);

internal sealed record ArchiveEntryData(string Name, TarEntryType Type, byte[] Data);
