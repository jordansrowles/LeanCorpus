namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Shared helpers for benchmark temp directory management.
/// Centralises the Create/Delete pattern used across multiple benchmark classes
/// so that stale segment files don't accumulate in /tmp over a long benchmark run.
/// </summary>
public static class BenchmarkHelpers
{
    /// <summary>
    /// Root directory for all benchmark temp files. Uses <c>bench/tmp</c> under the
    /// repository root (half-TB NVMe) instead of the 12 GB tmpfs RAM disk.
    /// </summary>
    public static string TempRoot { get; } = ResolveTempRoot();

    /// <summary>
    /// Creates a unique temporary directory under <see cref="TempRoot"/>
    /// with the given prefix and a GUID suffix.
    /// </summary>
    public static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(TempRoot, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes the entire <see cref="TempRoot"/> directory tree. Call at the
    /// beginning or end of a full benchmark run to reclaim all accumulated temp data.
    /// </summary>
    public static void CleanTempRoot()
    {
        if (Directory.Exists(TempRoot))
        {
            // Aggressive GC to release any lingering mmap handles before nuking the tree.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Directory.Delete(TempRoot, recursive: true);
        }
    }

    private static string ResolveTempRoot()
    {
        // Walk up from the benchmark assembly location until we find the
        // repository root (marked by Rowles.LeanCorpus.slnx), then anchor
        // bench/tmp there. Uses the assembly path rather than the current
        // directory so it works regardless of how BenchmarkDotNet sets the cwd.
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rowles.LeanCorpus.slnx")))
            {
                var root = Path.Combine(current.FullName, "bench", "tmp");
                Directory.CreateDirectory(root);
                return root;
            }

            current = current.Parent;
        }

        // Fallback: use the current directory (shouldn't happen in practice).
        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "bench", "tmp");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// Recursively deletes a directory if it exists. Retries after a full GC
    /// sweep when the first attempt fails with <see cref="IOException"/>,
    /// which can happen on Linux when mmap-backed files haven't been finalised.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // On Linux, MemoryMappedFile handles may not be fully released
            // until the GC finalises the SafeMemoryMappedViewHandle.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // If it still fails after GC, let the exception propagate —
            // a transient /tmp-full run just has to clean up on the next attempt.
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
