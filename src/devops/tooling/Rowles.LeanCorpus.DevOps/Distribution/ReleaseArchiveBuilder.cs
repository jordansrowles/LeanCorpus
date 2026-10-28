using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Distribution;

internal sealed record ReleaseArchiveRequest
{
    public required string RepositoryRoot { get; init; }

    public required string ProjectRoot { get; init; }

    public required string PublishDirectory { get; init; }

    public required string OutputDirectory { get; init; }

    public required string ProductVersion { get; init; }

    public required string SourceCommit { get; init; }

    public required string SourceBranch { get; init; }

    public required bool SourceDirty { get; init; }

    public required string RuntimeIdentifier { get; init; }

    public required bool SelfContained { get; init; }

    public required Guid PublishRunId { get; init; }
}

internal sealed record ReleaseArchiveResult(string ArchivePath, string ChecksumPath, string ArchiveSha256, string BundleDirectory);

internal sealed class ReleaseArchiveBuilder
{
    private static readonly HashSet<string> SupportedRuntimeIdentifiers = new(StringComparer.Ordinal)
    {
        "win-x64", "linux-x64", "osx-arm64", "linux-arm64"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ReleaseArchiveResult> BuildAsync(ReleaseArchiveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var windowsTarget = request.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal);
        var executableName = windowsTarget ? "devops.exe" : "devops";
        var publishFiles = Directory.EnumerateFiles(request.PublishDirectory, "*", SearchOption.AllDirectories).ToArray();
        if (publishFiles.Length != 1 || !string.Equals(Path.GetFileName(publishFiles[0]), executableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Single-file publish output for {request.RuntimeIdentifier} must contain only '{executableName}'. Found [{string.Join(", ", publishFiles.Select(Path.GetFileName))}].");
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var modeLabel = request.SelfContained ? "self-contained" : "framework-dependent";
        var stem = $"devops-{request.ProductVersion}-{request.RuntimeIdentifier}-{modeLabel}";
        var bundleDirectory = Path.Combine(request.OutputDirectory, stem);
        var archivePath = Path.Combine(request.OutputDirectory, stem + ".zip");
        var checksumPath = archivePath + ".sha256";
        if (Directory.Exists(bundleDirectory) || File.Exists(archivePath) || File.Exists(checksumPath))
        {
            throw new IOException($"Release output already exists for {stem}; use a new distribution run.");
        }

        Directory.CreateDirectory(bundleDirectory);
        var executablePath = Path.Combine(bundleDirectory, executableName);
        File.Copy(publishFiles[0], executablePath);
        if (!windowsTarget && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.Copy(Path.Combine(request.RepositoryRoot, "LICENCE"), Path.Combine(bundleDirectory, "LICENCE"));
        File.Copy(Path.Combine(request.ProjectRoot, "RELEASE-ARCHIVE-README.md"), Path.Combine(bundleDirectory, "README.md"));
        File.Copy(Path.Combine(request.ProjectRoot, "THIRD-PARTY-NOTICES.md"), Path.Combine(bundleDirectory, "THIRD-PARTY-NOTICES.md"));
        CopyLicenseDirectory(Path.Combine(request.ProjectRoot, "Licenses"), Path.Combine(bundleDirectory, "licenses"));

        var executableSha256 = await ComputeSha256Async(executablePath, cancellationToken).ConfigureAwait(false);
        var manifest = new ReleaseDistributionManifest
        {
            SchemaVersion = 1,
            Product = "LeanCorpus DevOps",
            ProductVersion = request.ProductVersion,
            SourceCommit = request.SourceCommit,
            SourceBranch = request.SourceBranch,
            SourceDirty = request.SourceDirty,
            RuntimeIdentifier = request.RuntimeIdentifier,
            TargetFramework = "net10.0",
            SingleFile = true,
            SelfContained = request.SelfContained,
            Executable = executableName,
            ExecutableSha256 = executableSha256,
            PublishRunId = request.PublishRunId,
            Prerequisites = request.SelfContained
                ? ["Git for repository operations.", "The .NET SDK selected by the checkout's global.json for build and project evaluation commands."]
                : ["The .NET 10 runtime to start the tool.", "Git for repository operations.", "The .NET SDK selected by the checkout's global.json for build and project evaluation commands."]
        };
        var manifestPath = Path.Combine(bundleDirectory, "distribution-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, cancellationToken).ConfigureAwait(false);

        var bundleFiles = Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(bundleDirectory, path), StringComparer.Ordinal)
            .ToArray();
        var checksums = new StringBuilder();
        foreach (var path in bundleFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(bundleDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            checksums.Append(await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false))
                .Append("  ").Append(relativePath).AppendLine();
        }
        var shaPath = Path.Combine(bundleDirectory, "SHA256SUMS");
        await File.WriteAllTextAsync(shaPath, checksums.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);

        await CreateZipAsync(bundleDirectory, archivePath, windowsTarget, cancellationToken).ConfigureAwait(false);
        var archiveSha256 = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(checksumPath, $"{archiveSha256}  {Path.GetFileName(archivePath)}{Environment.NewLine}", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return new ReleaseArchiveResult(archivePath, checksumPath, archiveSha256, bundleDirectory);
    }

    private static void Validate(ReleaseArchiveRequest request)
    {
        foreach (var path in new[] { request.RepositoryRoot, request.ProjectRoot, request.PublishDirectory })
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"Release archive input directory does not exist: {path}");
            }
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new ArgumentException("Release archive output directory is required.", nameof(request));
        }

        if (!SupportedRuntimeIdentifiers.Contains(request.RuntimeIdentifier))
        {
            throw new InvalidDataException($"Release archives are not qualified for runtime identifier '{request.RuntimeIdentifier}'. Supported targets: {string.Join(", ", SupportedRuntimeIdentifiers.Order(StringComparer.Ordinal))}.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.ProductVersion, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.-]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout))
        {
            throw new InvalidDataException($"Release product version '{request.ProductVersion}' is invalid.");
        }

        if (request.PublishRunId == Guid.Empty || !System.Text.RegularExpressions.Regex.IsMatch(request.SourceCommit, "^[0-9a-fA-F]{7,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout))
        {
            throw new InvalidDataException("Release archive source and publish run identities are required.");
        }

        foreach (var requiredFile in new[]
        {
            Path.Combine(request.RepositoryRoot, "LICENCE"),
            Path.Combine(request.ProjectRoot, "RELEASE-ARCHIVE-README.md"),
            Path.Combine(request.ProjectRoot, "THIRD-PARTY-NOTICES.md")
        })
        {
            if (!File.Exists(requiredFile))
            {
                throw new FileNotFoundException("Release archive documentation or licence input is missing.", requiredFile);
            }
        }
    }

    private static void CopyLicenseDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Third-party licence directory does not exist: {sourceDirectory}");
        }

        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0)
        {
            throw new InvalidDataException("Release archives require the bundled third-party licence texts.");
        }

        foreach (var source in files)
        {
            var relative = Path.GetRelativePath(sourceDirectory, source);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
    }

    private static async Task CreateZipAsync(string bundleDirectory, string archivePath, bool windowsTarget, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 16_384, FileOptions.Asynchronous);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(bundleDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            if (!windowsTarget)
            {
                const int UnixRegularFile = 0x8000;
                // ZIP permissions describe the target payload, independent of host and checkout modes.
                var fileMode = relative == "devops" ? 0x1ED : 0x1A4; // 0755 executable, 0644 data
                entry.ExternalAttributes = unchecked(((UnixRegularFile | fileMode) << 16) | 0x20);
            }

            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await input.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private sealed record ReleaseDistributionManifest
    {
        public required int SchemaVersion { get; init; }
        public required string Product { get; init; }
        public required string ProductVersion { get; init; }
        public required string SourceCommit { get; init; }
        public required string SourceBranch { get; init; }
        public required bool SourceDirty { get; init; }
        public required string RuntimeIdentifier { get; init; }
        public required string TargetFramework { get; init; }
        public required bool SingleFile { get; init; }
        public required bool SelfContained { get; init; }
        public required string Executable { get; init; }
        public required string ExecutableSha256 { get; init; }
        public required Guid PublishRunId { get; init; }
        public required IReadOnlyList<string> Prerequisites { get; init; }
    }
}
