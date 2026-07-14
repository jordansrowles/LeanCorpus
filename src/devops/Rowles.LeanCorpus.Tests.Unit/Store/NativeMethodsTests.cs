using System.Reflection;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Store;

/// <summary>
/// Comprehensive tests for <see cref="NativeMethods"/> — struct layout, P/Invoke
/// signatures, live-fire calls with safe arguments, and integration via
/// <see cref="IndexInput.Prefetch"/>.
/// </summary>
[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class NativeMethodsTests : IDisposable
{
    private readonly string _dir;

    public NativeMethodsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lc_nm_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    // ── Constants ──

    [Fact(DisplayName = "NativeMethods: POSIX_FADV_DONTNEED equals 4")]
    public void PosixFadvDontneed_Equals4()
    {
        Assert.Equal(4, NativeMethods.POSIX_FADV_DONTNEED);
    }

    // ── Struct layout ──

    [Fact(DisplayName = "NativeMethods: WIN32_MEMORY_RANGE_ENTRY has Sequential layout")]
    public void Win32MemoryRangeEntry_HasSequentialLayout()
    {
        // StructLayoutAttribute is a pseudo-custom attribute in .NET —
        // the layout kind is surfaced on Type directly.
        Assert.True(typeof(NativeMethods.WIN32_MEMORY_RANGE_ENTRY).IsLayoutSequential);
    }

    [Fact(DisplayName = "NativeMethods: WIN32_MEMORY_RANGE_ENTRY size equals two pointer widths")]
    public void Win32MemoryRangeEntry_SizeMatchesTwoPointers()
    {
        // Two pointer-sized integers: nint + nuint
        var size = Marshal.SizeOf<NativeMethods.WIN32_MEMORY_RANGE_ENTRY>();
        Assert.Equal(IntPtr.Size * 2, size); // 8 on 32-bit, 16 on 64-bit
    }

    [Fact(DisplayName = "NativeMethods: WIN32_MEMORY_RANGE_ENTRY field offsets are correct")]
    public void Win32MemoryRangeEntry_FieldOffsetsAreCorrect()
    {
        var vaOffset = Marshal.OffsetOf<NativeMethods.WIN32_MEMORY_RANGE_ENTRY>("VirtualAddress");
        var nbOffset = Marshal.OffsetOf<NativeMethods.WIN32_MEMORY_RANGE_ENTRY>("NumberOfBytes");
        Assert.Equal(0, vaOffset.ToInt32());
        Assert.Equal(IntPtr.Size, nbOffset.ToInt32());
    }

    [Fact(DisplayName = "NativeMethods: WIN32_MEMORY_RANGE_ENTRY round-trip preserves values")]
    public unsafe void Win32MemoryRangeEntry_RoundTripPreservesValues()
    {
        var entry = new NativeMethods.WIN32_MEMORY_RANGE_ENTRY
        {
            VirtualAddress = unchecked((nint)0xDEAD_BEEF_DEAD_BEEF),
            NumberOfBytes = 4096
        };
        Assert.Equal(unchecked((nint)0xDEAD_BEEF_DEAD_BEEF), entry.VirtualAddress);
        Assert.Equal((nuint)4096, entry.NumberOfBytes);
    }

    // ── P/Invoke signatures ──

    [Fact(DisplayName = "NativeMethods: PrefetchVirtualMemory has correct signature and attributes")]
    public void PrefetchVirtualMemory_HasCorrectSignature()
    {
        var method = GetStaticMethod("PrefetchVirtualMemory");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);

        // [return: MarshalAs(UnmanagedType.Bool)]
        var returnAttr = method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>();
        Assert.NotNull(returnAttr);
        Assert.Equal(UnmanagedType.Bool, returnAttr.Value);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(nint), parameters[0].ParameterType);
        Assert.Equal(typeof(nuint), parameters[1].ParameterType);
        Assert.True(parameters[2].ParameterType.IsPointer);
        Assert.Equal(typeof(uint), parameters[3].ParameterType);

        AssertLibraryImport(method, "kernel32.dll", setLastError: false);
    }

    [Fact(DisplayName = "NativeMethods: GetCurrentProcess has correct signature and attributes")]
    public void GetCurrentProcess_HasCorrectSignature()
    {
        var method = GetStaticMethod("GetCurrentProcess");
        Assert.NotNull(method);
        Assert.Equal(typeof(nint), method.ReturnType);
        Assert.Empty(method.GetParameters());

        AssertLibraryImport(method, "kernel32.dll", setLastError: null);
    }

    [Fact(DisplayName = "NativeMethods: madvise has correct signature and attributes")]
    public void Madvise_HasCorrectSignature()
    {
        var method = GetStaticMethod("madvise");
        Assert.NotNull(method);
        Assert.Equal(typeof(int), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(nint), parameters[0].ParameterType);
        Assert.Equal(typeof(nuint), parameters[1].ParameterType);
        Assert.Equal(typeof(int), parameters[2].ParameterType);

        AssertLibraryImport(method, "libc", setLastError: false);
    }

    [Fact(DisplayName = "NativeMethods: posix_fadvise has correct signature and attributes")]
    public void PosixFadvise_HasCorrectSignature()
    {
        var method = GetStaticMethod("posix_fadvise");
        Assert.NotNull(method);
        Assert.Equal(typeof(int), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(SafeFileHandle), parameters[0].ParameterType);
        Assert.Equal(typeof(long), parameters[1].ParameterType);
        Assert.Equal(typeof(long), parameters[2].ParameterType);
        Assert.Equal(typeof(int), parameters[3].ParameterType);

        AssertLibraryImport(method, "libc", setLastError: false);
    }

    // ── Live-fire calls (platform-gated) ──

    [Fact(DisplayName = "NativeMethods: GetCurrentProcess returns non-zero on Windows")]
    public unsafe void GetCurrentProcess_ReturnsNonZeroOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        nint handle = NativeMethods.GetCurrentProcess();
        Assert.NotEqual(nint.Zero, handle);
    }

    [Fact(DisplayName = "NativeMethods: PrefetchVirtualMemory with zeroed entry does not crash on Windows")]
    public unsafe void PrefetchVirtualMemory_ZeroedEntryDoesNotCrashOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        nint handle = NativeMethods.GetCurrentProcess();
        Assert.NotEqual(nint.Zero, handle);

        var entry = new NativeMethods.WIN32_MEMORY_RANGE_ENTRY();
        // Zeroed entry (null VirtualAddress, zero NumberOfBytes) — returns false, never crashes
        bool result = NativeMethods.PrefetchVirtualMemory(handle, 1, &entry, 0);
        Assert.False(result);
    }

    [Fact(DisplayName = "NativeMethods: madvise with null pointer does not throw on Linux/macOS")]
    public unsafe void Madvise_NullPointerDoesNotThrowOnPosix()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        // MADV_SEQUENTIAL = 2.  Null addr + zero length: kernel may return 0
        // (no pages to advise) or -1/EINVAL.  The contract is no crash, no throw.
        int result = NativeMethods.madvise(nint.Zero, 0, 2);
        Assert.True(result is 0 or -1);
    }

    [Fact(DisplayName = "NativeMethods: posix_fadvise with invalid fd does not crash on Linux")]
    public unsafe void PosixFadvise_InvalidFdDoesNotCrashOnLinux()
    {
        if (!OperatingSystem.IsLinux()) return;

        // posix_fadvise returns 0 on success and the errno value directly on failure
        // (unlike most syscalls that return -1).  fd=-1 → EBADF (9).
        using var handle = new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
        int result = NativeMethods.posix_fadvise(handle, 0, 0, NativeMethods.POSIX_FADV_DONTNEED);
        Assert.Equal(9, result); // EBADF
    }

    // ── Integration via IndexInput.Prefetch ──

    [Fact(DisplayName = "NativeMethods: IndexInput.Prefetch on small file does not throw")]
    public void IndexInput_PrefetchOnSmallFileDoesNotThrow()
    {
        var path = Path.Combine(_dir, "prefetch.bin");
        File.WriteAllBytes(path, new byte[4096]);
        using var input = new IndexInput(path);
        input.Prefetch();
    }

    [Fact(DisplayName = "NativeMethods: IndexInput.Prefetch on empty file returns early safely")]
    public void IndexInput_Prefetch_EmptyFileReturnsSafely()
    {
        var path = Path.Combine(_dir, "empty.bin");
        File.WriteAllBytes(path, []);
        using var input = new IndexInput(path);
        input.Prefetch();
        input.Dispose();
    }

    // ── Helpers ──

    private static MethodInfo GetStaticMethod(string name)
    {
        return typeof(NativeMethods).GetMethod(name,
            BindingFlags.Static | BindingFlags.NonPublic)!;
    }

    private static void AssertLibraryImport(MethodInfo method, string libraryName, bool? setLastError)
    {
        var attr = method.GetCustomAttribute<LibraryImportAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(libraryName, attr.LibraryName);
        if (setLastError.HasValue)
            Assert.Equal(setLastError.Value, attr.SetLastError);
    }
}
