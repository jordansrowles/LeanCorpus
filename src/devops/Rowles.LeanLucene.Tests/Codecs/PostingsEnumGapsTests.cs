using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Gap-coverage tests for <see cref="PostingsEnum"/>:
/// Empty sentinel, ValidateFileHeader (valid and bad header), Dispose idempotency,
/// iteration, Reset, and Advance via a small hand-written v2 postings list.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class PostingsEnumGapsTests : IDisposable
{
    private readonly string _dir;

    public PostingsEnumGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_pe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // Empty sentinel

    [Fact(DisplayName = "PostingsEnum: Empty IsExhausted")]
    public void Empty_IsExhausted()
    {
        var pe = PostingsEnum.Empty;
        Assert.True(pe.IsExhausted);
    }

    [Fact(DisplayName = "PostingsEnum: Empty DocFreq Is Zero")]
    public void Empty_DocFreq_IsZero()
    {
        Assert.Equal(0, PostingsEnum.Empty.DocFreq);
    }

    [Fact(DisplayName = "PostingsEnum: Empty MoveNext Returns False")]
    public void Empty_MoveNext_ReturnsFalse()
    {
        var pe = PostingsEnum.Empty;
        Assert.False(pe.MoveNext());
    }

    [Fact(DisplayName = "PostingsEnum: Empty Advance Returns False")]
    public void Empty_Advance_ReturnsFalse()
    {
        var pe = PostingsEnum.Empty;
        Assert.False(pe.Advance(0));
    }

    [Fact(DisplayName = "PostingsEnum: Empty Dispose Does Not Throw")]
    public void Empty_Dispose_DoesNotThrow()
    {
        var pe = PostingsEnum.Empty;
        var ex = Record.Exception(() => pe.Dispose());
        Assert.Null(ex);
    }

    [Fact(DisplayName = "PostingsEnum: Empty Dispose Is Idempotent")]
    public void Empty_Dispose_IsIdempotent()
    {
        var pe = PostingsEnum.Empty;
        pe.Dispose();
        var ex = Record.Exception(() => pe.Dispose());
        Assert.Null(ex);
    }

    [Fact(DisplayName = "PostingsEnum: Empty GetCurrentPositions Returns Empty Span")]
    public void Empty_GetCurrentPositions_ReturnsEmptySpan()
    {
        var pe = PostingsEnum.Empty;
        Assert.Equal(0, pe.GetCurrentPositions().Length);
    }

    // ValidateFileHeader

    [Fact(DisplayName = "PostingsEnum: ValidateFileHeader Valid Header Returns Version")]
    public void ValidateFileHeader_ValidHeader_ReturnsVersion()
    {
        var path = Path.Combine(_dir, "test_valid.pos");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            // Magic: 0x4C4C4E31 (little-endian).
            bw.Write(CodecConstants.Magic);
            // Version: PostingsVersion (3).
            bw.Write(CodecConstants.PostingsVersion);
        }

        using var input = new IndexInput(path);
        byte version = PostingsEnum.ValidateFileHeader(input);
        Assert.Equal(CodecConstants.PostingsVersion, version);
    }

    [Fact(DisplayName = "PostingsEnum: ValidateFileHeader Bad Magic Throws")]
    public void ValidateFileHeader_BadMagic_Throws()
    {
        var path = Path.Combine(_dir, "test_bad.pos");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0xDEADBEEF);  // Wrong magic.
            bw.Write((byte)1);
        }

        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() => PostingsEnum.ValidateFileHeader(input));
    }

    [Fact(DisplayName = "PostingsEnum: ValidateFileHeader Unsupported Version Throws")]
    public void ValidateFileHeader_UnsupportedVersion_Throws()
    {
        var path = Path.Combine(_dir, "test_future.pos");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(CodecConstants.Magic);
            bw.Write((byte)99);  // Future version > max supported.
        }

        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() => PostingsEnum.ValidateFileHeader(input));
    }

    // Hand-written v2 postings list

    [Fact(DisplayName = "PostingsEnum: Create v2 Iterates DocIds And Frequencies")]
    public void CreateV2_IteratesDocIdsAndFrequencies()
    {
        var path = WriteV2PostingsList();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, 0, postingsVersion: 2);
        try
        {
            Assert.Equal(3, pe.DocFreq);
            Assert.Equal(-1, pe.DocId);

            Assert.True(pe.MoveNext());
            Assert.Equal(2, pe.DocId);
            Assert.Equal(1, pe.Freq);

            Assert.True(pe.MoveNext());
            Assert.Equal(5, pe.DocId);
            Assert.Equal(2, pe.Freq);

            Assert.True(pe.MoveNext());
            Assert.Equal(9, pe.DocId);
            Assert.Equal(3, pe.Freq);

            Assert.False(pe.MoveNext());
            Assert.Equal(-1, pe.DocId);
        }
        finally
        {
            pe.Dispose();
        }
    }

    [Fact(DisplayName = "PostingsEnum: Create v2 Advance Seeks To Target DocId")]
    public void CreateV2_Advance_SeeksToTargetDocId()
    {
        var path = WriteV2PostingsList();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, 0, postingsVersion: 2);
        try
        {
            Assert.True(pe.Advance(5));
            Assert.Equal(5, pe.DocId);
            Assert.Equal(2, pe.Freq);

            Assert.True(pe.Advance(8));
            Assert.Equal(9, pe.DocId);
            Assert.Equal(3, pe.Freq);

            Assert.False(pe.Advance(10));
            Assert.Equal(-1, pe.DocId);
        }
        finally
        {
            pe.Dispose();
        }
    }

    [Fact(DisplayName = "PostingsEnum: Create v2 Reset Rewinds Cursor")]
    public void CreateV2_Reset_RewindsCursor()
    {
        var path = WriteV2PostingsList();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, 0, postingsVersion: 2);
        try
        {
            Assert.True(pe.MoveNext());
            Assert.Equal(2, pe.DocId);

            pe.Reset();

            Assert.True(pe.MoveNext());
            Assert.Equal(2, pe.DocId);
        }
        finally
        {
            pe.Dispose();
        }
    }

    private string WriteV2PostingsList()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".pos");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write(3); // count
        bw.Write(0); // skip count

        WriteVarInt(bw, 2); // doc id 2
        WriteVarInt(bw, 3); // doc id 5
        WriteVarInt(bw, 4); // doc id 9

        bw.Write(true); // has frequencies
        WriteVarInt(bw, 1);
        WriteVarInt(bw, 2);
        WriteVarInt(bw, 3);

        return path;
    }

    private static void WriteVarInt(BinaryWriter writer, int value)
    {
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            writer.Write((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }
        writer.Write((byte)remaining);
    }
}
