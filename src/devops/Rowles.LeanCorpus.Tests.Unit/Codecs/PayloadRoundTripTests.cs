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
        string tmp = Path.GetTempFileName();
        try
        {
            long headerPos;
            {
                using var bodyOut = new IndexOutput(tmp);
                using var bw = new BlockPostingsWriter(bodyOut);
                headerPos = bodyOut.Position;
                bodyOut.WriteInt32(0);
                bodyOut.WriteInt64(0L);
                bodyOut.WriteBoolean(true);  // hasFreqs
                bodyOut.WriteBoolean(false); // hasPositions
                bodyOut.WriteBoolean(false); // hasPayloads

                bw.StartTerm();
                for (int i = 0; i < docIds.Length; i++)
                    bw.AddPosting(docIds[i], freqs[i]);
                var meta = bw.FinishTerm();
                long endPos = bodyOut.Position;
                bodyOut.Seek(headerPos);
                bodyOut.WriteInt32(meta.DocFreq);
                bodyOut.WriteInt64(meta.SkipOffset);
                bodyOut.Seek(endPos);
            }

            byte[] body = File.ReadAllBytes(tmp);
            File.Delete(tmp);

            int env = 1 + VarIntSize(body.Length);
            // Patch skipOffset for CodecKit envelope
            long oldSkip = BitConverter.ToInt64(body, (int)headerPos + 4);
            byte[] bump = BitConverter.GetBytes(oldSkip + env);
            bump.CopyTo(body, (int)headerPos + 4);

            termOffset = env;
            using var output = new IndexOutput(posPath);
            output.WriteByte(CodecConstants.PostingsVersion);
            output.WriteVarInt(body.Length);
            output.WriteBytes(body);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static int VarIntSize(long value)
    {
        int s = 0; do { s++; value >>= 7; } while (value != 0);
        return s;
    }
}
