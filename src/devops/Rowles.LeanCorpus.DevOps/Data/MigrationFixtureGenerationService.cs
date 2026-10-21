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

internal sealed record MigrationFixtureGenerationRequest(
    CheckoutIdentity Checkout,
    string PackageSource,
    string? OutputDirectory);

internal sealed record MigrationFixtureGenerationResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal sealed class MigrationFixtureGenerationService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    string dotnetExecutable = "dotnet")
{
    private const string DefaultPackageSource = "https://api.nuget.org/v3/index.json";
    private const string FixtureTemplateDirectory = "scripts/devops/data/fixtures-311";
    private const string FixtureRoot = "src/devops/Rowles.LeanCorpus.Tests.Shared/Fixtures/Indexes";

    public async Task<MigrationFixtureGenerationResult> GenerateAsync(
        MigrationFixtureGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.PackageSource;
        var sourceError = ValidatePackageSource(source);
        if (sourceError is not null)
            return new MigrationFixtureGenerationResult(new CommandResult(OutcomeKind.ValidationFailure, sourceError), null, null);

        var repositoryRoot = request.Checkout.RepositoryRoot;
        var outputDirectory = Path.GetFullPath(request.OutputDirectory is null
            ? Path.Combine(repositoryRoot, FixtureRoot)
            : Path.IsPathRooted(request.OutputDirectory) ? request.OutputDirectory : Path.Combine(repositoryRoot, request.OutputDirectory));
        var manifest = await runStore.StartAsync("data migration-fixtures-311",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packageSource"] = SafePackageSource(source),
                ["outputDirectory"] = outputDirectory,
                ["fixtureVersion"] = "3.1.1"
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("data", manifest.RunId);
        var generationRoot = Path.Combine(artifactPaths.ArtifactRoot, "temp", "migration-fixtures-311", Guid.NewGuid().ToString("N"));
        var generatorRoot = Path.Combine(generationRoot, "generator");
        var packageRoot = Path.Combine(generationRoot, "packages");
        var outputRoot = Path.Combine(generationRoot, "output");
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();
        string? restoreFailure = null;

        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record migration fixture source provenance.", git.Failure).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(generatorRoot);
            Directory.CreateDirectory(outputRoot);
            CopyGeneratorInputs(repositoryRoot, generatorRoot);
            await WriteNuGetConfigAsync(Path.Combine(generatorRoot, "NuGet.Config"), source, cancellationToken).ConfigureAwait(false);

            var projectPath = Path.Combine(generatorRoot, "Generator.csproj");
            var packagePath = Path.Combine(packageRoot, "leancorpus", "3.1.1", "leancorpus.3.1.1.nupkg");
            var restore = await RunChildAsync(
                ["restore", projectPath, "--packages", packageRoot, "--configfile", Path.Combine(generatorRoot, "NuGet.Config"),
                    "--disable-build-servers", "-p:NuGetAudit=false"],
                generatorRoot, runDirectory, "restore", "Restoring the pinned LeanCorpus 3.1.1 package.", cancellationToken).ConfigureAwait(false);
            if (restore.RawExitCode is int restoreExitCode)
                childExitCodes.Add(restoreExitCode);
            artifacts.AddRange(["restore.stdout.log", "restore.stderr.log"]);
            if (restore.Outcome != OutcomeKind.Success)
            {
                restoreFailure = restore.Failure;
                return await FinishProcessFailureAsync(manifest, runDirectory, restore, childExitCodes, artifacts,
                    "Could not restore the pinned LeanCorpus 3.1.1 fixture generator.", generationRoot, preserveGenerationRoot: true).ConfigureAwait(false);
            }
            if (!File.Exists(packagePath))
            {
                await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                    "The restore completed without its expected package archive; temporary files were retained.").ConfigureAwait(false);
                artifacts.Add("cleanup.json");
                return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                    "The isolated restore completed without the pinned LeanCorpus package archive.", packagePath,
                    artifacts, childExitCodes).ConfigureAwait(false);
            }

            var generator = await RunChildAsync(
                ["run", "--project", projectPath, "--configuration", "Release", "--no-restore", "--", outputRoot, packagePath],
                generatorRoot, runDirectory, "generator", "Generating the frozen 3.1.1 physical index fixtures.", cancellationToken).ConfigureAwait(false);
            if (generator.RawExitCode is int generatorExitCode)
                childExitCodes.Add(generatorExitCode);
            artifacts.AddRange(["generator.stdout.log", "generator.stderr.log"]);
            if (generator.Outcome != OutcomeKind.Success)
            {
                return await FinishProcessFailureAsync(manifest, runDirectory, generator, childExitCodes, artifacts,
                    "The pinned 3.1.1 fixture generator failed.", generationRoot, preserveGenerationRoot: true).ConfigureAwait(false);
            }

