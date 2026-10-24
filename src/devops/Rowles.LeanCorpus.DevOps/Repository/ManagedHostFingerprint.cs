using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Repository;

internal static class ManagedHostFingerprint
{
    private const int SchemaVersion = 1;
    private const string StageDirectory = "bootstrap-staging";
    private static readonly string[] SourceExtensions = [".cs", ".csproj", ".json", ".props", ".targets", ".slnx"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static string? Validate(string repositoryRoot, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string root;
        string executable;
        try
        {
            root = Path.GetFullPath(repositoryRoot);
            executable = Path.GetFullPath(executablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Could not identify the managed DevOps executable: {exception.Message}";
        }

        var expectedRootExecutable = Path.Combine(root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        var stageRoot = Path.Combine(root, "artifacts", "devops", StageDirectory);
        string manifestPath;
        if (PathsEqual(executable, expectedRootExecutable))
        {
            manifestPath = Path.Combine(root, "artifacts", "devops", "managed-host.json");
        }
        else if (IsWithin(stageRoot, executable))
        {
            manifestPath = Path.Combine(Path.GetDirectoryName(executable)!, "managed-host.json");
        }
        else
        {
            // Running the host with `dotnet <assembly>` is the development route and has no promoted apphost.
            return null;
        }

        if (!File.Exists(manifestPath))
        {
            return $"The managed DevOps executable '{executable}' has no promotion manifest at '{manifestPath}'. Run `pwsh ./setup.ps1` to publish it.";
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ManagedHostManifest>(File.ReadAllBytes(manifestPath), JsonOptions);
            if (manifest is null || manifest.SchemaVersion != SchemaVersion)
            {
                return "The managed DevOps promotion manifest is empty or uses an unsupported schema.";
            }

            var currentSources = GetSourceEntries(root);
            if (!string.Equals(manifest.SourceFingerprint, ComputeSourceFingerprint(currentSources), StringComparison.Ordinal) || !SourceEntriesEqual(manifest.SourceFiles, currentSources))
            {
                return "The managed DevOps apphost is stale because its source inputs changed. Run `pwsh ./setup.ps1` to rebuild it.";
            }

            var executableHash = HashFile(executable);
            if (!string.Equals(manifest.ExecutableSha256, executableHash, StringComparison.Ordinal))
            {
                return "The managed DevOps executable does not match its promotion manifest. Run `pwsh ./setup.ps1` to rebuild it.";
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return $"Could not validate the managed DevOps promotion manifest: {exception.Message}";
        }
    }

    private static IReadOnlyList<SourceFileFingerprint> GetSourceEntries(string repositoryRoot)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var repositoryFiles = new[]
        {
            "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
            "src/devops/Directory.Build.props", "src/devops/Directory.Packages.props"
        };
        foreach (var relativePath in repositoryFiles)
        {
            if (File.Exists(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                paths.Add(relativePath);
            }
        }

        AddDirectoryFiles(repositoryRoot, "src/devops/Rowles.LeanCorpus.DevOps", paths);
        AddDirectoryFiles(repositoryRoot, ".config/devops", paths);
        if (!paths.Contains(RepositoryProjectCatalog.GetRelativePath(RepositoryProjectRole.DevOpsHost)))
        {
            throw new InvalidDataException("The managed DevOps host project is missing from the selected checkout.");
        }

        return paths.Order(StringComparer.Ordinal)
            .Select(relativePath => new SourceFileFingerprint(relativePath, HashFile(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))))
            .ToArray();
    }

    private static void AddDirectoryFiles(string repositoryRoot, string relativeDirectory, HashSet<string> paths)
    {
        var fullDirectory = Path.Combine(repositoryRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(fullDirectory))
        {
            return;
        }

        foreach (var fullPath in EnumerateFilesWithoutLinks(fullDirectory))
        {
            var extension = Path.GetExtension(fullPath);
            if (!SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Managed DevOps source input '{fullPath}' is a link or reparse point.");
            }

            paths.Add(Path.GetRelativePath(repositoryRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string directory)
    {
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Managed DevOps source directory '{directory}' is a link or reparse point.");
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            yield return file;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            foreach (var file in EnumerateFilesWithoutLinks(childDirectory))
            {
                yield return file;
            }
        }
    }

    private static string ComputeSourceFingerprint(IEnumerable<SourceFileFingerprint> entries)
    {
        var canonical = new StringBuilder();
        foreach (var entry in entries)
        {
            canonical.Append(entry.Path).Append('\0').Append(entry.Sha256).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static bool SourceEntriesEqual(IReadOnlyList<SourceFileFingerprint>? left, IReadOnlyList<SourceFileFingerprint> right)
        => left is not null && left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.Path, pair.Second.Path, StringComparison.Ordinal) &&
            string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.Ordinal));

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public sealed record SourceFileFingerprint(string Path, string Sha256);

    public sealed record ManagedHostManifest(int SchemaVersion, string ExecutableSha256, string SourceFingerprint, IReadOnlyList<SourceFileFingerprint> SourceFiles);
}
