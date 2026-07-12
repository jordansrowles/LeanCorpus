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

    /// <summary>
    /// Deletes a file, retrying on <see cref="IOException"/> (file locked by AV or pending mmap release).
    /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> are swallowed
    /// so callers can use this for best-effort cleanup without pre-checking existence.
    /// </summary>
    internal static void Delete(string path)
    {
        int retries = MaxRetries;
        while (true)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
        }
    }

    /// <summary>
    /// Moves a file, retrying on <see cref="IOException"/> (target or source locked).
    /// </summary>
    internal static void Move(string sourcePath, string destPath, bool overwrite = false)
    {
        int retries = MaxRetries;
        while (true)
        {
            try
            {
                File.Move(sourcePath, destPath, overwrite);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
        }
    }

    /// <summary>
    /// Copies a file, retrying on <see cref="IOException"/> (source or target locked).
    /// </summary>
    internal static void Copy(string sourcePath, string destPath, bool overwrite = false)
    {
        int retries = MaxRetries;
        while (true)
        {
            try
            {
                File.Copy(sourcePath, destPath, overwrite);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
        }
    }

    /// <summary>Thin wrapper around File.Exists for centralised I/O.</summary>
    internal static bool FileExists(string path) => File.Exists(path);

    /// <summary>Thin wrapper around Directory.CreateDirectory for centralised I/O.</summary>
    internal static void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>
    /// Deletes a directory, retrying on <see cref="IOException"/> (files within still locked).
    /// <see cref="DirectoryNotFoundException"/> is swallowed.
    /// </summary>
    internal static void DeleteDirectory(string path, bool recursive)
    {
        int retries = MaxRetries;
        while (true)
        {
            try
            {
                Directory.Delete(path, recursive);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(RetryDelayMs); }
            catch (DirectoryNotFoundException) { return; }
        }
    }

    /// <summary>Thin wrapper around Directory.EnumerateFiles for centralised I/O.</summary>
    internal static IEnumerable<string> EnumerateFiles(string path, string pattern) =>
        Directory.EnumerateFiles(path, pattern);

    /// <summary>Thin wrapper around Directory.GetFiles for centralised I/O.</summary>
    internal static string[] GetFiles(string path, string pattern) => Directory.GetFiles(path, pattern);

    /// <summary>
    /// Reads lines from a text file with retry on open, using
    /// <see cref="FileShare.Read"/> and <see cref="FileShare.Delete"/>.
    /// </summary>
    internal static IEnumerable<string> ReadLines(string path, Encoding encoding)
    {
        using var fs = OpenReadDelete(path);
        using var sr = new StreamReader(fs, encoding);
        string? line;
        while ((line = sr.ReadLine()) is not null)
            yield return line;
    }
}
