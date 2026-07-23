using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Writes a file through a same-directory temporary file and atomic replacement.
/// </summary>
internal static class IndexAtomicFileWriter
{
    public static void WriteText(string path, string contents, bool durable)
    {
        Write(path, durable, stream =>
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
        });
    }

    private const int MoveRetries = 5;
    private const int MoveRetryDelayMs = 10;

    public static void Write(string path, bool durable, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = FileOpenRetry.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                if (durable)
                    FileOpenRetry.FlushToDisk(stream);
            }

            FileOpenRetry.Move(tempPath, path, overwrite: true);

            if (durable)
                DirectoryFsync.Sync(Path.GetDirectoryName(path) ?? string.Empty, strict: true);
        }
        catch
        {
            try { FileOpenRetry.Delete(tempPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "atomic-write temp file cleanup"); }
            throw;
        }
    }
}
