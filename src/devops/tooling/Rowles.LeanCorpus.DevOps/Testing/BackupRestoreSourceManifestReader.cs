using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record BackupRestoreSourceFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record BackupRestoreSourceManifest
{
    [JsonPropertyName("commit")]
    public string Commit { get; init; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public IReadOnlyList<BackupRestoreSourceFile> Files { get; init; } = [];
}

internal sealed class BackupRestoreSourceManifestReader(IProcessRunner processRunner)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<BackupRestoreSourceManifest> ReadAsync(
        string repositoryRoot,
        string currentCommit,
        string? sourceManifestPath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentCommit);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(processRunner);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"Source repository does not exist: {root}");
        }

        BackupRestoreSourceManifest candidate;
        if (!string.IsNullOrWhiteSpace(sourceManifestPath))
        {
            candidate = await ReadProvidedManifestAsync(sourceManifestPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            candidate = await ReadGitInventoryAsync(root, currentCommit, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        return await ValidateAndFingerprintAsync(root, candidate, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BackupRestoreSourceManifest> ReadProvidedManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Source manifest does not exist: {fullPath}");
        }
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length is <= 0 or > 16 * 1024 * 1024)
        {
            throw new InvalidOperationException("Source manifest must be between 1 byte and 16 MiB.");
        }

        try
        {
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var manifest = await JsonSerializer.DeserializeAsync<BackupRestoreSourceManifest>(stream, JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return manifest ?? throw new InvalidOperationException("Source manifest cannot be JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Source manifest is not valid JSON.", exception);
        }
    }

    private async Task<BackupRestoreSourceManifest> ReadGitInventoryAsync(
        string repositoryRoot,
        string commit,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "leancorpus-backup-restore-inventory", Guid.NewGuid().ToString("N"));
        var stdoutPath = Path.Combine(logRoot, "git.stdout.log");
        var stderrPath = Path.Combine(logRoot, "git.stderr.log");
        try
        {
            var result = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = "git",
                Arguments = ["-c", "core.quotepath=false", "-C", repositoryRoot,
                    "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
                WorkingDirectory = workingDirectory,
                StandardOutputLogPath = stdoutPath,
                StandardErrorLogPath = stderrPath
            }, cancellationToken).ConfigureAwait(false);
            if (result.Outcome != OutcomeKind.Success)
            {
                throw new InvalidOperationException(result.Failure ??
                    "Source provenance requires Git or an explicit source manifest for an exported checkout.");
            }

            var inventoryBytes = await File.ReadAllBytesAsync(stdoutPath, cancellationToken).ConfigureAwait(false);
            var paths = ParseNulDelimitedPaths(inventoryBytes);
            if (paths.Count == 0)
            {
                throw new InvalidOperationException("Source provenance requires a non-empty Git file inventory.");
            }

            var files = new List<BackupRestoreSourceFile>(paths.Count);
            foreach (var path in paths)
            {
                var fullPath = ResolveSourcePath(repositoryRoot, path);
                if (!File.Exists(fullPath))
                {
                    continue;
                }
                files.Add(new BackupRestoreSourceFile
                {
                    Path = path,
                    Sha256 = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false)
                });
            }

            return new BackupRestoreSourceManifest { Commit = commit, Files = files };
        }
        finally
        {
            try
            {
                if (Directory.Exists(logRoot))
                {
                    Directory.Delete(logRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<BackupRestoreSourceManifest> ValidateAndFingerprintAsync(
        string repositoryRoot,
        BackupRestoreSourceManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!IsSha(manifest.Commit, 40) || manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("Source manifest must contain a 40-character commit and a non-empty file inventory.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<BackupRestoreSourceFile>(manifest.Files.Count);
        foreach (var entry in manifest.Files)
        {
            if (entry is null || !IsSafeRelativePath(entry.Path) || !seen.Add(entry.Path) || !IsSha(entry.Sha256, 64))
            {
                throw new InvalidOperationException($"Invalid source manifest entry: {entry?.Path ?? "<null>"}");
            }

            var fullPath = ResolveSourcePath(repositoryRoot, entry.Path);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Source manifest file is missing: {entry.Path}");
            }
            var actualHash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (!actualHash.Equals(entry.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Source manifest mismatch: {entry.Path}");
            }
            validated.Add(entry);
        }

        var fingerprint = ComputeFingerprint(validated);
        if (!string.IsNullOrEmpty(manifest.Fingerprint) && !manifest.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Source manifest fingerprint does not match its inventory.");
        }

        return manifest with { Fingerprint = fingerprint, Files = validated };
    }

    private static IReadOnlyList<string> ParseNulDelimitedPaths(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return [];
        }
        if (bytes[^1] != 0)
        {
            throw new InvalidOperationException("Git returned an incomplete NUL-delimited source inventory.");
        }

        var paths = new List<string>();
        var start = 0;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != 0)
                {
                    continue;
                }
                if (index == start)
                {
                    throw new InvalidOperationException("Git returned an empty source path.");
                }
                paths.Add(utf8.GetString(bytes, start, index - start));
                start = index + 1;
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException("Git returned a source path that is not valid UTF-8.", exception);
        }
        return paths;
    }

    private static string ResolveSourcePath(string repositoryRoot, string path)
    {
        if (!IsSafeRelativePath(path))
        {
            throw new InvalidOperationException($"Source path is not a safe repository-relative path: {path}");
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var current = root;
        foreach (var segment in path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.GetFullPath(Path.Combine(current, segment));
            if (!IsWithinRepository(root, current))
            {
                throw new InvalidOperationException($"Source path escapes the repository: {path}");
            }

            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget is not null)
            {
                var resolved = entry.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is null)
                {
                    throw new InvalidOperationException($"Source path has an unresolved symbolic link: {path}");
                }
                current = Path.GetFullPath(resolved.FullName);
                if (!IsWithinRepository(root, current))
                {
                    throw new InvalidOperationException($"Source path follows a symbolic link outside the repository: {path}");
                }
            }
        }
        return current;
    }

    private static bool IsWithinRepository(string repositoryRoot, string candidate)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(candidate);
        var prefix = root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0') || Path.IsPathRooted(path) || path.Contains(':') ||
            path.StartsWith('\\') || path.StartsWith('/') || path.Contains("../", StringComparison.Ordinal) ||
            path.Equals("..", StringComparison.Ordinal) || path.Contains("..\\", StringComparison.Ordinal) ||
            path.Contains("/..", StringComparison.Ordinal) || path.Contains("\\..", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static bool IsSha(string? value, int length)
        => value is { Length: var actualLength } && actualLength == length &&
            value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeFingerprint(IReadOnlyList<BackupRestoreSourceFile> files)
    {
        var lines = files.Select(static file => $"{file.Path}\t{file.Sha256}").Order(StringComparer.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
