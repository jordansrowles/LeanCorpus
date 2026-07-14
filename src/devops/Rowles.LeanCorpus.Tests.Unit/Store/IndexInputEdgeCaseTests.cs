using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Store;

/// <summary>
/// Edge-case and EOF coverage for <see cref="IndexInput"/> targeting branches
/// not reached by the primary test files:
/// <list type="bullet">
///   <item>EOF throws in ref and non-ref overloads for all primitive readers</item>
///   <item><see cref="IndexInput.ReadVarIntFast()"/> all five unrolled byte-length paths</item>
///   <item>VarInt overflow beyond <see cref="int.MaxValue"/></item>
///   <item>Corrupt/truncated UTF-8 via <c>Utf8ByteCount</c></item>
///   <item>Heap-allocation path for charCount &gt; 256 in <c>ReadUtf8String</c> and <c>CompareCharsAndAdvance</c></item>
///   <item><see cref="IndexInput.Prefetch()"/> on empty and non-empty files</item>
/// </list>
/// </summary>
[Trait("Category", "Store")]
[Trait("Category", "UnitTest")]
public sealed class IndexInputEdgeCaseTests : IDisposable
{
    private readonly string _dir;

    public IndexInputEdgeCaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_iie_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private string WriteBytes(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── ReadByte(ref) EOF ─────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadByte(ref) At EOF Throws EndOfStreamException")]
    public void ReadByte_Ref_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01]);
        using var input = new IndexInput(path);

        long cursor = 1; // already at EOF
        Assert.Throws<EndOfStreamException>(() => input.ReadByte(ref cursor));
    }

    // ── ReadSpan EOF ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadSpan At EOF Throws EndOfStreamException")]
    public void ReadSpan_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02]);
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadSpan(3));
    }

    [Fact(DisplayName = "IndexInput: ReadSpan(ref) At EOF Throws EndOfStreamException")]
    public void ReadSpan_Ref_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadSpan(3, ref cursor));
    }

    // ── ReadInt32Array EOF ────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadInt32Array At EOF Throws EndOfStreamException")]
    public void ReadInt32Array_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03]); // 3 bytes – not enough for one int
        using var input = new IndexInput(path);

        var dest = new int[1];
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32Array(dest, 1));
    }

    [Fact(DisplayName = "IndexInput: ReadInt32Array(ref) At EOF Throws EndOfStreamException")]
    public void ReadInt32Array_Ref_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03]);
        using var input = new IndexInput(path);

        var dest = new int[1];
        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt32Array(dest, 1, ref cursor));
    }

    // ── ReadInt64 EOF ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadInt64 At EOF Throws EndOfStreamException")]
    public void ReadInt64_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]); // 7 bytes
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadInt64());
    }

    [Fact(DisplayName = "IndexInput: ReadInt64(ref) At EOF Throws EndOfStreamException")]
    public void ReadInt64_Ref_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadInt64(ref cursor));
    }

    // ── ReadSingle EOF ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadSingle At EOF Throws EndOfStreamException")]
    public void ReadSingle_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03]); // 3 bytes – not enough for a float
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadSingle());
    }

    [Fact(DisplayName = "IndexInput: ReadSingle(ref) At EOF Throws EndOfStreamException")]
    public void ReadSingle_Ref_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadSingle(ref cursor));
    }

    // ── ReadDouble EOF ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadDouble At EOF Throws EndOfStreamException")]
    public void ReadDouble_AtEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadDouble());
    }

    // ── ReadLengthPrefixedString EOF ──────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString With No Length Byte Throws EndOfStreamException")]
    public void ReadLengthPrefixedString_EmptyFile_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([]); // empty
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadLengthPrefixedString());
    }

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString Body Truncated Throws EndOfStreamException")]
    public void ReadLengthPrefixedString_BodyTruncated_ThrowsEndOfStreamException()
    {
        // Length prefix = 5 (single byte 0x05), followed by only 2 body bytes.
        var path = WriteBytes([0x05, 0x41, 0x42]);
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadLengthPrefixedString());
    }

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString(ref) With No Length Byte Throws EndOfStreamException")]
    public void ReadLengthPrefixedString_Ref_EmptyFile_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadLengthPrefixedString(ref cursor));
    }

    [Fact(DisplayName = "IndexInput: ReadLengthPrefixedString(ref) Body Truncated Throws EndOfStreamException")]
    public void ReadLengthPrefixedString_Ref_BodyTruncated_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x05, 0x41, 0x42]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadLengthPrefixedString(ref cursor));
    }

    // ── ReadUtf8String heap-alloc path ────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadUtf8String With charCount Greater Than 256 Uses Heap Allocation")]
    public void ReadUtf8String_CharCountOver256_UsesHeapAlloc()
    {
        var text = new string('A', 257);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var path = WriteBytes(bytes);

        using var input = new IndexInput(path);
        string result = input.ReadUtf8String(257);

        Assert.Equal(text, result);
    }

    // ── ReadUtf8String 3-byte and 4-byte UTF-8 sequences ─────────────────────

    [Fact(DisplayName = "IndexInput: ReadUtf8String Three-Byte UTF-8 Sequence Decodes Correctly")]
    public void ReadUtf8String_ThreeByteSequence_DecodesCorrectly()
    {
        // U+20AC = € = 0xE2 0x82 0xAC
        var path = WriteBytes([0xE2, 0x82, 0xAC]);
        using var input = new IndexInput(path);

        string result = input.ReadUtf8String(1);
        Assert.Equal("€", result);
    }

    [Fact(DisplayName = "IndexInput: ReadUtf8String Four-Byte UTF-8 Sequence Decodes Correctly")]
    public void ReadUtf8String_FourByteSequence_DecodesCorrectly()
    {
        // U+1F600 = 😀 = 0xF0 0x9F 0x98 0x80 (2 UTF-16 chars / surrogates)
        var path = WriteBytes([0xF0, 0x9F, 0x98, 0x80]);
        using var input = new IndexInput(path);

        string result = input.ReadUtf8String(2); // charCount=2 for a surrogate pair
        Assert.Equal("😀", result);
    }

    // ── CompareCharsAndAdvance heap-alloc path ────────────────────────────────

    [Fact(DisplayName = "IndexInput: CompareCharsAndAdvance With charCount Greater Than 256 Uses Heap Allocation")]
    public void CompareCharsAndAdvance_CharCountOver256_UsesHeapAlloc()
    {
        var text = new string('Z', 257);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var path = WriteBytes(bytes);

        using var input = new IndexInput(path);
        int result = input.CompareCharsAndAdvance(257, text.AsSpan());

        Assert.Equal(0, result);
    }

    // ── Corrupt UTF-8 via Utf8ByteCount ───────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadUtf8String Truncated Two-Byte Sequence Throws InvalidDataException")]
    public void ReadUtf8String_TruncatedTwoByteSequence_ThrowsInvalidDataException()
    {
        // Only the first byte of a 2-byte sequence; seqLen=2 > remaining=1.
        var path = WriteBytes([0xC3]);
        using var input = new IndexInput(path);

        Assert.Throws<InvalidDataException>(() => input.ReadUtf8String(1));
    }

    [Fact(DisplayName = "IndexInput: ReadUtf8String All Bytes Consumed Before All Chars Read Throws InvalidDataException")]
    public void ReadUtf8String_AllBytesConsumedBeforeCharsRead_ThrowsInvalidDataException()
    {
        // 0xC3 0xA9 = é (1 char). Asking for 2 chars causes bytes >= maxBytes on second iteration.
        var path = WriteBytes([0xC3, 0xA9]);
        using var input = new IndexInput(path);

        Assert.Throws<InvalidDataException>(() => input.ReadUtf8String(2));
    }

    [Fact(DisplayName = "IndexInput: ReadUtf8String ASCII In Slow Path Then BytesExhausted Throws InvalidDataException")]
    public void ReadUtf8String_AsciiInSlowPath_BytesExhausted_ThrowsInvalidDataException()
    {
        // charCount(2) > maxBytes(1) bypasses fast path. ASCII slow branch runs once,
        // then bytes >= maxBytes → ThrowCorruptUtf8.
        var path = WriteBytes([0x41]); // 'A'
        using var input = new IndexInput(path);

        Assert.Throws<InvalidDataException>(() => input.ReadUtf8String(2));
    }

    // ── ReadVarInt EOF and overflow ───────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadVarInt Mid-Decode EOF Throws EndOfStreamException")]
    public void ReadVarInt_MidDecodeEof_ThrowsEndOfStreamException()
    {
        // 0x80 = continuation bit set; next byte read hits EOF.
        var path = WriteBytes([0x80]);
        using var input = new IndexInput(path);

        Assert.Throws<EndOfStreamException>(() => input.ReadVarInt());
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt Result Exceeds Int32 Range Throws InvalidDataException")]
    public void ReadVarInt_ExceedsInt32Range_ThrowsInvalidDataException()
    {
        // Encodes uint 0x80000000 (= int.MaxValue + 1) as 5-byte VarInt.
        // p[4] = 0x08: (8 & 0x7F) << 28 = 0x80000000 > int.MaxValue.
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x08]);
        using var input = new IndexInput(path);

        Assert.Throws<InvalidDataException>(() => input.ReadVarInt());
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt(ref) Mid-Decode EOF Throws EndOfStreamException")]
    public void ReadVarInt_Ref_MidDecodeEof_ThrowsEndOfStreamException()
    {
        var path = WriteBytes([0x80]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<EndOfStreamException>(() => input.ReadVarInt(ref cursor));
    }

    [Fact(DisplayName = "IndexInput: ReadVarInt(ref) Result Exceeds Int32 Range Throws InvalidDataException")]
    public void ReadVarInt_Ref_ExceedsInt32Range_ThrowsInvalidDataException()
    {
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x08]);
        using var input = new IndexInput(path);

        long cursor = 0;
        Assert.Throws<InvalidDataException>(() => input.ReadVarInt(ref cursor));
    }

    // ── ReadVarIntFast – all 5 unrolled paths ─────────────────────────────────

    [Fact(DisplayName = "IndexInput: ReadVarIntFast 1-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_OneByte_UsesUnrolledPath()
    {
        // value=42 (< 0x80); pad to 5 bytes so unrolled path is taken.
        var path = WriteBytes([0x2A, 0x00, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);

        Assert.Equal(42, input.ReadVarIntFast());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast 2-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_TwoByte_UsesUnrolledPath()
    {
        // value=300: [0xAC, 0x02, ...] – p[0]=0xAC >= 0x80, p[1]=0x02 < 0x80.
        var path = WriteBytes([0xAC, 0x02, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);

        Assert.Equal(300, input.ReadVarIntFast());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast 3-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_ThreeByte_UsesUnrolledPath()
    {
        // value=16384=0x4000: [0x80, 0x80, 0x01, ...] – bit 14 set.
        var path = WriteBytes([0x80, 0x80, 0x01, 0x00, 0x00]);
        using var input = new IndexInput(path);

        Assert.Equal(16384, input.ReadVarIntFast());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast 4-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_FourByte_UsesUnrolledPath()
    {
        // value=2097152=0x200000: bit 21 set.
        var path = WriteBytes([0x80, 0x80, 0x80, 0x01, 0x00]);
        using var input = new IndexInput(path);

        Assert.Equal(2097152, input.ReadVarIntFast());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast 5-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_FiveByte_UsesUnrolledPath()
    {
        // value=268435456=0x10000000: bit 28 set; p[3]=0x80 >= 0x80, p[4]=0x01.
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x01]);
        using var input = new IndexInput(path);

        Assert.Equal(268435456, input.ReadVarIntFast());
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast Falls Back To ReadVarInt For Short Files")]
    public void ReadVarIntFast_FallbackPath_ShortFile()
    {
        // 4-byte file: position+5 > length, so falls back to ReadVarInt().
        var path = WriteBytes([0x2A, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);

        Assert.Equal(42, input.ReadVarIntFast());
    }

    // ── ReadVarIntFast(ref) – all 5 unrolled paths ───────────────────────────

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) 1-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_Ref_OneByte_UsesUnrolledPath()
    {
        var path = WriteBytes([0x2A, 0x00, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(42, input.ReadVarIntFast(ref cursor));
        Assert.Equal(1, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) 2-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_Ref_TwoByte_UsesUnrolledPath()
    {
        var path = WriteBytes([0xAC, 0x02, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(300, input.ReadVarIntFast(ref cursor));
        Assert.Equal(2, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) 3-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_Ref_ThreeByte_UsesUnrolledPath()
    {
        var path = WriteBytes([0x80, 0x80, 0x01, 0x00, 0x00]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(16384, input.ReadVarIntFast(ref cursor));
        Assert.Equal(3, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) 4-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_Ref_FourByte_UsesUnrolledPath()
    {
        var path = WriteBytes([0x80, 0x80, 0x80, 0x01, 0x00]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(2097152, input.ReadVarIntFast(ref cursor));
        Assert.Equal(4, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) 5-Byte Value Uses Unrolled Path")]
    public void ReadVarIntFast_Ref_FiveByte_UsesUnrolledPath()
    {
        var path = WriteBytes([0x80, 0x80, 0x80, 0x80, 0x01]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(268435456, input.ReadVarIntFast(ref cursor));
        Assert.Equal(5, cursor);
    }

    [Fact(DisplayName = "IndexInput: ReadVarIntFast(ref) Falls Back To ReadVarInt For Short Files")]
    public void ReadVarIntFast_Ref_FallbackPath_ShortFile()
    {
        var path = WriteBytes([0x2A, 0x00, 0x00, 0x00]);
        using var input = new IndexInput(path);
        long cursor = 0;
        Assert.Equal(42, input.ReadVarIntFast(ref cursor));
    }

    // ── Prefetch ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IndexInput: Prefetch On Empty File Does Not Throw")]
    public void Prefetch_EmptyFile_DoesNotThrow()
    {
        var path = WriteBytes([]);
        using var input = new IndexInput(path);
        input.Prefetch(); // length == 0 → early return
    }

    [Fact(DisplayName = "IndexInput: Prefetch On Non-Empty File Does Not Throw")]
    public void Prefetch_NonEmptyFile_DoesNotThrow()
    {
        var path = WriteBytes([0x01, 0x02, 0x03, 0x04]);
        using var input = new IndexInput(path);
        input.Prefetch(); // exercises the OS-specific path
    }
}
