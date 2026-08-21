namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Tracks files written by this process so commits synchronise only changed files.
/// Versions prevent a concurrent write from being cleared by an older sync.
/// </summary>
internal static class DirtyFileTracker
{
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Dictionary<string, Entry> s_files = new(s_pathComparer);
    private static readonly Lock s_lock = new();
    private static long s_version;

    internal static void MarkWritten(string path)
    {
        string fullPath = Path.GetFullPath(path);
        lock (s_lock)
            s_files[fullPath] = new Entry(++s_version);
    }

    internal static DirtyFile Move(string sourcePath, string destinationPath)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        lock (s_lock)
        {
            s_files.Remove(source);
            long version = ++s_version;
            s_files[destination] = new Entry(version);
            return new DirtyFile(destination, version);
        }
    }

    internal static void Forget(string path)
    {
        string fullPath = Path.GetFullPath(path);
        lock (s_lock)
            s_files.Remove(fullPath);
    }

    internal static void Delete(string path) => Forget(path);

    internal static void ForgetDirectory(string directoryPath)
    {
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        string prefix = directory + Path.DirectorySeparatorChar;
        lock (s_lock)
        {
            // Dictionary.Remove does not invalidate enumerators on supported runtimes.
            foreach (string path in s_files.Keys)
            {
                if (path.StartsWith(prefix, s_pathComparison))
                    s_files.Remove(path);
            }
        }
    }

    internal static List<DirtyFile> Snapshot(string directoryPath, Func<string, bool> includeFileName)
    {
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var result = new List<DirtyFile>();
        lock (s_lock)
        {
            foreach (var (path, entry) in s_files)
            {
                if (!s_pathComparer.Equals(Path.GetDirectoryName(path), directory))
                    continue;
                string fileName = Path.GetFileName(path);
                if (includeFileName(fileName))
                    result.Add(new DirtyFile(path, entry.Version));
            }
        }
        return result;
    }

    internal static void MarkSynced(DirtyFile file)
    {
        lock (s_lock)
        {
            if (s_files.TryGetValue(file.Path, out var current) && current.Version == file.Version)
                s_files.Remove(file.Path);
        }
    }

    internal readonly record struct DirtyFile(string Path, long Version);
    private readonly record struct Entry(long Version);
}
