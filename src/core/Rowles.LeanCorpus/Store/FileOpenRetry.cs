using System.Runtime.CompilerServices;
using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Shared helper that opens files for reading with transient retry.
/// On Windows, freshly flushed files can be briefly locked by antivirus
/// real-time scanners intercepting FlushFileBuffers. This wrapper retries
/// a few times with a short backoff before giving up.
/// </summary>
internal static class FileOpenRetry
{
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 10;

    /// <summary>
    /// Opens a file for reading with <see cref="FileShare.Read"/>,
    /// retrying on <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    internal static FileStream OpenRead(string path)
        => Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// Opens a file for reading with <see cref="FileShare.Read"/> and
    /// <see cref="FileShare.Delete"/>, retrying on <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    internal static FileStream OpenReadDelete(string path)
        => Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    /// <summary>
    /// Reads the entire contents of a file as a UTF-8 string, opening with
    /// <see cref="FileShare.Read"/> and <see cref="FileShare.Delete"/> so that
    /// concurrent deletion (e.g. commit-policy pruning of old <c>segments_N</c>
    /// files) can proceed on Windows. Retries on <see cref="UnauthorizedAccessException"/>
    /// and converts to <see cref="IOException"/> on exhaustion so callers that
    /// previously used <c>File.ReadAllText</c> see the same exception type.
    /// </summary>
    internal static string ReadAllText(string path)
    {
        try
        {
            using var fs = OpenReadDelete(path);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException(
                $"The process cannot access the file '{path}' because it is being used by another process.");
        }
    }

    /// <summary>
    /// Opens a file with the specified mode, access, and share,
    /// retrying on <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    internal static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        int retries = MaxRetries;
        while (true)
        {
            try
            {
                return new FileStream(path, mode, access, share);
            }
            catch (UnauthorizedAccessException) when (retries-- > 0)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
    }
}
