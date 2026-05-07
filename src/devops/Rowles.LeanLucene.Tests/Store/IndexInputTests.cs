using System.Runtime.InteropServices;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Store;

/// <summary>
/// Unit tests for <see cref="IndexInput"/> covering all primitive readers,
/// VarInt decoding, string decoding, seek/position, and end-of-stream guards.
/// </summary>
[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class IndexInputTests : IDisposable
{
    private readonly string _dir;

    public IndexInputTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_input_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteFile(string name, byte[] data)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] LittleEndianInt32(int value)
    {
        var buf = new byte[4];
        MemoryMarshal.Write(buf, in value);
        return buf;
    }

    private static byte[] LittleEndianInt64(long value)
    {
        var buf = new byte[8];
        MemoryMarshal.Write(buf, in value);
        return buf;
    }

    private static byte[] LittleEndianSingle(float value)
    {
        var buf = new byte[4];
        MemoryMarshal.Write(buf, in value);
        return buf;
    }

    private static byte[] LittleEndianDouble(double value)
    {
        var buf = new byte[8];
        MemoryMarshal.Write(buf, in value);
        return buf;
    }

    // ── Empty file ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: Empty File Has Length Zero")]
    public void EmptyFile_LengthIsZero()
    {
        var path = WriteFile("empty.bin", []);
        using var input = new IndexInput(path);
        Assert.Equal(0L, input.Length);
        Assert.Equal(0L, input.Position);
    }

    [Fact(DisplayName = "IndexInput: ReadByte On Empty File Throws EndOfStream")]
    public void ReadByte_EmptyFile_ThrowsEndOfStream()
    {
        var path = WriteFile("empty2.bin", []);
        using var input = new IndexInput(path);
        Assert.Throws<EndOfStreamException>(() => input.ReadByte());
    }

    // ── ReadByte ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadByte Returns Correct Value")]
    public void ReadByte_ReturnsValue()
    {
        var path = WriteFile("byte.bin", [0xAB, 0xCD]);
        using var input = new IndexInput(path);
        Assert.Equal(0xAB, input.ReadByte());
        Assert.Equal(0xCD, input.ReadByte());
        Assert.Equal(2L, input.Position);
    }

    [Fact(DisplayName = "IndexInput: ReadByte Past End Throws EndOfStream")]
    public void ReadByte_PastEnd_Throws()
    {
        var path = WriteFile("byte2.bin", [0x01]);
        using var input = new IndexInput(path);
        input.ReadByte();
        Assert.Throws<EndOfStreamException>(() => input.ReadByte());
    }

    [Fact(DisplayName = "IndexInput: ReadByte Ref-Position Leaves Cursor Unchanged")]
    public void ReadByte_RefPosition_LeavesPositionUnchanged()
    {
        var path = WriteFile("byte_ref.bin", [0x77, 0x88]);
        using var input = new IndexInput(path);
        long pos = 1L;
        var b = input.ReadByte(ref pos);
        Assert.Equal(0x88, b);
        Assert.Equal(2L, pos);
        Assert.Equal(0L, input.Position);
    }

    // ── ReadBoolean ───────────────────────────────────────────────────────────

    [Theory(DisplayName = "IndexInput: ReadBoolean Returns Correct Value")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(255, true)]
    public void ReadBoolean_ReturnsCorrectValue(byte raw, bool expected)
    {
        var path = WriteFile($"bool_{raw}.bin", [raw]);
        using var input = new IndexInput(path);
        Assert.Equal(expected, input.ReadBoolean());
    }

    // ── ReadInt32 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadInt32 Decodes Little-Endian")]
    public void ReadInt32_DecodesLittleEndian()
    {
        var path = WriteFile("i32.bin", LittleEndianInt32(0x12345678));
        using var input = new IndexInput(path);
        Assert.Equal(0x12345678, input.ReadInt32());
    }

    [Fact(DisplayName = "IndexInput: ReadInt32 Ref-Position Leaves Cursor Unchanged")]
    public void ReadInt32_RefPosition_LeavesPositionUnchanged()
    {
        var path = WriteFile("i32_ref.bin", LittleEndianInt32(42));
        using var input = new IndexInput(path);
        long pos = 0L;
        var value = input.ReadInt32(ref pos);
        Assert.Equal(42, value);
        Assert.Equal(4L, pos);
        Assert.Equal(0L, input.Position);
    }

    [Fact(DisplayName = "IndexInput: ReadInt32 Past End Throws EndOfStream")]
    public void ReadInt32_PastEnd_Throws()
    {
        var path = WriteFile("i32_eof.bin", [0x01, 0x02]);
        using var input = new IndexInput(path);
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32());
    }

    // ── ReadInt32Array ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadInt32Array Reads Multiple Values")]
    public void ReadInt32Array_ReadsValues()
    {
        var bytes = new byte[12];
        LittleEndianInt32(10).CopyTo(bytes, 0);
        LittleEndianInt32(20).CopyTo(bytes, 4);
        LittleEndianInt32(30).CopyTo(bytes, 8);
        var path = WriteFile("i32arr.bin", bytes);

        using var input = new IndexInput(path);
        var dest = new int[3];
        input.ReadInt32Array(dest, 3);
        Assert.Equal([10, 20, 30], dest);
    }

    // ── ReadInt64 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadInt64 Decodes Little-Endian")]
    public void ReadInt64_DecodesLittleEndian()
    {
        var path = WriteFile("i64.bin", LittleEndianInt64(long.MaxValue));
        using var input = new IndexInput(path);
        Assert.Equal(long.MaxValue, input.ReadInt64());
    }

    [Fact(DisplayName = "IndexInput: ReadInt64 Ref-Position Leaves Cursor Unchanged")]
    public void ReadInt64_RefPosition_LeavesPositionUnchanged()
    {
        var path = WriteFile("i64_ref.bin", LittleEndianInt64(999L));
        using var input = new IndexInput(path);
        long pos = 0L;
        Assert.Equal(999L, input.ReadInt64(ref pos));
        Assert.Equal(0L, input.Position);
    }

    // ── ReadSingle ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadSingle Decodes Value")]
    public void ReadSingle_DecodesValue()
    {
        var path = WriteFile("f32.bin", LittleEndianSingle(3.14f));
        using var input = new IndexInput(path);
        Assert.Equal(3.14f, input.ReadSingle(), 5);
    }

    // ── ReadDouble ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadDouble Decodes Value")]
    public void ReadDouble_DecodesValue()
    {
        var path = WriteFile("f64.bin", LittleEndianDouble(2.718281828));
        using var input = new IndexInput(path);
        Assert.Equal(2.718281828, input.ReadDouble(), 9);
    }

    // ── ReadBytes / ReadSpan ──────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadBytes Returns Correct Bytes")]
    public void ReadBytes_ReturnsCorrectBytes()
    {
        var path = WriteFile("bytes.bin", [10, 20, 30, 40]);
        using var input = new IndexInput(path);
        var result = input.ReadBytes(3);
        Assert.Equal([10, 20, 30], result);
        Assert.Equal(3L, input.Position);
    }

    [Fact(DisplayName = "IndexInput: ReadBytes Past End Throws EndOfStream")]
    public void ReadBytes_PastEnd_Throws()
    {
        var path = WriteFile("bytes_eof.bin", [1, 2]);
        using var input = new IndexInput(path);
        Assert.Throws<EndOfStreamException>(() => input.ReadBytes(5));
    }

    [Fact(DisplayName = "IndexInput: ReadSpan Returns Correct Span")]
    public void ReadSpan_ReturnsCorrectSpan()
    {
        var path = WriteFile("span.bin", [5, 6, 7, 8]);
        using var input = new IndexInput(path);
        var span = input.ReadSpan(2);
        Assert.Equal([5, 6], span.ToArray());
    }

    // ── Seek / Position ───────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: Seek Changes Position")]
    public void Seek_ChangesPosition()
    {
        var path = WriteFile("seek.bin", [1, 2, 3, 4, 5]);
        using var input = new IndexInput(path);
        input.Seek(3);
        Assert.Equal(3L, input.Position);
        Assert.Equal(4, input.ReadByte());
    }

    // ── VarInt ────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "IndexInput: ReadVarInt Decodes Single-Byte Values")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    public void ReadVarInt_SingleByte(int expected)
    {
        var path = WriteFile($"varint_{expected}.bin", [(byte)expected]);
        using var input = new IndexInput(path);
        Assert.Equal(expected, input.ReadVarInt());
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt Decodes Multi-Byte Value")]
    public void ReadVarInt_MultiByte()
    {
        // 300 = 0b10101100_00000010 in LEB128
        var path = WriteFile("varint_300.bin", [0xAC, 0x02]);
        using var input = new IndexInput(path);
        Assert.Equal(300, input.ReadVarInt());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast Matches ReadVarInt")]
    public void ReadVarIntFast_MatchesReadVarInt()
    {
        // Write a 5-byte LEB128 value so the fast path is exercised.
        var path = WriteFile("varint_fast.bin", [0x80, 0x80, 0x80, 0x80, 0x01]);
        using var input = new IndexInput(path);
        long pos = 0L;
        var fastResult = input.ReadVarIntFast(ref pos);
        input.Seek(0);
        var safeResult = input.ReadVarInt();
        Assert.Equal(safeResult, fastResult);
    }

    // ── ReadLengthPrefixedString ──────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString Decodes BinaryWriter String")]
    public void ReadLengthPrefixedString_DecodesString()
    {
        const string text = "hello";
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            bw.Write(text);
        var path = WriteFile("lps.bin", ms.ToArray());

        using var input = new IndexInput(path);
        Assert.Equal(text, input.ReadLengthPrefixedString());
    }

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString Empty Returns Empty String")]
    public void ReadLengthPrefixedString_Empty_ReturnsEmpty()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            bw.Write(string.Empty);
        var path = WriteFile("lps_empty.bin", ms.ToArray());

        using var input = new IndexInput(path);
        Assert.Equal(string.Empty, input.ReadLengthPrefixedString());
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: Dispose Is Idempotent")]
    public void Dispose_IsIdempotent()
    {
        var path = WriteFile("disp.bin", [1, 2, 3]);
        var input = new IndexInput(path);
        input.Dispose();
        input.Dispose(); // second dispose must not throw
    }

    [Fact(DisplayName = "IndexInput: ReadByte After Dispose Throws")]
    public void ReadByte_AfterDispose_Throws()
    {
        var path = WriteFile("disp2.bin", [1, 2, 3]);
        var input = new IndexInput(path);
        input.Dispose();
        // IndexInput.Dispose sets _ptr = null; ReadByte will NullReferenceException (not ODE)
        Assert.ThrowsAny<Exception>(() => input.ReadByte());
    }

    // ── Prefetch ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: Prefetch Does Not Throw")]
    public void Prefetch_DoesNotThrow()
    {
        var path = WriteFile("prefetch.bin", [1, 2, 3, 4]);
        using var input = new IndexInput(path);
        input.Prefetch(); // advisory only — must not throw
    }
}
