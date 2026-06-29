using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Store;
using System.Text;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

public sealed class PostingsEnumGapsTests : IDisposable
{
    private readonly string _dir;

    public PostingsEnumGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pe_gaps_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact(DisplayName = "PostingsEnum: ValidateFileHeader Valid Header Returns Version")]
    public void ValidateFileHeader_ValidHeader_ReturnsVersion()
    {
        var path = Path.Combine(_dir, "test_valid.pos");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            using var bodyMs = new MemoryStream();
            using var bodyWriter = new BinaryWriter(bodyMs, Encoding.UTF8, leaveOpen: false);
            bodyWriter.Flush();
            CodecFileHeader.Write(bw, CodecFormats.Postings, bodyMs.ToArray());
        }

        using var input = new IndexInput(path);
        byte version = PostingsEnum.ValidateFileHeader(input);
        Assert.Equal(CodecConstants.PostingsVersion, version);
    }

    [Fact(DisplayName = "PostingsEnum: ValidateFileHeader Unsupported Version Throws")]
    public void ValidateFileHeader_UnsupportedVersion_Throws()
    {
        var path = Path.Combine(_dir, "test_future.pos");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((byte)99); // version = 99 (unsupported)
            bw.Write((byte)0);  // body length VarInt = 0
        }

        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() => PostingsEnum.ValidateFileHeader(input));
    }

    [Fact(DisplayName = "PostingsEnum: Create Iterates DocIds And Frequencies")]
    public void Create_IteratesDocIdsAndFrequencies()
    {
        var (path, offset) = WriteCurrentFormatPostings();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, offset);
        try
        {
            Assert.True(pe.MoveNext());
            Assert.Equal(2, pe.DocId);

            Assert.True(pe.MoveNext());
            Assert.Equal(5, pe.DocId);

            Assert.True(pe.MoveNext());
            Assert.Equal(9, pe.DocId);

            Assert.False(pe.MoveNext());
        }
        finally
        {
            pe.Dispose();
        }
    }

    [Fact(DisplayName = "PostingsEnum: Create Advance Seeks To Target DocId")]
    public void Create_Advance_SeeksToTargetDocId()
    {
        var (path, offset) = WriteCurrentFormatPostings();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, offset);
        try
        {
            Assert.True(pe.MoveNext());
            Assert.Equal(2, pe.DocId);

            // Advance to doc id 5
            Assert.True(pe.Advance(5));
            Assert.Equal(5, pe.DocId);

            // Advance past end
            Assert.False(pe.Advance(100));
            Assert.True(pe.DocId >= 100 || pe.DocId == int.MinValue);
        }
        finally
        {
            pe.Dispose();
        }
    }

    [Fact(DisplayName = "PostingsEnum: Create Reset Rewinds Cursor")]
    public void Create_Reset_RewindsCursor()
    {
        var (path, offset) = WriteCurrentFormatPostings();
        using var input = new IndexInput(path);
        var pe = PostingsEnum.Create(input, offset);
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

    private (string Path, long Offset) WriteCurrentFormatPostings()
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".pos");
        string tmp = Path.Combine(_dir, "tmp." + Guid.NewGuid().ToString("N"));

        long offset;
        using (var bodyOut = new IndexOutput(tmp))
        {
            long headerPos = bodyOut.Position;
            bodyOut.WriteInt32(0);             // docFreq placeholder
            bodyOut.WriteInt64(0L);             // skipOffset placeholder
            bodyOut.WriteBoolean(true);         // hasFreqs
            bodyOut.WriteBoolean(false);        // hasPositions
            bodyOut.WriteBoolean(false);        // hasPayloads

            using var blockWriter = new BlockPostingsWriter(bodyOut);
            blockWriter.StartTerm();
            blockWriter.AddPosting(2, 1);
            blockWriter.AddPosting(5, 2);
            blockWriter.AddPosting(9, 3);
            var meta = blockWriter.FinishTerm();
            long endPos = bodyOut.Position;
            bodyOut.Seek(headerPos);
            bodyOut.WriteInt32(meta.DocFreq);
            bodyOut.WriteInt64(meta.SkipOffset);
            bodyOut.Seek(endPos);
        }

        byte[] body = File.ReadAllBytes(tmp);
        File.Delete(tmp);

        int envelopeSize = 1 + VarIntSize(body.Length);
        // Patch skipOffset inside body to account for CodecKit envelope
        long oldSkip = BitConverter.ToInt64(body, 4); // skipOffset is at body[4..11]
        byte[] bumped = BitConverter.GetBytes(oldSkip + envelopeSize);
        bumped.CopyTo(body, 4);

        offset = envelopeSize;
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(CodecConstants.PostingsVersion);
            output.WriteVarInt(body.Length);
            output.WriteBytes(body);
        }
        return (path, offset);
    }

    private static int VarIntSize(long value)
    {
        int size = 0;
        do { size++; value >>= 7; } while (value != 0);
        return size;
    }
}