            var outputFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var layout in new[] { "loose", "compound" })
            {
                var stem = $"3.1.1-release-{layout}";
                var manifestPath = Path.Combine(outputRoot, stem + ".manifest.json");
                var layoutDirectory = Path.Combine(outputRoot, layout);
                var archive = CreateArchive(layoutDirectory, manifestPath);
                archive = PreserveCanonicalEncoding(repositoryRoot, stem, archive);
                var manifestNode = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false)) as JsonObject
                    ?? throw new InvalidDataException($"Generated fixture manifest '{manifestPath}' is not a JSON object.");
                manifestNode["ArchiveSha256"] = Hash(archive);
                var json = manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
                outputFiles[stem + ".fixture.b64"] = Encoding.ASCII.GetBytes(EncodeBase64Lines(archive));
                outputFiles[stem + ".manifest.json"] = Encoding.UTF8.GetBytes(json);
            }

            PromoteOutputFiles(outputDirectory, outputFiles);
            Directory.Delete(generationRoot, recursive: true);
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: true, cancellationToken).ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            var completed = await runStore.FinishAsync(manifest, RunState.Succeeded, artifacts: artifacts,
                cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
            return new MigrationFixtureGenerationResult(new CommandResult(OutcomeKind.Success,
                $"Frozen 3.1.1 migration fixtures generated in '{outputDirectory}'.",
                Details: Path.Combine(runDirectory, "manifest.json")), completed.RunId, runDirectory);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                "Generation was cancelled; temporary generator files were retained.").ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled,
                "Migration fixture generation was cancelled.", exception.Message, artifacts, childExitCodes).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or System.ComponentModel.Win32Exception)
        {
            await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: false, CancellationToken.None,
                "Generation did not complete; temporary generator files were retained.").ConfigureAwait(false);
            artifacts.Add("cleanup.json");
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                "Could not generate or promote the frozen 3.1.1 migration fixtures.",
                string.IsNullOrWhiteSpace(restoreFailure) ? exception.Message : $"{exception.Message}{Environment.NewLine}{restoreFailure}", artifacts, childExitCodes).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> BuildRestoreArguments(string projectPath, string packageRoot, string nugetConfigPath)
        => ["restore", projectPath, "--packages", packageRoot, "--configfile", nugetConfigPath, "--disable-build-servers", "-p:NuGetAudit=false"];

    internal static byte[] CreateArchive(string layoutDirectory, string manifestPath)
    {
        using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifestDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Fixture manifest '{manifestPath}' does not contain a Files array.");

        using var tarStream = new MemoryStream();
        using (var writer = new TarWriter(tarStream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var file in files.EnumerateArray())
            {
                var name = file.GetProperty("Name").GetString();
                if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal))
                    throw new InvalidDataException($"Fixture manifest '{manifestPath}' contains an unsafe file name.");
                var path = Path.Combine(layoutDirectory, name);
                var bytes = File.ReadAllBytes(path);
                var expectedLength = file.GetProperty("Length").GetInt64();
                var expectedHash = file.GetProperty("Sha256").GetString();
                var actualHash = Hash(bytes);
                if (bytes.LongLength != expectedLength || !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Fixture file '{name}' does not match its manifest length and SHA-256.");

                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(bytes, writable: false),
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
        var tarLength = checked((int)tarStream.Length);
        var recordPadding = (tarRecordSize - tarLength % tarRecordSize) % tarRecordSize;
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(tarStream.GetBuffer(), 0, tarLength);
            if (recordPadding != 0)
                gzip.Write(new byte[recordPadding]);
        }
        var archive = compressed.ToArray();
        archive[9] = byte.MaxValue;
        return archive;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static byte[] PreferCanonicalEncoding(byte[] generatedArchive, byte[]? canonicalArchive)
    {
        ArgumentNullException.ThrowIfNull(generatedArchive);
        if (canonicalArchive is null || !ArchivePayloadsMatch(generatedArchive, canonicalArchive))
            return generatedArchive;
        return canonicalArchive;
    }

    private static byte[] PreserveCanonicalEncoding(string repositoryRoot, string stem, byte[] generatedArchive)
    {
        var fixtureRoot = Path.Combine(repositoryRoot, FixtureRoot);
        var archivePath = Path.Combine(fixtureRoot, stem + ".fixture.b64");
        var manifestPath = Path.Combine(fixtureRoot, stem + ".manifest.json");
        if (!File.Exists(archivePath) || !File.Exists(manifestPath))
            return generatedArchive;

        var canonicalArchive = Convert.FromBase64String(File.ReadAllText(archivePath));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        if (!manifest.RootElement.TryGetProperty("ArchiveSha256", out var archiveHash) || archiveHash.ValueKind != JsonValueKind.String ||
            !string.Equals(Hash(canonicalArchive), archiveHash.GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Canonical migration fixture archive '{archivePath}' does not match its manifest hash.");

        return PreferCanonicalEncoding(generatedArchive, canonicalArchive);
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

    private static string? ValidatePackageSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return $"Package source must be an HTTPS URI without embedded credentials or an existing local directory (default: {DefaultPackageSource}).";
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString())
        {
            if (uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                return null;
            if (uri.IsFile && Directory.Exists(uri.LocalPath))
                return null;
        }
        if (!Path.IsPathRooted(source) && Directory.Exists(source))
            return null;
        return "Package source must be an HTTPS URI without embedded credentials or an existing local directory.";
    }

    private static string SafePackageSource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path) : "local-directory";

    private static void CopyGeneratorInputs(string repositoryRoot, string generatorRoot)
    {
        var source = Path.Combine(repositoryRoot, FixtureTemplateDirectory);
        foreach (var fileName in new[] { "Generator.csproj.template", "Program.cs.template", "global.json.template" })
        {
            var sourcePath = Path.Combine(source, fileName);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Migration fixture generator template '{sourcePath}' was not found.", sourcePath);
            File.Copy(sourcePath, Path.Combine(generatorRoot, Path.GetFileNameWithoutExtension(fileName)), overwrite: false);
        }
    }

    private static async Task WriteNuGetConfigAsync(string path, string packageSource, CancellationToken cancellationToken)
    {
        var document = new XDocument(
            new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "pinned-release"), new XAttribute("value", packageSource)))));
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string EncodeBase64Lines(byte[] bytes)
    {
        var text = Convert.ToBase64String(bytes);
        var result = new StringBuilder(text.Length + text.Length / 76 + 1);
        for (var offset = 0; offset < text.Length; offset += 76)
        {
            result.Append(text, offset, Math.Min(76, text.Length - offset));
            result.Append('\n');
        }
        return result.ToString();
    }

    private static void PromoteOutputFiles(string outputDirectory, IReadOnlyDictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(outputDirectory);
        var staging = Path.Combine(outputDirectory, ".migration-fixtures-311-" + Guid.NewGuid().ToString("N"));
        var stagedFiles = Path.Combine(staging, "staged");
        var backups = Path.Combine(staging, "backups");
        Directory.CreateDirectory(stagedFiles);
        Directory.CreateDirectory(backups);
        var destinations = new List<(string Destination, string? Backup)>();
        var promoted = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var (fileName, bytes) in files)
            {
                var stagedPath = Path.Combine(stagedFiles, fileName);
                File.WriteAllBytes(stagedPath, bytes);
                var destination = Path.Combine(outputDirectory, fileName);
                var backupPath = File.Exists(destination) ? Path.Combine(backups, fileName) : null;
                if (backupPath is not null)
                    File.Copy(destination, backupPath, overwrite: false);
                destinations.Add((destination, backupPath));
            }

            try
            {
                foreach (var (fileName, _) in files)
                {
                    var stagedPath = Path.Combine(stagedFiles, fileName);
                    var destination = Path.Combine(outputDirectory, fileName);
                    var item = destinations.Single(value => string.Equals(value.Destination, destination, StringComparison.Ordinal));
                    File.Move(stagedPath, destination, overwrite: true);
                    promoted.Add(item);
                }
            }
            catch
            {
                foreach (var item in promoted.AsEnumerable().Reverse())
                {
                    if (item.Backup is not null)
                        File.Copy(item.Backup, item.Destination, overwrite: true);
                    else
                        File.Delete(item.Destination);
                }
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
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
        var path = Path.Combine(runDirectory, "cleanup.json");
        var temp = path + ".tmp";
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

    private async Task<MigrationFixtureGenerationResult> FinishProcessFailureAsync(
        RunManifest manifest,
        string runDirectory,
        ProcessResult child,
        IReadOnlyList<int> childExitCodes,
        IEnumerable<string> artifacts,
        string message,
        string generationRoot,
        bool preserveGenerationRoot)
    {
        await WriteCleanupReceiptAsync(runDirectory, generationRoot, removed: !preserveGenerationRoot, CancellationToken.None,
            preserveGenerationRoot ? "The failed generator state was retained for diagnosis." : null).ConfigureAwait(false);
        var allArtifacts = artifacts.Append("cleanup.json");
        var terminalState = child.Outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        var finished = await runStore.FinishAsync(manifest, terminalState, child.RawExitCode, allArtifacts,
            child.Failure, CancellationToken.None, childExitCodes).ConfigureAwait(false);
        return new MigrationFixtureGenerationResult(new CommandResult(child.Outcome, message, child.RawExitCode,
            JoinDetails(child.Failure, string.Join(Environment.NewLine, child.StandardErrorTail), runDirectory,
                preserveGenerationRoot ? generationRoot : null)), finished.RunId, runDirectory);
    }

    private async Task<MigrationFixtureGenerationResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details,
        IEnumerable<string>? artifacts = null,
        IEnumerable<int>? childExitCodes = null)
    {
        var terminalState = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        try
        {
            var finished = await runStore.FinishAsync(manifest, terminalState,
                artifacts: artifacts, failure: details, cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
            return new MigrationFixtureGenerationResult(new CommandResult(outcome, message, Details: JoinDetails(runDirectory, details)),
                finished.RunId, runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new MigrationFixtureGenerationResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Migration fixture generation failed and its run evidence could not be finalised.", Details: JoinDetails(details, exception.Message)),
                manifest.RunId, runDirectory);
        }
    }

    private static string? JoinDetails(params string?[] values)
    {
        var present = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }
}
