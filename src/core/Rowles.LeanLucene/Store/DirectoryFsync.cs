using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Rowles.LeanLucene.Store;

/// <summary>
/// Cross-platform helper that flushes a directory's metadata (file-entry renames, creations,
/// deletions) to durable storage. Required for crash-safe atomic-rename commit protocols on
/// POSIX filesystems where directory entries are buffered independently of file contents.
/// </summary>
/// <remarks>
/// On Linux and macOS this opens the directory read-only, calls <c>fsync</c>, and closes
/// the descriptor. On Windows this is a no-op: NTFS journals directory updates synchronously
/// as part of the metadata transaction log, so an explicit directory sync is unnecessary
/// (and the Win32 API has no equivalent to <c>fsync(directory_fd)</c>).
/// </remarks>
internal static class DirectoryFsync
{
    private const int O_RDONLY = 0;

    internal delegate int OpenDirectoryDelegate(byte[] pathBytes, int byteCount);
    internal delegate int FileDescriptorDelegate(int fileDescriptor);
    internal delegate int GetLastErrorDelegate();

    /// <summary>
    /// Forces the directory's metadata to be persisted to the underlying storage device.
    /// On Windows this is a no-op. On Unix, errors are swallowed when <paramref name="strict"/>
    /// is false (best-effort) or thrown as <see cref="IOException"/> when true.
    /// </summary>
    /// <param name="directoryPath">The absolute path of the directory to flush.</param>
    /// <param name="strict">When true, fsync failures are surfaced as <see cref="IOException"/>.</param>
    public static void Sync(string directoryPath, bool strict = false)
    {
        SyncCore(
            directoryPath,
            strict,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            OpenDirectory,
            fsync,
            close,
            Marshal.GetLastWin32Error);
    }

    internal static void SyncCore(
        string directoryPath,
        bool strict,
        bool isWindows,
        OpenDirectoryDelegate openDirectory,
        FileDescriptorDelegate syncDirectory,
        FileDescriptorDelegate closeDirectory,
        GetLastErrorDelegate getLastError)
    {
        if (string.IsNullOrEmpty(directoryPath)) return;
        if (isWindows) return;

        int byteCount = Encoding.UTF8.GetByteCount(directoryPath);
        // +1 for null terminator. Use a rented buffer to avoid string interpolation / Marshal alloc.
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount + 1);
        try
        {
            int written = Encoding.UTF8.GetBytes(directoryPath, 0, directoryPath.Length, rented, 0);
            rented[written] = 0;

            int fd = openDirectory(rented, written);
            if (fd < 0)
            {
                if (strict)
                {
                    int err = getLastError();
                    throw new IOException($"open('{directoryPath}') failed: errno {err}");
                }
                return;
            }

            int rc;
            try { rc = syncDirectory(fd); }
            finally { _ = closeDirectory(fd); }

            if (rc != 0 && strict)
            {
                int err = getLastError();
                throw new IOException($"fsync('{directoryPath}') failed: errno {err}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static unsafe int OpenDirectory(byte[] pathBytes, int byteCount)
    {
        fixed (byte* ptr = pathBytes)
        {
            return open(ptr, O_RDONLY);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern unsafe int open(byte* pathname, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);

    /// <summary>
    /// Forces a previously written file's contents to be persisted to the underlying storage
    /// device. Equivalent to <c>fsync</c> on Unix and <c>FlushFileBuffers</c> on Windows.
    /// Errors are swallowed when <paramref name="strict"/> is false; otherwise they propagate.
    /// </summary>
    public static void SyncFile(string filePath, bool strict = false)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (strict)
        {
            SyncFileCore(filePath);
            return;
        }
        try
        {
            SyncFileCore(filePath);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static void SyncFileCore(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            fs.Flush(flushToDisk: true);
        }
        catch (IOException) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Flush(flushToDisk: true);
        }
    }
}
