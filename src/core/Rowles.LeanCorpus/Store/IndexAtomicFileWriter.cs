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
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(stream);
                if (durable)
                    stream.Flush(flushToDisk: true);
            }

            // On Windows, File.Move with overwrite:true fails with IOException if any handle
            // — even a transient FileShare.Read from File.ReadAllText (e.g. a concurrent
            // searcher refresh calling IndexRecovery.TryLoadCommit) — is open on the target.
            // Retry a few times with a short backoff before giving up.
            for (int retries = MoveRetries; ; retries--)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    break;
                }
                catch (IOException) when (retries > 0)
                {
                    Thread.Sleep(MoveRetryDelayMs);
                }
            }

            if (durable)
                DirectoryFsync.Sync(Path.GetDirectoryName(path) ?? string.Empty, strict: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
