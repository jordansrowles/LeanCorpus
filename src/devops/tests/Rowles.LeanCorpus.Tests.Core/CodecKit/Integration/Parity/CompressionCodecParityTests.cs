using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;

namespace Rowles.LeanCorpus.Tests.Core.CodecKit;
[Category(TestCategory.Integration)]
[Area(TestArea.CodecKit)]
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

    /// <summary>
    /// SHA-256 of the compressed bytes for fixed inputs, recorded before the optional codecs
    /// changed their buffer handling. On-disk bytes must never change for a fixed input and level.
    /// </summary>
    public static TheoryData<FieldCompressionPolicy, string, int, string> GoldenCases() => new()
    {
        { FieldCompressionPolicy.Lz4, "random", 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" },
        { FieldCompressionPolicy.Lz4, "random", 1, "6591786051d3780dbd2ae62da67b19fa169eb0dbfae9bb1845b4a0164d0d9d01" },
        { FieldCompressionPolicy.Lz4, "random", 4096, "650a1765ed3068cb6f2b7b430d9c3604066148bddc51f9c7579cb00a57d65da8" },
        { FieldCompressionPolicy.Lz4, "random", 65536, "a4e6dbb4df1a289d4d6a3ab4306204adbe74444c5dd6e3b6efce64289f1d38c6" },
        { FieldCompressionPolicy.Lz4, "random", 1048576, "69b1734eb4e24eae6c553a57883677a889600fce8b4a439379c0154423d6a9e5" },
        { FieldCompressionPolicy.Lz4, "text", 4096, "897f37b0d0245140613add175f523fffc257ea77cca1b87f7f2e4a6acfdacaf4" },
        { FieldCompressionPolicy.Lz4, "text", 65536, "469f7447366964172a26be06c28aab36170bcb3c89c3e97c2b13478d8862b5f7" },
        { FieldCompressionPolicy.Zstandard, "random", 0, "6fb8543883dafcc2ed0934866b8b3f775231f5b0933add08bf502e9e84944cbd" },
        { FieldCompressionPolicy.Zstandard, "random", 1, "753307da556e7a19e36fe2e633cf167407307eec696afa6be6e1c3fd8f2ab2fa" },
        { FieldCompressionPolicy.Zstandard, "random", 4096, "11d43433bcc27a7d692e902d6ad78231782631ea8a23264361f7330fcae239ac" },
        { FieldCompressionPolicy.Zstandard, "random", 65536, "923450ccdcfe3c8cee5bda5b20a27af039e789e02c006e1caade0064b10473d4" },
        { FieldCompressionPolicy.Zstandard, "random", 1048576, "9eaf2fa3827c7e23c845ec3c75dfc58ee135a2ceb1218f03508296f61aa35534" },
        { FieldCompressionPolicy.Zstandard, "text", 4096, "40f4ef55610672d72b2412a59302a605dd2ed7a3474ed8de4ecd5bc21a068507" },
        { FieldCompressionPolicy.Zstandard, "text", 65536, "177c72e0ef0d1f1f9ee377984fbf78f75cd77901b4ba043643a1d5af36446661" },
    };

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Compress_FixedInput_MatchesGoldenBytes(FieldCompressionPolicy policy, string kind, int payloadSize, string expectedSha256)
    {
        var payload = kind == "text" ? CreateTextPayload(payloadSize) : CreatePayload(payloadSize);
        var codec = CompressionCodecRegistry.Get(policy);

        var compressed = codec.Compress(payload);

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(compressed)));
        Assert.Equal(payload, codec.Decompress(compressed, payload.Length));
    }

    [Theory]
    [InlineData(FieldCompressionPolicy.Lz4)]
    [InlineData(FieldCompressionPolicy.Zstandard)]
    public void Compress_ConcurrentCallers_ProduceSameBytesAsSequentialCalls(FieldCompressionPolicy policy)
    {
        // Pooled scratch is rented and returned inside each call, so concurrent callers of
        // different sizes must never see another caller's bytes.
        var codec = CompressionCodecRegistry.Get(policy);
        var payloads = Enumerable.Range(0, 48)
            .Select(index => index % 2 == 0 ? CreatePayload(512 + index * 977) : CreateTextPayload(512 + index * 1_531))
            .ToArray();
        var expected = payloads.Select(payload => codec.Compress(payload)).ToArray();

        var actual = new byte[payloads.Length * 8][];
        Parallel.For(0, actual.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            actual[index] = codec.Compress(payloads[index % payloads.Length]));

        for (int index = 0; index < actual.Length; index++)
        {
            var payload = payloads[index % payloads.Length];
            Assert.Equal(expected[index % payloads.Length], actual[index]);
            Assert.Equal(payload, codec.Decompress(actual[index], payload.Length));
        }
    }

    private static byte[] CreateTextPayload(int size)
    {
        var builder = new StringBuilder(size);
        var random = new Random(31 + size); // NOSONAR: S2245, a seeded generator makes the golden payload reproducible; nothing here is security-sensitive.
        string[] words = ["index", "segment", "postings", "vector", "stored", "field", "codec", "frame", "merge", "query"];
        while (builder.Length < size)
            builder.Append(words[random.Next(words.Length)]).Append(' ');
        return Encoding.ASCII.GetBytes(builder.ToString(0, size));
    }

    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        var random = new Random(17 + size);
        random.NextBytes(payload);
        return payload;
    }
}
