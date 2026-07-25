using System.Runtime.CompilerServices;
using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Shared helper for file I/O with transient retry on Windows.
/// </summary>
/// <remarks>
/// On Windows, freshly flushed files can be briefly locked by antivirus
/// real-time scanners intercepting FlushFileBuffers. Read, move, delete,
/// and copy operations retry a few times to absorb these transient locks.
/// On Linux there are no mandatory file locks, so retry is skipped entirely.
///
/// Write/create operations (<see cref="Open(string, FileMode, FileAccess, FileShare)"/>
/// and its overload) do NOT retry. Creating or opening a file for exclusive
/// write is either an atomic success or a permanent conflict that retrying
/// cannot resolve. In particular, write-lock acquisition must fail fast so
/// that concurrent writer contention is diagnosed immediately.
/// </remarks>
internal static class FileOpenRetry
{
    // Windows: transient Defender scan locks need a brief retry window.
    // Linux: no mandatory file locking; zero retry overhead.
    private static readonly int TransientMaxRetries = OperatingSystem.IsWindows() ? 5 : 0;
    private static readonly int TransientRetryDelayMs = 200;

    /// <summary>
    /// Opens a file for reading with <see cref="FileShare.Read"/>,
    /// retrying on transient locks.
    /// </summary>
    internal static Stream OpenRead(string path)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when ((ex is UnauthorizedAccessException or IOException) && retries-- > 0)
            {
                Thread.Sleep(TransientRetryDelayMs);
            }
        }
    }

    /// <summary>
    /// Opens a file for reading with <see cref="FileShare.Read"/> and
    /// <see cref="FileShare.Delete"/>, retrying on transient locks.
    /// </summary>
    internal static Stream OpenReadDelete(string path)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (Exception ex) when ((ex is UnauthorizedAccessException or IOException) && retries-- > 0)
            {
                Thread.Sleep(TransientRetryDelayMs);
            }
        }
    }

    /// <summary>
    /// Reads the entire contents of a file as a UTF-8 string, opening with
    /// <see cref="FileShare.Read"/> and <see cref="FileShare.Delete"/> so that
    /// concurrent deletion (e.g. commit-policy pruning of old <c>segments_N</c>
    /// files) can proceed on Windows. Retries on transient locks and
    /// converts to <see cref="IOException"/> on exhaustion so callers that
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
    /// Opens a file with the specified mode, access, and share.
    /// Does NOT retry — retry is appropriate only for read, move, delete,
    /// and copy operations that may hit transient AV scan locks.
    /// Write/create and lock acquisition must fail fast.
    /// </summary>
    internal static Stream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        return new FileStream(path, mode, access, share);
    }

    /// <summary>
    /// Opens a file with the specified mode, access, share, buffer size, and options.
    /// Does NOT retry; see <see cref="Open(string, FileMode, FileAccess, FileShare)"/>.
    /// </summary>
    internal static Stream Open(string path, FileMode mode, FileAccess access, FileShare share,
        int bufferSize, FileOptions options)
    {
        return new FileStream(path, mode, access, share, bufferSize, options);
    }

    /// <summary>Flushes a facade-created file stream through to durable storage.</summary>
    internal static void FlushToDisk(Stream stream) => ((FileStream)stream).Flush(flushToDisk: true);

    /// <summary>
    /// Deletes a file, retrying on transient locks.
    /// <see cref="FileNotFoundException"/> and <see cref="DirectoryNotFoundException"/> are swallowed
    /// so callers can use this for best-effort cleanup without pre-checking existence.
    /// </summary>
    internal static void Delete(string path)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
        }
    }

    /// <summary>
    /// Moves a file, retrying on transient locks.
    /// </summary>
    internal static void Move(string sourcePath, string destPath, bool overwrite = false)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                File.Move(sourcePath, destPath, overwrite);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
        }
    }

    /// <summary>
    /// Copies a file, retrying on transient locks.
    /// </summary>
    internal static void Copy(string sourcePath, string destPath, bool overwrite = false)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                File.Copy(sourcePath, destPath, overwrite);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
        }
    }

    /// <summary>Thin wrapper around File.Exists for centralised I/O.</summary>
    internal static bool FileExists(string path) => File.Exists(path);

    /// <summary>Returns the length of a file in bytes.</summary>
    internal static long GetFileLength(string path) => new FileInfo(path).Length;

    /// <summary>Returns file metadata required by index inspection and backup operations.</summary>
    internal static FileMetadata GetFileMetadata(string path)
    {
        var info = new FileInfo(path);
        return new FileMetadata(info.Name, info.Extension, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>Creates a UTF-8 text reader over an existing stream.</summary>
    internal static TextReader OpenTextReader(Stream stream, Encoding encoding, bool leaveOpen = false) =>
        new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: leaveOpen);

    /// <summary>Creates a UTF-8 text writer over an existing stream.</summary>
    internal static TextWriter OpenTextWriter(Stream stream, Encoding encoding, bool leaveOpen = false) =>
        new StreamWriter(stream, encoding, leaveOpen: leaveOpen);

    /// <summary>Creates an auto-flushing text writer that appends to a file.</summary>
    internal static TextWriter OpenAppendText(string path)
    {
        var stream = Open(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream) { AutoFlush = true };
    }

    /// <summary>Thin wrapper around Directory.CreateDirectory for centralised I/O.</summary>
    internal static void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <summary>Thin wrapper around Directory.Exists for centralised I/O.</summary>
    internal static bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>Returns the full path of a directory's parent, or null for a root.</summary>
    internal static string? GetParentDirectory(string path) => Directory.GetParent(path)?.FullName;

    /// <summary>
    /// Deletes a directory, retrying on transient locks.
    /// <see cref="DirectoryNotFoundException"/> is swallowed.
    /// </summary>
    internal static void DeleteDirectory(string path, bool recursive)
    {
        int retries = TransientMaxRetries;
        while (true)
        {
            try
            {
                Directory.Delete(path, recursive);
                return;
            }
            catch (IOException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (UnauthorizedAccessException) when (retries-- > 0) { Thread.Sleep(TransientRetryDelayMs); }
            catch (DirectoryNotFoundException) { return; }
        }
    }

    /// <summary>Thin wrapper around Directory.EnumerateFiles for centralised I/O.</summary>
    internal static IEnumerable<string> EnumerateFiles(string path, string pattern) =>
        Directory.EnumerateFiles(path, pattern);

    /// <summary>Enumerates every file in a directory.</summary>
    internal static IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path);

    /// <summary>Enumerates every child directory in a directory.</summary>
    internal static IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);

    /// <summary>Enumerates all files and directories in a directory.</summary>
    internal static IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        Directory.EnumerateFileSystemEntries(path);

    /// <summary>Thin wrapper around Directory.GetFiles for centralised I/O.</summary>
    internal static string[] GetFiles(string path, string pattern) => Directory.GetFiles(path, pattern);

    /// <summary>Returns every file in a directory.</summary>
    internal static string[] GetFiles(string path) => Directory.GetFiles(path);

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

/// <summary>Immutable file metadata exposed without leaking <see cref="FileInfo"/> outside Store.</summary>
internal readonly record struct FileMetadata(
    string Name,
    string Extension,
    long Length,
    DateTime LastWriteTimeUtc);
