namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>
/// Allocates repository-owned temporary directories under
/// <c>&lt;repository root&gt;/artifacts/tmp/&lt;area&gt;/&lt;prefix&gt;&lt;32 hex&gt;</c> (ADR046).
/// Each directory is unique, owner-only on Unix and removed on dispose. This file has no
/// LeanCorpus dependency so projects that do not reference Tests.Shared can link it as source.
/// </summary>
internal static class RepositoryTemporaryStorage
{
    /// <summary>The marker file that identifies the repository root.</summary>
    public const string RootMarker = ".config/devops/test-suites.json";

    /// <summary>The only allowed areas.</summary>
    public static readonly IReadOnlyList<string> Areas =
    [
        "tests", "chaos", "benchmarks", "profiling", "dataforge", "server-tests", "aot", "devops", "devops-tests", "cli"
    ];

    private static readonly Lazy<string> LazyRepositoryRoot = new(() => ResolveRepositoryRoot(AppContext.BaseDirectory));

    /// <summary>The repository root that contains this test or tool's build output.</summary>
    public static string RepositoryRoot => LazyRepositoryRoot.Value;

    /// <summary>Returns <c>artifacts/tmp/&lt;area&gt;</c> for this checkout, creating it if needed.</summary>
    public static string GetAreaRoot(string area) => GetAreaRoot(RepositoryRoot, area);

    /// <summary>Creates a new, unique, owner-only directory in <paramref name="area"/>.</summary>
    public static RepositoryTemporaryDirectory CreateDirectory(string area, string prefix = "")
        => CreateDirectoryUnder(RepositoryRoot, area, prefix);

    /// <summary>Creates a new, unique, owner-only directory path in <paramref name="area"/> without an owner object.</summary>
    public static string CreateDirectoryPath(string area, string prefix = "")
        => CreateDirectoryUnder(RepositoryRoot, area, prefix).Release();

    /// <summary>Finds the nearest ancestor of <paramref name="startPath"/> that contains <see cref="RootMarker"/>.</summary>
    public static string ResolveRepositoryRoot(string startPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);
        for (var current = new DirectoryInfo(Path.GetFullPath(startPath)); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, ".config", "devops", "test-suites.json")))
                return current.FullName;
        }

        throw new InvalidOperationException(
            $"No LeanCorpus repository root ({RootMarker}) was found above '{startPath}'. Repository temporary storage never falls back to the system temporary directory.");
    }

    /// <summary>Creates a unique directory under <paramref name="repositoryRoot"/>; exposed for tests of this helper.</summary>
    public static RepositoryTemporaryDirectory CreateDirectoryUnder(string repositoryRoot, string area, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        string areaRoot = GetAreaRoot(repositoryRoot, area);
        string path = Path.Combine(areaRoot, string.Concat(prefix, Guid.NewGuid().ToString("N")));
        CreateOwnerOnly(path);
        return new RepositoryTemporaryDirectory(path);
    }

    /// <summary>
    /// Deletes <paramref name="path"/> recursively, retrying while Windows releases handles held by
    /// terminated processes. Rethrows after five seconds.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        for (var attempt = 1; ; attempt++)
        {
            if (!Directory.Exists(path))
                return;
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(Math.Min(50 * attempt, 500));
            }
        }
    }

    private static string GetAreaRoot(string repositoryRoot, string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Areas.Contains(area, StringComparer.Ordinal))
            throw new ArgumentException($"'{area}' is not a repository temporary storage area. Use one of: {string.Join(", ", Areas)}.", nameof(area));

        string tmpRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "artifacts", "tmp");
        string areaRoot = Path.Combine(tmpRoot, area);
        if (!Directory.Exists(areaRoot))
        {
            Directory.CreateDirectory(tmpRoot);
            CreateOwnerOnly(areaRoot);
        }

        return areaRoot;
    }

    private static void CreateOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

/// <summary>A repository temporary directory that is deleted on dispose.</summary>
internal sealed class RepositoryTemporaryDirectory : IDisposable
{
    private bool _released;

    internal RepositoryTemporaryDirectory(string path) => Path = path;

    /// <summary>The full path of the directory.</summary>
    public string Path { get; }

    /// <summary>Stops this object deleting the directory and returns its path.</summary>
    public string Release()
    {
        _released = true;
        return Path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_released)
            RepositoryTemporaryStorage.DeleteDirectory(Path);
    }
}
