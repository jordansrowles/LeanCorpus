using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Store;

/// <summary>
/// Gap-coverage tests for <see cref="IndexInput"/> targeting the ref-parameter
/// overloads (stateless cursor variants) and edge cases not yet covered:
/// <see cref="IndexInput.ReadByte(ref long)"/>,
/// <see cref="IndexInput.ReadBoolean(ref long)"/>, 
/// <see cref="IndexInput.ReadSpan(int, ref long)"/>,
/// <see cref="IndexInput.ReadInt32(ref long)"/>,
/// <see cref="IndexInput.ReadInt64(ref long)"/>,
/// <see cref="IndexInput.ReadSingle(ref long)"/>,
/// <see cref="IndexInput.ReadVarInt(ref long)"/>,
/// <see cref="IndexInput.ReadLengthPrefixedString(ref long)"/>,
/// <see cref="IndexInput.CompareUtf8BytesAndAdvance"/>,
/// <see cref="IndexInput.CompareCharsAndAdvance"/>,
/// malformed VarInt overflow.
/// </summary>
[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class IndexInputRefMethodTests : IDisposable
{
    private readonly string _dir;

    public IndexInputRefMethodTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_iir_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteBytes(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteWith(Action<BinaryWriter> write)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".dat");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        write(bw);
        return path;
    }

    // ReadByte(ref long)

    [Fact(DisplayName = "IndexInput: ReadByte(ref) Advances Caller Cursor Only")]
    public void ReadByte_Ref_AdvancesCallerCursorOnly()
    {
        var path = WriteBytes([0x0A, 0x0B]);
        using var input = new IndexInput(path);

        long cursor = 0;
        byte first = input.ReadByte(ref cursor);
        Assert.Equal(0x0A, first);
        Assert.Equal(1, cursor);
        Assert.Equal(0, input.Position);
    }

    // ReadBoolean(ref long)

    [Fact(DisplayName = "IndexInput: ReadBoolean(ref) Advances Caller Cursor Only")]
    public void ReadBoolean_Ref_AdvancesCallerCursorOnly()
    {
        var path = WriteWith(bw => { bw.Write(true); bw.Write(false); });
        using var input = new IndexInput(path);

        long cursor = 0;
        bool first = input.ReadBoolean(ref cursor);
        Assert.True(first);
        Assert.Equal(1, cursor);
        Assert.Equal(0, input.Position); // instance cursor unchanged

        bool second = input.ReadBoolean(ref cursor);
        Assert.False(second);
        Assert.Equal(2, cursor);
    }

    // ReadSpan(int, ref long)

    [Fact(DisplayName = "IndexInput: ReadSpan(ref) Advances Caller Cursor Only")]
    public void ReadSpan_Ref_AdvancesCallerCursorOnly()
    {
        var path = WriteBytes([0x01, 0x02, 0x03]);
        using var input = new IndexInput(path);

        long cursor = 1;
        var span = input.ReadSpan(2, ref cursor);
        Assert.True(span.SequenceEqual(new byte[] { 0x02, 0x03 }));
        Assert.Equal(3, cursor);
        Assert.Equal(0, input.Position);
    }

    // ReadInt32(ref long)

    [Fact(DisplayName = "IndexInput: ReadInt32(ref) Returns Correct Value")]
    public void ReadInt32_Ref_ReturnsCorrectValue()
    {
        var path = WriteWith(bw => bw.Write(0x12345678));
        using var input = new IndexInput(path);

        long cursor = 0;
        int val = input.ReadInt32(ref cursor);
        Assert.Equal(0x12345678, val);
        Assert.Equal(4, cursor);
        Assert.Equal(0, input.Position);
    }

    [Fact(DisplayName = "IndexInput: ReadInt32(ref) Throws At End Of Stream")]
    public void ReadInt32_Ref_ThrowsAtEndOfStream()
    {
        var path = WriteBytes([0x01, 0x02]); // Only 2 bytes.
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32(ref cursor));
    }

    // ReadInt64(ref long)

    [Fact(DisplayName = "IndexInput: ReadInt64(ref) Returns Correct Value")]
    public void ReadInt64_Ref_ReturnsCorrectValue()
    {
        var path = WriteWith(bw => bw.Write(0x0102030405060708L));
        using var input = new IndexInput(path);

        long cursor = 0;
        long val = input.ReadInt64(ref cursor);
        Assert.Equal(0x0102030405060708L, val);
        Assert.Equal(8, cursor);
    }

    // ReadSingle(ref long)

    [Fact(DisplayName = "IndexInput: ReadSingle(ref) Returns Correct Value")]
    public void ReadSingle_Ref_ReturnsCorrectValue()
    {
        var path = WriteWith(bw => bw.Write(3.14f));
        using var input = new IndexInput(path);

        long cursor = 0;
        float val = input.ReadSingle(ref cursor);
        Assert.Equal(3.14f, val, 4);
        Assert.Equal(4, cursor);
    }

    // ReadVarInt(ref long)

    [Fact(DisplayName = "IndexInput: ReadVarInt(ref) Returns Single-Byte Value")]
    public void ReadVarInt_Ref_SingleByte_ReturnsCorrectValue()
    {
        var path = WriteBytes([42]);
        using var input = new IndexInput(path);

        long cursor = 0;
        int val = input.ReadVarInt(ref cursor);
        Assert.Equal(42, val);
        Assert.Equal(1, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt(ref) Returns Multi-Byte Value")]
    public void ReadVarInt_Ref_MultiBytes_ReturnsCorrectValue()
    {
        // Encode 300 = 0b100101100 as 2 VarInt bytes: 0xAC 0x02.
        var path = WriteBytes([0xAC, 0x02]);
        using var input = new IndexInput(path);

        long cursor = 0;
        int val = input.ReadVarInt(ref cursor);
        Assert.Equal(300, val);
        Assert.Equal(2, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt Malformed Too Many Continuation Bytes Throws")]
    public void ReadVarInt_MalformedTooManyContinuationBytes_Throws()
    {
        // 6 bytes with continuation bit set, so shift reaches >= 35.
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);
        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() => input.ReadVarInt());
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt(ref) Malformed Too Many Continuation Bytes Throws")]
    public void ReadVarInt_Ref_MalformedTooManyContinuationBytes_Throws()
    {
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<InvalidDataException>(() => input.ReadVarInt(ref cursor));
    }

    // ReadLengthPrefixedString(ref long)

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString(ref) Returns Correct String")]
    public void ReadLengthPrefixedString_Ref_ReturnsCorrectString()
    {
        var path = WriteWith(bw =>
        {
            bw.Write(0);           // Padding int to verify cursor independence.
            bw.Write("hello");
        });
        using var input = new IndexInput(path);

        long cursor = 4; // Skip the padding int.
        string result = input.ReadLengthPrefixedString(ref cursor);
        Assert.Equal("hello", result);
        Assert.Equal(0, input.Position); // instance cursor unchanged
    }

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString(ref) Empty String Returns Empty")]
    public void ReadLengthPrefixedString_Ref_EmptyString_ReturnsEmpty()
    {
        var path = WriteWith(bw => bw.Write(string.Empty));
        using var input = new IndexInput(path);

        long cursor = 0;
        string result = input.ReadLengthPrefixedString(ref cursor);
        Assert.Equal(string.Empty, result);
    }

    // CompareUtf8BytesAndAdvance

    [Fact(DisplayName = "IndexInput: CompareUtf8BytesAndAdvance Equal Returns Zero")]
    public void CompareUtf8BytesAndAdvance_EqualBytes_ReturnsZero()
    {
        var text = "hello"u8.ToArray();
        var path = WriteBytes(text);
        using var input = new IndexInput(path);

        // Compare 5 chars from position 0 against the same UTF-8 bytes.
        int result = input.CompareUtf8BytesAndAdvance(5, "hello"u8);
        Assert.Equal(0, result);
        Assert.Equal(5, input.Position);
    }

    [Fact(DisplayName = "IndexInput: CompareUtf8BytesAndAdvance Less Returns Negative")]
    public void CompareUtf8BytesAndAdvance_FileLessThanTerm_ReturnsNegative()
    {
        var path = WriteBytes("apple"u8.ToArray());
        using var input = new IndexInput(path);

        int result = input.CompareUtf8BytesAndAdvance(5, "zebra"u8);
        Assert.True(result < 0);
    }

    [Fact(DisplayName = "IndexInput: CompareUtf8BytesAndAdvance Greater Returns Positive")]
    public void CompareUtf8BytesAndAdvance_FileGreaterThanTerm_ReturnsPositive()
    {
        var path = WriteBytes("zebra"u8.ToArray());
        using var input = new IndexInput(path);

        int result = input.CompareUtf8BytesAndAdvance(5, "apple"u8);
        Assert.True(result > 0);
    }

    // CompareCharsAndAdvance

    [Fact(DisplayName = "IndexInput: CompareCharsAndAdvance Equal Returns Zero")]
    public void CompareCharsAndAdvance_EqualChars_ReturnsZero()
    {
        var path = WriteBytes("hello"u8.ToArray());
        using var input = new IndexInput(path);

        int result = input.CompareCharsAndAdvance(5, "hello".AsSpan());
        Assert.Equal(0, result);
        Assert.Equal(5, input.Position);
    }

    [Fact(DisplayName = "IndexInput: CompareCharsAndAdvance Less Returns Negative")]
    public void CompareCharsAndAdvance_FileLessThanTerm_ReturnsNegative()
    {
        var path = WriteBytes("aaa"u8.ToArray());
        using var input = new IndexInput(path);

        int result = input.CompareCharsAndAdvance(3, "zzz".AsSpan());
        Assert.True(result < 0);
    }

    // ReadDouble

    [Fact(DisplayName = "IndexInput: ReadDouble Returns Correct Value")]
    public void ReadDouble_ReturnsCorrectValue()
    {
        var path = WriteWith(bw => bw.Write(Math.PI));
        using var input = new IndexInput(path);
        Assert.Equal(Math.PI, input.ReadDouble(), 10);
    }

    // ReadUtf8String

    [Fact(DisplayName = "IndexInput: ReadUtf8String Returns Correct String")]
    public void ReadUtf8String_ReturnsCorrectString()
    {
        var path = WriteBytes("hello"u8.ToArray());
        using var input = new IndexInput(path);
        string result = input.ReadUtf8String(5);
        Assert.Equal("hello", result);
    }

    [Fact(DisplayName = "IndexInput: ReadUtf8String Multi-Byte Chars Returns Correct String")]
    public void ReadUtf8String_MultiByteChars_ReturnsCorrectString()
    {
        // U+00E9 is 0xC3 0xA9 in UTF-8 (2 bytes, 1 char).
        var path = WriteBytes([0xC3, 0xA9]);
        using var input = new IndexInput(path);
        string result = input.ReadUtf8String(1);
        Assert.Equal("\u00E9", result);
    }
}
