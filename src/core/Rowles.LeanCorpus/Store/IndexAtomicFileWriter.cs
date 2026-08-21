using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Writes a file through a same-directory temporary file and atomic replacement.
/// </summary>
internal static class IndexAtomicFileWriter
{
    public static void WriteText(string path, string contents, bool durable)
        => WriteText(path, contents, durable, syncDirectory: true);

    internal static void WriteText(string path, string contents, bool durable, bool syncDirectory)
    {
        Write(path, durable, syncDirectory, stream =>
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });
    }

    private const int MoveRetries = 5;
    private const int MoveRetryDelayMs = 10;

    public static void Write(string path, bool durable, Action<Stream> write)
        => Write(path, durable, syncDirectory: true, write);

    internal static void Write(string path, bool durable, bool syncDirectory, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        // A unique same-directory name preserves atomic rename semantics while
        // allowing concurrent writers to different generations of the same file.
        var tempPath = string.Concat(path, ".", Guid.NewGuid().ToString("N"), ".tmp");
        try
        {
            using (var stream = FileOpenRetry.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                if (durable)
                    FileOpenRetry.FlushToDisk(stream);
            }
            DirtyFileTracker.MarkWritten(tempPath);

            var publishedFile = FileOpenRetry.Move(tempPath, path, overwrite: true);

            if (durable && syncDirectory)
                DirectoryFsync.Sync(Path.GetDirectoryName(path) ?? string.Empty, strict: true);

            if (durable)
                DirtyFileTracker.MarkSynced(publishedFile);
        }
        catch
        {
            try { FileOpenRetry.Delete(tempPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "atomic-write temp file cleanup"); }
            throw;
        }
    }
}
