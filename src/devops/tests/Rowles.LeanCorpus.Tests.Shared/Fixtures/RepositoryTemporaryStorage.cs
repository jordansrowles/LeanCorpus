namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>
/// Allocates repository-owned temporary directories under
/// <c>&lt;repository root&gt;/artifacts/tmp/&lt;area&gt;/&lt;prefix&gt;&lt;32 hex&gt;</c> (ADR046), or under a
/// validated owner-only <see cref="RootOverrideVariable"/> directory.
/// Each directory is unique, owner-only on Unix and removed on dispose. This file has no
/// LeanCorpus dependency so projects that do not reference Tests.Shared can link it as source.
/// </summary>
internal static class RepositoryTemporaryStorage
{
    /// <summary>
    /// Optional override for the parent of the area directories, for example an owner-only directory on
    /// a RAM disk. It must already exist, must not be a symbolic link and, on Unix, must be owner-only.
    /// </summary>
    public const string RootOverrideVariable = "LEANCORPUS_TEMP_ROOT";

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
        CreateNewOwnerOnly(path);
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

    /// <summary>
    /// Returns the parent of the area directories: <paramref name="overrideRoot"/> when it is set and
    /// valid, otherwise <c>&lt;repository root&gt;/artifacts/tmp</c>. Neither may be a symbolic link. An
    /// override must already be owner-only; the default root is created or tightened to owner-only.
    /// </summary>
    public static string ResolveTemporaryRoot(string repositoryRoot, string? overrideRoot)
    {
        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            string defaultRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "artifacts", "tmp");
            EnsureSecureDirectory(defaultRoot, "The repository temporary root");
            return defaultRoot;
        }

        string root = Path.GetFullPath(overrideRoot);
        if (!Directory.Exists(root))
            throw new InvalidOperationException($"{RootOverrideVariable} '{root}' does not exist. Create it, owner-only, before running.");
        RejectLink(root, RootOverrideVariable);
        if (HasGroupOrOtherAccess(root))
            throw new InvalidOperationException($"{RootOverrideVariable} '{root}' must be owner-only (chmod 700).");
        return root;
    }

    /// <summary>
    /// Returns a new unique path in <paramref name="area"/> without creating it, for code that creates
    /// the directory itself. The area root is owner-only, so anything created beneath it is private to
    /// the current user even when created with default permissions.
    /// </summary>
    public static string NewPath(string area, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return Path.Combine(GetAreaRoot(area), string.Concat(prefix, Guid.NewGuid().ToString("N")));
    }

    private static string GetAreaRoot(string repositoryRoot, string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Areas.Contains(area, StringComparer.Ordinal))
            throw new ArgumentException($"'{area}' is not a repository temporary storage area. Use one of: {string.Join(", ", Areas)}.", nameof(area));

        string tmpRoot = ResolveTemporaryRoot(repositoryRoot, Environment.GetEnvironmentVariable(RootOverrideVariable));
        string areaRoot = Path.Combine(tmpRoot, area);
        EnsureSecureDirectory(areaRoot, $"Temporary storage area '{area}'");
        return areaRoot;
    }

    /// <summary>
    /// Creates <paramref name="path"/> owner-only, or validates an existing one: it must not be a
    /// symbolic link or reparse point, and on Unix any group or other access is removed. Tightening a
    /// directory owned by another user fails, so a foreign directory is never trusted.
    /// </summary>
    public static void EnsureSecureDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                throw new InvalidOperationException($"{description} '{path}' exists but is not a directory.");
            CreateOwnerOnly(path);
        }

        RejectLink(path, description);
        if (!OperatingSystem.IsWindows() && HasGroupOrOtherAccess(path))
            File.SetUnixFileMode(path, OwnerOnly);
    }

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static void RejectLink(string path, string description)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"{description} '{path}' is a symbolic link or reparse point. Use a real directory.");
    }

    private static bool HasGroupOrOtherAccess(string path)
    {
        if (OperatingSystem.IsWindows())
            return false;
        const UnixFileMode groupOrOther = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & groupOrOther) != 0;
    }

    private static void CreateOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, OwnerOnly);
    }

    private static void CreateNewOwnerOnly(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException($"Temporary directory '{path}' already exists; refusing to reuse it.");
        CreateOwnerOnly(path);
        if (!OperatingSystem.IsWindows() && HasGroupOrOtherAccess(path))
            File.SetUnixFileMode(path, OwnerOnly);
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
