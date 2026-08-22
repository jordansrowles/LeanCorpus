namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Cross-platform helper that flushes a directory's metadata (file-entry renames, creations,
/// deletions) to durable storage. Required for crash-safe atomic-rename commit protocols on
/// POSIX filesystems where directory entries are buffered independently of file contents.
/// </summary>
/// <remarks>
/// POSIX implementations open the directory read-only and call <c>fsync</c>. Windows opens
/// a directory handle with backup semantics and calls <c>FlushFileBuffers</c>, tolerating the
/// access-denied result returned by filesystems that do not support directory flushing.
/// </remarks>
internal static class DirectoryFsync
{
    /// <summary>
    /// Forces the directory's metadata to be persisted to the underlying storage device.
    /// Errors are swallowed when <paramref name="strict"/> is false (best-effort) or thrown as
    /// <see cref="IOException"/> when true, except for unsupported Windows directory flushing.
    /// </summary>
    /// <param name="directoryPath">The absolute path of the directory to flush.</param>
    /// <param name="strict">When true, fsync failures are surfaced as <see cref="IOException"/>.</param>
    public static void Sync(string directoryPath, bool strict = false)
    {
        if (string.IsNullOrEmpty(directoryPath)) return;
        long startedAt = Diagnostics.FileSystemDiagnostics.StartSync();
        long directoryStartedAt = Diagnostics.FileSystemDiagnostics.StartDirectorySync();
        DirectorySyncResult result = DirectorySyncResult.Failed;
        try
        {
            if (strict)
            {
                result = PlatformFileSystem.SyncDirectory(directoryPath);
                return;
            }

            try { result = PlatformFileSystem.SyncDirectory(directoryPath); }
            catch (FileNotFoundException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "directory fsync (non-strict)"); }
            catch (DirectoryNotFoundException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "directory fsync (non-strict)"); }
            catch (UnauthorizedAccessException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "directory fsync (non-strict)"); }
            catch (IOException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "directory fsync (non-strict)"); }
        }
        finally
        {
            Diagnostics.FileSystemDiagnostics.RecordDirectorySync(directoryStartedAt, result);
            Diagnostics.FileSystemDiagnostics.RecordSync(startedAt);
        }
    }

    /// <summary>
    /// Forces a previously written file's contents to be persisted to the underlying storage
    /// device. Equivalent to <c>fsync</c> on Unix and <c>FlushFileBuffers</c> on Windows.
    /// Errors are swallowed when <paramref name="strict"/> is false; otherwise they propagate.
    /// </summary>
    public static void SyncFile(string filePath, bool strict = false)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        long startedAt = Diagnostics.FileSystemDiagnostics.StartSync();
        long fileStartedAt = Diagnostics.FileSystemDiagnostics.StartFileSync();
        try
        {
            if (strict)
            {
                SyncFileCore(filePath);
                return;
            }
            try
            {
                SyncFileCore(filePath);
            }
            catch (FileNotFoundException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "fsync (non-strict)"); }
            catch (DirectoryNotFoundException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "fsync (non-strict)"); }
            catch (UnauthorizedAccessException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "fsync (non-strict)"); }
            catch (IOException ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "fsync (non-strict)"); }
        }
        finally
        {
            Diagnostics.FileSystemDiagnostics.RecordFileSync(fileStartedAt);
            Diagnostics.FileSystemDiagnostics.RecordSync(startedAt);
        }
    }

    private static void SyncFileCore(string filePath)
    {
        PlatformFileSystem.SyncFile(filePath);
    }
}
