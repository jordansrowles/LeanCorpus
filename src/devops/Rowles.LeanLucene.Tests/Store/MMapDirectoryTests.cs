using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Store;

/// <summary>
/// Contains unit tests for MMap Directory.
/// </summary>
[Trait("Category", "Store")]
public sealed class MMapDirectoryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MMapDirectoryTests(TestDirectoryFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Verifies the MMap Directory: Open Input Returns Readable Span scenario.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Open Input Returns Readable Span")]
    public void MMapDirectory_OpenInput_ReturnsReadableSpan()
    {
        // Write known bytes to a file, open via MMapDirectory,
        // assert the returned IndexInput span contains the exact bytes.
        var dir = new MMapDirectory(_fixture.Path);
        var fileName = "test_readable.bin";
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };

        using (var output = dir.CreateOutput(fileName))
        {
            output.WriteBytes(expected);
        }

        using var input = dir.OpenInput(fileName);
        var actual = input.ReadBytes(expected.Length);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies the Index Input: Read Unaligned Returns Correct Primitive scenario.
    /// </summary>
    [Fact(DisplayName = "Index Input: Read Unaligned Returns Correct Primitive")]
    public void IndexInput_ReadUnaligned_ReturnsCorrectPrimitive()
    {
        // Write a float and an int at non-aligned offsets, read back via
        // Unsafe.ReadUnaligned<T>, assert values match.
        var dir = new MMapDirectory(_fixture.Path);
        var fileName = "test_unaligned.bin";
        float expectedFloat = 3.14f;
        int expectedInt = 42;

        using (var output = dir.CreateOutput(fileName))
        {
            output.WriteByte(0xFF); // padding byte to create misalignment
            output.WriteInt32(expectedInt);
            output.WriteSingle(expectedFloat);
        }

        using var input = dir.OpenInput(fileName);
        input.Seek(1); // skip padding
        var actualInt = input.ReadInt32();
        var actualFloat = input.ReadSingle();

        Assert.Equal(expectedInt, actualInt);
        Assert.Equal(expectedFloat, actualFloat);
    }

    /// <summary>
    /// Verifies the Index Output: Write And Flush Produces Durable File scenario.
    /// </summary>
    [Fact(DisplayName = "Index Output: Write And Flush Produces Durable File")]
    public void IndexOutput_WriteAndFlush_ProducesDurableFile()
    {
        // Write a sequence of bytes via IndexOutput, dispose, read back raw,
        // assert byte-for-byte equality.
        var dir = new MMapDirectory(_fixture.Path);
        var fileName = "test_durable.bin";
        var expected = new byte[256];
        for (int i = 0; i < expected.Length; i++)
            expected[i] = (byte)(i & 0xFF);

        using (var output = dir.CreateOutput(fileName))
        {
            output.WriteBytes(expected);
        }

        var filePath = System.IO.Path.Combine(_fixture.Path, fileName);
        var actual = File.ReadAllBytes(filePath);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies the Index Input: Read Int 32 Beyond File End Throws End Of Stream Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Index Input: Read Int 32 Beyond File End Throws End Of Stream Exception")]
    public void IndexInput_ReadInt32_BeyondFileEnd_ThrowsEndOfStreamException()
    {
        var dir = new MMapDirectory(_fixture.Path);
        var fileName = "test_bounds.bin";

        using (var output = dir.CreateOutput(fileName))
        {
            output.WriteByte(0xAA);
        }

        using var input = dir.OpenInput(fileName);
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32());
    }

    /// <summary>
    /// Verifies the MMap Directory: Path Traversal File Name Throws Argument Exception scenario.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Path Traversal File Name Throws Argument Exception")]
    public void MMapDirectory_PathTraversalFileName_ThrowsArgumentException()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentException>(() => dir.OpenInput("..\\..\\windows\\win.ini"));
    }

    /// <summary>
    /// Verifies the MMap Directory: Null Path Throws scenario.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Null Path Throws Argument Null Exception")]
    public void MMapDirectory_NullPath_Throws()
        => Assert.Throws<ArgumentNullException>(() => new MMapDirectory(null!));

    /// <summary>
    /// Verifies the MMap Directory: Creates Missing Directory scenario.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Creates Missing Directory")]
    public void MMapDirectory_CreatesMissingDirectory()
    {
        var path = Path.Combine(_fixture.Path, "newdir_" + Guid.NewGuid().ToString("N")[..6]);
        Assert.False(Directory.Exists(path));
        using var _ = new MMapDirectory(path);
        Assert.True(Directory.Exists(path));
    }

    /// <summary>
    /// Verifies that null file name throws in FileExists.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Null File Name Throws Argument Null Exception")]
    public void MMapDirectory_NullFileName_Throws()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentNullException>(() => dir.FileExists(null!));
    }

    /// <summary>
    /// Verifies that empty file name throws in FileExists.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Empty File Name Throws Argument Exception")]
    public void MMapDirectory_EmptyFileName_Throws()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentException>(() => dir.FileExists(string.Empty));
    }

    /// <summary>
    /// Verifies that a whitespace file name throws in FileExists.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Whitespace File Name Throws Argument Exception")]
    public void MMapDirectory_WhitespaceFileName_Throws()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentException>(() => dir.FileExists("   "));
    }

    /// <summary>
    /// Verifies that a forward-slash in a file name throws.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Forward Slash In Name Throws Argument Exception")]
    public void MMapDirectory_ForwardSlash_Throws()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentException>(() => dir.FileExists("sub/file.txt"));
    }

    /// <summary>
    /// Verifies that a control character in a file name throws.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Control Char In Name Throws Argument Exception")]
    public void MMapDirectory_ControlChar_Throws()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.Throws<ArgumentException>(() => dir.FileExists("file\x01.txt"));
    }

    /// <summary>
    /// Verifies FileExists returns false for a missing file.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: FileExists Returns False For Missing File")]
    public void MMapDirectory_FileExists_ReturnsFalse()
    {
        var dir = new MMapDirectory(_fixture.Path);
        Assert.False(dir.FileExists("definitely_missing_xyz.bin"));
    }

    /// <summary>
    /// Verifies FileExists returns true for an existing file.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: FileExists Returns True For Existing File")]
    public void MMapDirectory_FileExists_ReturnsTrue()
    {
        var dir = new MMapDirectory(_fixture.Path);
        var name = "present_" + Guid.NewGuid().ToString("N")[..6] + ".bin";
        File.WriteAllText(Path.Combine(_fixture.Path, name), "x");
        Assert.True(dir.FileExists(name));
    }

    /// <summary>
    /// Verifies ListAll returns only file names without directory path.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: ListAll Returns File Names Without Path")]
    public void MMapDirectory_ListAll_ReturnsFileNamesOnly()
    {
        var sub = Path.Combine(_fixture.Path, "listall_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(sub);
        var dir = new MMapDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "a.seg"), "x");
        File.WriteAllText(Path.Combine(sub, "b.seg"), "y");

        var files = dir.ListAll();
        Assert.Contains("a.seg", files);
        Assert.Contains("b.seg", files);
        Assert.All(files, f => Assert.Equal(f, Path.GetFileName(f)));
    }

    /// <summary>
    /// Verifies DeleteFile removes the named file from the directory.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: DeleteFile Removes The File")]
    public void MMapDirectory_DeleteFile_RemovesFile()
    {
        var sub = Path.Combine(_fixture.Path, "del_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(sub);
        var dir = new MMapDirectory(sub);
        var name = "del_me.bin";
        File.WriteAllText(Path.Combine(sub, name), "bye");

        dir.DeleteFile(name);
        Assert.False(File.Exists(Path.Combine(sub, name)));
    }

    /// <summary>
    /// Verifies that Dispose closes tracked IndexInput instances.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: Dispose Closes Tracked Inputs")]
    public void MMapDirectory_Dispose_ClosesTrackedInputs()
    {
        var sub = Path.Combine(_fixture.Path, "disp_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(sub);
        var filePath = Path.Combine(sub, "tracked.bin");
        File.WriteAllBytes(filePath, [1, 2, 3]);

        var mmap = new MMapDirectory(sub);
        var input = mmap.OpenInput("tracked.bin");
        mmap.Dispose();

        var disposedField = typeof(IndexInput).GetField("_disposed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(disposedField);
        Assert.True((bool)disposedField.GetValue(input)!);
        Assert.Throws<NullReferenceException>(() => input.ReadByte());
    }

    /// <summary>
    /// Verifies that CreateOutput after Dispose throws ObjectDisposedException.
    /// </summary>
    [Fact(DisplayName = "MMap Directory: CreateOutput After Dispose Throws ObjectDisposed")]
    public void MMapDirectory_CreateOutput_AfterDispose_Throws()
    {
        var sub = Path.Combine(_fixture.Path, "disp2_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(sub);
        var mmap = new MMapDirectory(sub);
        mmap.Dispose();
        Assert.Throws<ObjectDisposedException>(() => mmap.CreateOutput("file.dat"));
    }

    /// <summary>
    /// Verifies the Index Output: Use Pooled Buffer No Heap Allocation scenario.
    /// </summary>
    [Fact(DisplayName = "Index Output: Use Pooled Buffer No Heap Allocation")]
    [Trait("Category", "Advanced")]
    public void IndexOutput_UsePooledBuffer_NoHeapAllocation()
    {
        // Write 10 MB through IndexOutput, assert GC allocation delta
        // is below a defined threshold.
        var dir = new MMapDirectory(_fixture.Path);
        var fileName = "test_pooled.bin";
        var data = new byte[1024];
        Array.Fill<byte>(data, 0xAB);

        // Warm up
        using (var warmup = dir.CreateOutput("warmup.bin"))
        {
            warmup.WriteBytes(data);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        using (var output = dir.CreateOutput(fileName))
        {
            for (int i = 0; i < 10_240; i++)
                output.WriteBytes(data);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        long allocated = after - before;

        Assert.True(allocated < 100_000,
            $"Expected fewer than 100 KB allocated, but got {allocated / 1024.0:F1} KB");
    }
}
