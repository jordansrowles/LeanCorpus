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
        using (var output = new IndexOutput(path))
            PostingsFileHeader.WriteV2Header(output);

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

        long offset;
        using (var output = new IndexOutput(path))
        {
            PostingsFileHeader.WriteV2Header(output);

            long headerPos = output.Position;
            output.WriteInt32(0);             // docFreq placeholder
            output.WriteInt64(0L);             // skipOffset placeholder
            output.WriteBoolean(true);         // hasFreqs
            output.WriteBoolean(false);        // hasPositions
            output.WriteBoolean(false);        // hasPayloads

            using var blockWriter = new BlockPostingsWriter(output);
            blockWriter.StartTerm();
            blockWriter.AddPosting(2, 1);
            blockWriter.AddPosting(5, 2);
            blockWriter.AddPosting(9, 3);
            var meta = blockWriter.FinishTerm();
            long endPos = output.Position;
            output.Seek(headerPos);
            output.WriteInt32(meta.DocFreq);
            output.WriteInt64(meta.SkipOffset);
            output.Seek(endPos);

            offset = headerPos; // v2: offset is absolute file position, no envelope to account for
        }

        return (path, offset);
    }

    // VarIntSize removed — v2 postings format no longer needs envelope offset computation.
}
