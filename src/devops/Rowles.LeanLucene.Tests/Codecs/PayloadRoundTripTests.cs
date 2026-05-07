using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Tests that per-position payloads survive a flush → disk → read round-trip
/// via the v2 postings format.
/// </summary>
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

    /// <summary>
    /// Verifies the Payload Round-trip: Single Doc Single Position scenario.
    /// </summary>
    [Fact(DisplayName = "Payload Round-trip: Single Doc Single Position")]
    public void PayloadRoundTrip_SingleDocSinglePosition()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        byte[] payload = [0xCA, 0xFE];

        // Write
        long termOffset;
        using (var output = new IndexOutput(posPath))
        {
            CodecConstants.WriteHeader(output, (byte)2);
            termOffset = output.Position;
            WriteTermPostings(output, docIds: [0], freqs: [1],
                positions: [[5]], payloads: [[payload]]);
        }

        // Read
        using var input = new IndexInput(posPath);
        byte version = PostingsEnum.ValidateFileHeader(input);
        Assert.True(version <= 3);

        var pe = PostingsEnum.CreateWithPositions(input, termOffset, version);
        Assert.True(pe.MoveNext());
        Assert.Equal(0, pe.DocId);

        var positions = pe.GetCurrentPositions();
        Assert.Equal(1, positions.Length);
        Assert.Equal(5, positions[0]);

        var readPayload = pe.GetPayload(0);
        Assert.Equal(payload, readPayload.ToArray());
        pe.Dispose();
    }

    /// <summary>
    /// Verifies the Payload Round-trip: Multi Doc Multi Position scenario.
    /// </summary>
    [Fact(DisplayName = "Payload Round-trip: Multi Doc Multi Position")]
    public void PayloadRoundTrip_MultiDocMultiPosition()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");
        byte[] p0 = [0x01, 0x02, 0x03];
        byte[] p1 = [0xAA];
        byte[] p2 = [0xBB, 0xCC];

        long termOffset;
        using (var output = new IndexOutput(posPath))
        {
            CodecConstants.WriteHeader(output, (byte)2);
            termOffset = output.Position;
            WriteTermPostings(output,
                docIds: [0, 3],
                freqs: [2, 1],
                positions: [[1, 4], [7]],
                payloads: [[p0, p1], [p2]]);
        }

        using var input = new IndexInput(posPath);
        byte version = PostingsEnum.ValidateFileHeader(input);

        var pe = PostingsEnum.CreateWithPositions(input, termOffset, version);

        // Doc 0
        Assert.True(pe.MoveNext());
        Assert.Equal(0, pe.DocId);
        var pos = pe.GetCurrentPositions();
        Assert.Equal(2, pos.Length);
        Assert.Equal(1, pos[0]);
        Assert.Equal(4, pos[1]);
        Assert.Equal(p0, pe.GetPayload(0).ToArray());
        Assert.Equal(p1, pe.GetPayload(1).ToArray());

        // Doc 3
        Assert.True(pe.MoveNext());
        Assert.Equal(3, pe.DocId);
        pos = pe.GetCurrentPositions();
        Assert.Equal(1, pos.Length);
        Assert.Equal(7, pos[0]);
        Assert.Equal(p2, pe.GetPayload(0).ToArray());

        Assert.False(pe.MoveNext());
        pe.Dispose();
    }

    /// <summary>
    /// Verifies the Payload Round-trip: Null Payloads Write Zero Length scenario.
    /// </summary>
    [Fact(DisplayName = "Payload Round-trip: Null Payloads Write Zero Length")]
    public void PayloadRoundTrip_NullPayloadsWriteZeroLength()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");

        long termOffset;
        using (var output = new IndexOutput(posPath))
        {
            CodecConstants.WriteHeader(output, (byte)2);
            termOffset = output.Position;
            WriteTermPostings(output,
                docIds: [0],
                freqs: [2],
                positions: [[0, 3]],
                payloads: [[null, [0xFF]]]);
        }

        using var input = new IndexInput(posPath);
        byte version = PostingsEnum.ValidateFileHeader(input);

        var pe = PostingsEnum.CreateWithPositions(input, termOffset, version);
        Assert.True(pe.MoveNext());

        pe.GetCurrentPositions();
        Assert.True(pe.GetPayload(0).IsEmpty); // null payload → empty
        Assert.Equal(new byte[] { 0xFF }, pe.GetPayload(1).ToArray());
        pe.Dispose();
    }

    /// <summary>
    /// Verifies the Payload Round-trip: No Payloads Still Works scenario.
    /// </summary>
    [Fact(DisplayName = "Payload Round-trip: No Payloads Still Works")]
    public void PayloadRoundTrip_NoPayloadsStillWorks()
    {
        var posPath = Path.Combine(_tempDir, "test.pos");

        long termOffset;
        using (var output = new IndexOutput(posPath))
        {
            CodecConstants.WriteHeader(output, (byte)2);
            termOffset = output.Position;
            WriteTermPostingsWithoutPayloads(output, docIds: [0, 1], freqs: [1, 2],
                positions: [[3], [1, 5]]);
        }

        using var input = new IndexInput(posPath);
        byte version = PostingsEnum.ValidateFileHeader(input);

        var pe = PostingsEnum.CreateWithPositions(input, termOffset, version);
        Assert.True(pe.MoveNext());
        var pos = pe.GetCurrentPositions();
        Assert.Equal(3, pos[0]);
        Assert.True(pe.GetPayload(0).IsEmpty); // no payloads at all

        Assert.True(pe.MoveNext());
        pos = pe.GetCurrentPositions();
        Assert.Equal(2, pos.Length);
        pe.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Writes a single term's v2 postings block with payloads.</summary>
    private static void WriteTermPostings(
        IndexOutput output, int[] docIds, int[] freqs,
        int[][] positions, byte[]?[][] payloads)
    {
        int count = docIds.Length;
        output.WriteInt32(count);
        output.WriteInt32(0); // skip count

        // Doc ID deltas
        int prev = 0;
        foreach (var id in docIds) { output.WriteVarInt(id - prev); prev = id; }

        // Frequencies
        output.WriteBoolean(true);
        foreach (var f in freqs) output.WriteVarInt(f);

        // Positions + payloads
        output.WriteBoolean(true);  // hasPositions
        output.WriteBoolean(true);  // hasPayloads
        for (int i = 0; i < count; i++)
        {
            output.WriteVarInt(positions[i].Length);
            int prevPos = 0;
            for (int j = 0; j < positions[i].Length; j++)
            {
                output.WriteVarInt(positions[i][j] - prevPos);
                prevPos = positions[i][j];

                var payload = payloads[i][j];
                if (payload is { Length: > 0 })
                {
                    output.WriteVarInt(payload.Length);
                    output.WriteBytes(payload);
                }
                else
                {
                    output.WriteVarInt(0);
                }
            }
        }
    }

    /// <summary>Writes a single term's v2 postings block without payloads.</summary>
    private static void WriteTermPostingsWithoutPayloads(
        IndexOutput output, int[] docIds, int[] freqs, int[][] positions)
    {
        int count = docIds.Length;
        output.WriteInt32(count);
        output.WriteInt32(0); // skip count

        int prev = 0;
        foreach (var id in docIds) { output.WriteVarInt(id - prev); prev = id; }

        output.WriteBoolean(true);
        foreach (var f in freqs) output.WriteVarInt(f);

        output.WriteBoolean(true);  // hasPositions
        output.WriteBoolean(false); // hasPayloads
        for (int i = 0; i < count; i++)
        {
            output.WriteVarInt(positions[i].Length);
            int prevPos = 0;
            foreach (var p in positions[i])
            {
                output.WriteVarInt(p - prevPos);
                prevPos = p;
            }
        }
    }
}
