namespace Rowles.LeanCorpus.Tests.Unit.Store;

/// <summary>
/// Smoke tests for <see cref="NativeMethods"/> P/Invoke declarations.
/// </summary>
[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class NativeMethodsTests
{
    [Fact(DisplayName = "NativeMethods: POSIX_FADV_DONTNEED equals 4")]
    public void PosixFadvDontneed_Equals4()
    {
        Assert.Equal(4, NativeMethods.POSIX_FADV_DONTNEED);
    }

    [Fact(DisplayName = "NativeMethods: madvise declaration exists")]
    public void Madvise_DeclarationExists()
    {
        var method = typeof(NativeMethods).GetMethod("madvise",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(3, method.GetParameters().Length);
    }

    [Fact(DisplayName = "NativeMethods: posix_fadvise declaration exists")]
    public void PosixFadvise_DeclarationExists()
    {
        var method = typeof(NativeMethods).GetMethod("posix_fadvise",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(4, method.GetParameters().Length);
    }

    [Fact(DisplayName = "NativeMethods: PrefetchVirtualMemory declaration exists")]
    public void PrefetchVirtualMemory_DeclarationExists()
    {
        var method = typeof(NativeMethods).GetMethod("PrefetchVirtualMemory",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact(DisplayName = "NativeMethods: GetCurrentProcess declaration exists")]
    public void GetCurrentProcess_DeclarationExists()
    {
        var method = typeof(NativeMethods).GetMethod("GetCurrentProcess",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact(DisplayName = "NativeMethods: WIN32_MEMORY_RANGE_ENTRY has correct fields")]
    public void Win32MemoryRangeEntry_HasCorrectFields()
    {
        var type = typeof(NativeMethods).GetNestedType("WIN32_MEMORY_RANGE_ENTRY",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(type);
        Assert.NotNull(type.GetField("VirtualAddress"));
        Assert.NotNull(type.GetField("NumberOfBytes"));
    }
}
