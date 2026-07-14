using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Codecs")]
[Trait("Category", "Migration")]
public sealed class TermVectorMigrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public TermVectorMigrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Term Vectors: v1 file read by v2 reader — offsets absent")]
    public void V1File_ReadByV2Reader_OffsetsAreNull()
    {
        var basePath = Path.Combine(_fixture.Path, $"tv-migrate-{Guid.NewGuid():N}");

        // ── Write v1 .tvd body ────────────────────────────────────────
        // Single document, one field "body", two terms: "hello" (pos 0), "world" (pos 1).
        // v1 format per term: [term][freq][posCount][positions][hasPayloads=0]
        var tvdBodyBuf = new ArrayBufferWriter<byte>(256);
        tvdBodyBuf.WriteInt32(1);                          // fieldCount = 1
        tvdBodyBuf.WriteString("body");                 // fieldName
        tvdBodyBuf.WriteInt32(2);                          // termCount = 2

        // Term 1: "hello"
        tvdBodyBuf.WriteString("hello");
        tvdBodyBuf.WriteInt32(1);                          // freq
        tvdBodyBuf.WriteInt32(1);                          // posCount
        tvdBodyBuf.WriteInt32(0);                          // positions[0]
        tvdBodyBuf.WriteByte(0);                           // hasPayloads = false
        // v1: no hasOffsets field here!

        // Term 2: "world"
        tvdBodyBuf.WriteString("world");
        tvdBodyBuf.WriteInt32(1);                          // freq
        tvdBodyBuf.WriteInt32(1);                          // posCount
        tvdBodyBuf.WriteInt32(1);                          // positions[0]
        tvdBodyBuf.WriteByte(0);                           // hasPayloads = false
        // v1: no hasOffsets field here!

        byte[] tvdBody = tvdBodyBuf.WrittenSpan.ToArray();

        // ── Write v1 .tvx body ────────────────────────────────────────
        var tvxBodyBuf = new ArrayBufferWriter<byte>(64);
        // Offset of document 0 into .tvd body = 0
        long headerSize = 1 + VarIntSize(tvdBody.Length); // version byte + VarInt body length
        tvxBodyBuf.WriteInt32(1);                          // docCount = 1
        tvxBodyBuf.WriteInt64(headerSize + 0);             // offset of doc 0
        byte[] tvxBody = tvxBodyBuf.WrittenSpan.ToArray();

        // ── Write files with v1 envelope ──────────────────────────────
        WriteV1Envelope(basePath + ".tvd", tvdBody);
        WriteV1Envelope(basePath + ".tvx", tvxBody);

        // ── Read with v2-aware reader ─────────────────────────────────
        using var reader = TermVectorsReader.Open(basePath + ".tvd", basePath + ".tvx");
        var vectors = reader.GetTermVector(0);

        Assert.NotNull(vectors);
        Assert.True(vectors.ContainsKey("body"));
        var entries = vectors["body"];
        Assert.Equal(2, entries.Count);

        var hello = Assert.Single(entries, static e => e.Term == "hello");
        Assert.Equal(1, hello.Freq);
        Assert.Single(hello.Positions, 0);
        Assert.Null(hello.StartOffsets);
        Assert.Null(hello.EndOffsets);

        var world = Assert.Single(entries, static e => e.Term == "world");
        Assert.Equal(1, world.Freq);
        Assert.Single(world.Positions, 1);
        Assert.Null(world.StartOffsets);
        Assert.Null(world.EndOffsets);
    }

    [Fact(DisplayName = "Term Vectors: v2 file round-trips with offsets")]
    public void V2File_RoundTrip_OffsetsPreserved()
    {
        var path = Path.Combine(_fixture.Path, $"tv-roundtrip-{Guid.NewGuid():N}");
        var docs = new Dictionary<string, List<TermVectorEntry>>[]
        {
            new(StringComparer.Ordinal)
            {
                ["body"] =
                [
                    new TermVectorEntry("hello", 1, [0],
                        Payloads: null, StartOffsets: [0], EndOffsets: [5]),
                    new TermVectorEntry("world", 1, [1],
                        Payloads: null, StartOffsets: [6], EndOffsets: [11])
                ]
            }
        };

        TermVectorsWriter.Write(path + ".tvd", path + ".tvx", docs);

        using var reader = TermVectorsReader.Open(path + ".tvd", path + ".tvx");
        var vectors = reader.GetTermVector(0);

        Assert.NotNull(vectors);
        var entries = vectors["body"];
        Assert.Equal(2, entries.Count);

        var hello = Assert.Single(entries, static e => e.Term == "hello");
        Assert.Equal([0], hello.StartOffsets!);
        Assert.Equal([5], hello.EndOffsets!);

        var world = Assert.Single(entries, static e => e.Term == "world");
        Assert.Equal([6], world.StartOffsets!);
        Assert.Equal([11], world.EndOffsets!);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static void WriteV1Envelope(string path, byte[] body)
    {
        using var output = new IndexOutput(path);
        output.WriteByte(1);                   // version = 1
        output.WriteVarInt(body.Length);       // body length (VarInt)
        output.WriteBytes(body);
    }

    private static int VarIntSize(long value)
    {
        int size = 1;
        while (value >= 128)
        {
            size++;
            value >>= 7;
        }
        return size;
    }
}
