using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.LeanCorpus.Tests.CompressionParity;

[Trait("Category", "CompressionParity")]
public sealed class CompressionCodecParityTests
{
    private static readonly FieldCompressionPolicy[] Policies =
    [
        FieldCompressionPolicy.None,
        FieldCompressionPolicy.Deflate,
        FieldCompressionPolicy.Brotli,
        FieldCompressionPolicy.Lz4,
        FieldCompressionPolicy.Snappy,
        FieldCompressionPolicy.Zstandard
    ];

    private static readonly int[] PayloadSizes = [0, 1, 4 * 1024, 64 * 1024, 1024 * 1024];

    static CompressionCodecParityTests()
    {
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();
    }

    public static TheoryData<FieldCompressionPolicy, int> RoundTripCases()
    {
        var cases = new TheoryData<FieldCompressionPolicy, int>();
        foreach (var policy in Policies)
        {
            foreach (int payloadSize in PayloadSizes)
                cases.Add(policy, payloadSize);
        }

        return cases;
    }

    public static TheoryData<FieldCompressionPolicy> PolicyCases()
    {
        var cases = new TheoryData<FieldCompressionPolicy>();
        foreach (var policy in Policies)
            cases.Add(policy);

        return cases;
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Decompress_CompressedPayload_ReturnsOriginalBytes(FieldCompressionPolicy policy, int payloadSize)
    {
        var payload = CreatePayload(payloadSize);
        var codec = CompressionCodecRegistry.Get(policy);

        var compressed = codec.Compress(payload);
        var restored = codec.Decompress(compressed, payload.Length);

        Assert.Equal(payload, restored);
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void Get_RegisteredPolicy_ReturnsExpectedCodec(FieldCompressionPolicy policy)
    {
        var codec = CompressionCodecRegistry.Get(policy);

        Assert.Equal((byte)policy, codec.PolicyByte);
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void Decompress_InvalidOriginalSize_Throws(FieldCompressionPolicy policy)
    {
        var payload = CreatePayload(4 * 1024);
        var codec = CompressionCodecRegistry.Get(policy);
        var compressed = codec.Compress(payload);

        Assert.ThrowsAny<Exception>(() => codec.Decompress(compressed, payload.Length + 1));
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void Decompress_EmptyCompressedPayloadForNonEmptyOriginal_Throws(FieldCompressionPolicy policy)
    {
        var payload = CreatePayload(4 * 1024);
        var codec = CompressionCodecRegistry.Get(policy);

        Assert.ThrowsAny<Exception>(() => codec.Decompress([], payload.Length));
    }

    [Fact]
    public void Lz4Decompress_EmptyPayloadWithCompressedBytes_Throws()
    {
        var codec = new Lz4CompressionCodec();

        Assert.Throws<InvalidDataException>(() => codec.Decompress([1], 0));
    }

    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        var random = new Random(17 + size);
        random.NextBytes(payload);
        return payload;
    }
}
