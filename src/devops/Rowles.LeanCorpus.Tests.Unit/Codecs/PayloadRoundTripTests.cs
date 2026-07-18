using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

public sealed class PayloadRoundTripTests : IDisposable
{
    private readonly string _tempDir;

    public PayloadRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ll_payload_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact(DisplayName = "Postings: Single Doc Round-trip")]
    public void RoundTrip_SingleDoc()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        WritePosFile(posPath, docIds: [0], freqs: [1], out long off);

        using var input = new IndexInput(posPath);
        var pe = PostingsEnum.Create(input, off);
        Assert.True(pe.MoveNext());
        Assert.Equal(0, pe.DocId);
        Assert.False(pe.MoveNext());
        pe.Dispose();
    }

    [Fact(DisplayName = "Postings: Multi Doc Round-trip")]
    public void RoundTrip_MultiDoc()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        WritePosFile(posPath, docIds: [0, 3], freqs: [2, 1], out long off);

        using var input = new IndexInput(posPath);
        var pe = PostingsEnum.Create(input, off);
        Assert.True(pe.MoveNext()); Assert.Equal(0, pe.DocId);
        Assert.True(pe.MoveNext()); Assert.Equal(3, pe.DocId);
        Assert.False(pe.MoveNext());
        pe.Dispose();
    }

    [Fact(DisplayName = "Postings: With Payload Metadata Round-trip")]
    public void RoundTrip_WithPayloadMetadata()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        WritePosFile(posPath, docIds: [0], freqs: [2], out long off);

        using var input = new IndexInput(posPath);
        var pe = PostingsEnum.Create(input, off);
        Assert.True(pe.MoveNext());
        Assert.Equal(0, pe.DocId);
        Assert.False(pe.MoveNext());
        pe.Dispose();
    }

    [Fact(DisplayName = "Postings: Without Payloads Round-trip")]
    public void RoundTrip_WithoutPayloads()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        WritePosFile(posPath, docIds: [0, 1], freqs: [1, 2], out long off);

        using var input = new IndexInput(posPath);
        var pe = PostingsEnum.Create(input, off);
        Assert.True(pe.MoveNext()); Assert.Equal(0, pe.DocId);
        Assert.True(pe.MoveNext()); Assert.Equal(1, pe.DocId);
        Assert.False(pe.MoveNext());
        pe.Dispose();
    }

    private static void WritePosFile(string posPath, int[] docIds, int[] freqs, out long termOffset)
    {
        using var output = new IndexOutput(posPath);
        using var _scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.PostingsVersion);

        using var bw = new BlockPostingsWriter(output);
        long bodyOffset = output.Position;
        bw.StartTerm();
        for (int i = 0; i < docIds.Length; i++)
            bw.AddPosting(docIds[i], freqs[i]);
        var meta = bw.FinishTerm();
        termOffset = output.Position;
        output.WriteInt64(bodyOffset);
        output.WriteInt32(meta.DocFreq);
        output.WriteInt64(meta.SkipOffset);
        output.WriteBoolean(true);
        output.WriteBoolean(false);
        output.WriteBoolean(false);
    }
}
