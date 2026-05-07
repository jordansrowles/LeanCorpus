using System.Runtime.InteropServices;

namespace Rowles.LeanLucene.Store;

/// <summary>
/// Platform-specific P/Invoke declarations for memory prefetch hints.
/// All calls are advisory — failures do not affect correctness.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // Windows: PrefetchVirtualMemory (Win8+)
    [StructLayout(LayoutKind.Sequential)]
    internal struct WIN32_MEMORY_RANGE_ENTRY
    {
        public nint VirtualAddress;
        public nuint NumberOfBytes;
    }

    [LibraryImport("kernel32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrefetchVirtualMemory(
        nint hProcess,
        nuint numberOfEntries,
        WIN32_MEMORY_RANGE_ENTRY* virtualAddresses,
        uint flags);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    // POSIX: madvise (Linux/macOS)
    [LibraryImport("libc", SetLastError = false)]
    internal static partial int madvise(nint addr, nuint length, int advice);
}
