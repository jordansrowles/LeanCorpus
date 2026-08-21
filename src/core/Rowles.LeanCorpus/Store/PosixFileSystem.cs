using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>POSIX filesystem durability operations.</summary>
internal sealed partial class PosixFileSystem : IPlatformFileSystem
{
    private const int O_RDONLY = 0;
    internal static readonly PosixFileSystem Instance = new();

    private PosixFileSystem() { }

    public void SyncFile(string path) => SyncDescriptor(path, O_RDONLY);

    public void SyncDirectory(string path) => SyncDescriptor(path, O_RDONLY);

    public bool IsTransient(Exception exception) => false;

    private static unsafe void SyncDescriptor(string path, int flags)
    {
        int byteCount = Encoding.UTF8.GetByteCount(path);
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount + 1);
        try
        {
            int written = Encoding.UTF8.GetBytes(path, 0, path.Length, rented, 0);
            rented[written] = 0;

            int descriptor;
            fixed (byte* pointer = rented)
                descriptor = open(pointer, flags);

            if (descriptor < 0)
                ThrowErrno("open", path, Marshal.GetLastWin32Error());

            int syncResult;
            int syncError;
            try
            {
                syncResult = fsync(descriptor);
                syncError = syncResult == 0 ? 0 : Marshal.GetLastWin32Error();
            }
            finally { _ = close(descriptor); }

            if (syncResult != 0)
                ThrowErrno("fsync", path, syncError);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ThrowErrno(string operation, string path, int error) =>
        throw new IOException($"{operation}('{path}') failed: errno {error}.", error);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
    private static unsafe partial int open(byte* pathname, int flags);

    [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static partial int fsync(int descriptor);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int close(int descriptor);
}
