using System.Collections.Concurrent;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Tracks process-written index files by directory so a commit inspects only its own files.
/// Versions prevent a concurrent rewrite from being cleared by an older synchronisation.
/// </summary>
internal static class DirtyFileTracker
{
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly ConcurrentDictionary<string, DirectoryDirtyState> s_directories =
        new(s_pathComparer);
    internal static void MarkWritten(string path)
    {
        Diagnostics.FileSystemDiagnostics.RecordDirtyRegistration();
        SplitPath(path, out string directory, out string fileName);
        var state = s_directories.GetOrAdd(directory, static _ => new DirectoryDirtyState());
        lock (state.SyncRoot)
            state.Files[fileName] = new Entry(++state.NextVersion);
    }

    internal static DirtyFile Move(string sourcePath, string destinationPath)
    {
        SplitPath(sourcePath, out string sourceDirectory, out string sourceName);
        SplitPath(destinationPath, out string destinationDirectory, out string destinationName);

        if (s_pathComparer.Equals(sourceDirectory, destinationDirectory))
        {
            var state = s_directories.GetOrAdd(destinationDirectory, static _ => new DirectoryDirtyState());
            lock (state.SyncRoot)
            {
                state.Files.Remove(sourceName);
                long version = ++state.NextVersion;
                state.Files[destinationName] = new Entry(version);
                return new DirtyFile(Path.Combine(destinationDirectory, destinationName), version);
            }
        }

        ForgetCore(sourceDirectory, sourceName);
        var destinationState = s_directories.GetOrAdd(destinationDirectory, static _ => new DirectoryDirtyState());
        lock (destinationState.SyncRoot)
        {
            long version = ++destinationState.NextVersion;
            destinationState.Files[destinationName] = new Entry(version);
            return new DirtyFile(Path.Combine(destinationDirectory, destinationName), version);
        }
    }

    internal static void Forget(string path)
    {
        SplitPath(path, out string directory, out string fileName);
        ForgetCore(directory, fileName);
    }

    internal static void Delete(string path) => Forget(path);

    internal static void ForgetDirectory(string directoryPath)
    {
        string directory = CanonicaliseDirectory(directoryPath);
        string prefix = directory + Path.DirectorySeparatorChar;
        foreach (string candidate in s_directories.Keys)
        {
            if (s_pathComparer.Equals(candidate, directory) || candidate.StartsWith(prefix, s_pathComparison))
                s_directories.TryRemove(candidate, out _);
        }
    }

    internal static List<DirtyFile> Snapshot(string directoryPath, Func<string, bool> includeFileName)
    {
        string directory = CanonicaliseDirectory(directoryPath);
        if (!s_directories.TryGetValue(directory, out var state))
        {
            Diagnostics.FileSystemDiagnostics.RecordDirtySnapshot(0, 0);
            return [];
        }

        List<DirtyFile> result;
        int scanned;
        lock (state.SyncRoot)
        {
            scanned = state.Files.Count;
            result = new List<DirtyFile>(scanned);
            foreach (var (fileName, entry) in state.Files)
            {
                if (includeFileName(fileName))
                    result.Add(new DirtyFile(Path.Combine(directory, fileName), entry.Version));
            }
        }

        Diagnostics.FileSystemDiagnostics.RecordDirtySnapshot(scanned, result.Count);
        return result;
    }

    /// <summary>
    /// Establishes the one-time baseline required when a process first opens an existing commit.
    /// A generation already made durable by this process does not need to be registered again.
    /// </summary>
    internal static void RequireDurabilityBaseline(
        string directoryPath,
        int generation,
        IEnumerable<string> referencedFileNames)
    {
        string directory = CanonicaliseDirectory(directoryPath);
        var state = s_directories.GetOrAdd(directory, static _ => new DirectoryDirtyState());
        lock (state.SyncRoot)
        {
            if (state.DurableGeneration >= generation)
                return;

            foreach (string fileName in referencedFileNames)
            {
                if (state.Files.ContainsKey(fileName))
                    continue;
                Diagnostics.FileSystemDiagnostics.RecordDirtyRegistration();
                state.Files[fileName] = new Entry(++state.NextVersion);
            }
        }
    }

    internal static void MarkDurableGeneration(string directoryPath, int generation)
    {
        string directory = CanonicaliseDirectory(directoryPath);
        var state = s_directories.GetOrAdd(directory, static _ => new DirectoryDirtyState());
        lock (state.SyncRoot)
            state.DurableGeneration = Math.Max(state.DurableGeneration, generation);
    }

    internal static void MarkSynced(DirtyFile file)
    {
        SplitPath(file.Path, out string directory, out string fileName);
        if (!s_directories.TryGetValue(directory, out var state))
            return;

        lock (state.SyncRoot)
        {
            if (state.Files.TryGetValue(fileName, out var current) && current.Version == file.Version)
                state.Files.Remove(fileName);
        }
    }

    private static void ForgetCore(string directory, string fileName)
    {
        if (!s_directories.TryGetValue(directory, out var state))
            return;
        lock (state.SyncRoot)
            state.Files.Remove(fileName);
    }

    private static void SplitPath(string path, out string directory, out string fileName)
    {
        string fullPath = Path.GetFullPath(path);
        directory = CanonicaliseDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);
        fileName = Path.GetFileName(fullPath);
    }

    private static string CanonicaliseDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal readonly record struct DirtyFile(string Path, long Version);
    private readonly record struct Entry(long Version);

    private sealed class DirectoryDirtyState
    {
        internal readonly Lock SyncRoot = new();
        internal readonly Dictionary<string, Entry> Files = new(s_pathComparer);
        internal long NextVersion;
        internal int DurableGeneration = -1;
    }
}
