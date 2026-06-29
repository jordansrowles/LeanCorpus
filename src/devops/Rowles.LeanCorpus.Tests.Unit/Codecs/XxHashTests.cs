using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Xunit;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Checksums")]
public class XxHashTests
{
    // ─── Test vectors from the xxHash specification ─────────────
    // Source: https://github.com/Cyan4973/xxHash/blob/dev/doc/xxhash_spec.md

    public static TheoryData<string, uint> XxHash32Vectors => new()
    {
        { "", 0x02CC5D05u },
        { "a", 0x550D7456u },
        { "abc", 0x32D153FFu },
        { "message digest", 0x7C948494u },
        { "abcdefghijklmnopqrstuvwxyz", 0x63A14D5Fu },
        { "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 0x9C285E64u },
        { "12345678901234567890123456789012345678901234567890123456789012345678901234567890", 0x9C05F475u },
    };

    public static TheoryData<string, ulong> XxHash64Vectors => new()
    {
        { "", 0xEF46DB3751D8E999ul },
        { "a", 0xD24EC4F1A98C6E5Bul },
        { "abc", 0x44BC2CF5AD770999ul },
        { "message digest", 0x066ED728FCEEB3BEul },
        { "abcdefghijklmnopqrstuvwxyz", 0xCFE1F278FA89835Cul },
        { "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 0xAAA46907D3047814ul },
        { "12345678901234567890123456789012345678901234567890123456789012345678901234567890", 0xE04A477F19EE145Dul },
    };

    // ─── XXH32 direct ───────────────────────────────────────────

    [Theory(DisplayName = "XxHash32.Compute matches spec test vectors")]
    [MemberData(nameof(XxHash32Vectors))]
    public void XxHash32_MatchesSpecVectors(string input, uint expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);
        uint actual = XxHash32.Compute(data);
        Assert.Equal(expected, actual);
    }

    [Theory(DisplayName = "XxHash32.ToBytes returns spec vector as LE bytes")]
    [MemberData(nameof(XxHash32Vectors))]
    public void XxHash32_ToBytes_MatchesSpec(string input, uint expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);
        byte[] bytes = XxHash32.ToBytes(data);
        Assert.Equal(4, bytes.Length);
        uint roundtripped = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        Assert.Equal(expected, roundtripped);
    }

    [Fact(DisplayName = "XxHash32 empty input")]
    public void XxHash32_Empty() => Assert.Equal(0x02CC5D05u, XxHash32.Compute([]));

    [Fact(DisplayName = "XxHash32 single byte")]
    public void XxHash32_SingleByte() => Assert.Equal(0x550D7456u, XxHash32.Compute([(byte)'a']));

    [Fact(DisplayName = "XxHash32 stripe boundary (15 bytes)")]
    public void XxHash32_StripeMinusOne()
    {
        // 15 bytes = stripe-1: uses the scalar-only path (no 4x4 accumulator loop)
        byte[] d = Encoding.ASCII.GetBytes("123456789012345");
        uint h = XxHash32.Compute(d);
        // Just verify it's deterministic and doesn't crash
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash32 stripe boundary (16 bytes)")]
    public void XxHash32_StripeExact()
    {
        byte[] d = Encoding.ASCII.GetBytes("1234567890123456");
        uint h = XxHash32.Compute(d);
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash32 stripe boundary (17 bytes)")]
    public void XxHash32_StripePlusOne()
    {
        byte[] d = Encoding.ASCII.GetBytes("12345678901234567");
        uint h = XxHash32.Compute(d);
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash32 stripe ×2 (32 bytes)")]
    public void XxHash32_TwoStripes()
    {
        byte[] d = Encoding.ASCII.GetBytes("12345678901234561234567890123456");
        uint h = XxHash32.Compute(d);
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash32 large payload (10000 bytes) deterministic")]
    public void XxHash32_LargePayload()
    {
        byte[] d = new byte[10000];
        new Random(42).NextBytes(d);
        uint h1 = XxHash32.Compute(d);
        uint h2 = XxHash32.Compute(d);
        Assert.Equal(h1, h2);
        Assert.NotEqual(0u, h1);
    }

    // ─── XXH64 direct ───────────────────────────────────────────

    [Theory(DisplayName = "XxHash64.Compute matches spec test vectors")]
    [MemberData(nameof(XxHash64Vectors))]
    public void XxHash64_MatchesSpecVectors(string input, ulong expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);
        ulong actual = XxHash64.Compute(data);
        Assert.Equal(expected, actual);
    }

    [Theory(DisplayName = "XxHash64.ToBytes returns spec vector as LE bytes")]
    [MemberData(nameof(XxHash64Vectors))]
    public void XxHash64_ToBytes_MatchesSpec(string input, ulong expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);
        byte[] bytes = XxHash64.ToBytes(data);
        Assert.Equal(8, bytes.Length);
        ulong roundtripped = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        Assert.Equal(expected, roundtripped);
    }

    [Fact(DisplayName = "XxHash64 empty input")]
    public void XxHash64_Empty() => Assert.Equal(0xEF46DB3751D8E999ul, XxHash64.Compute([]));

    [Fact(DisplayName = "XxHash64 stripe boundary (31 bytes)")]
    public void XxHash64_StripeMinusOne()
    {
        byte[] d = new byte[31];
        new Random(42).NextBytes(d);
        ulong h = XxHash64.Compute(d);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 stripe boundary (32 bytes)")]
    public void XxHash64_StripeExact()
    {
        byte[] d = new byte[32];
        new Random(42).NextBytes(d);
        ulong h = XxHash64.Compute(d);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 stripe boundary (33 bytes)")]
    public void XxHash64_StripePlusOne()
    {
        byte[] d = new byte[33];
        new Random(42).NextBytes(d);
        ulong h = XxHash64.Compute(d);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 stripe ×2 (64 bytes)")]
    public void XxHash64_TwoStripes()
    {
        byte[] d = new byte[64];
        new Random(42).NextBytes(d);
        ulong h = XxHash64.Compute(d);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 large payload (10000 bytes) deterministic")]
    public void XxHash64_LargePayload()
    {
        byte[] d = new byte[10000];
        new Random(42).NextBytes(d);
        ulong h1 = XxHash64.Compute(d);
        ulong h2 = XxHash64.Compute(d);
        Assert.Equal(h1, h2);
        Assert.NotEqual(0ul, h1);
    }

    // ─── Cross-consistency: XXH32 output matches own bytes ──────

    [Theory(DisplayName = "XxHash32 round-trip: hash → LE bytes → hash")]
    [MemberData(nameof(XxHash32Vectors))]
    public void XxHash32_RoundTrip(string input, uint expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);
        uint h = XxHash32.Compute(data);
        byte[] bytes = XxHash32.ToBytes(data);
        Assert.Equal(expected, h);
        Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    // ─── Provider integration ──────────────────────────────────

    [Fact(DisplayName = "XxHash32 provider registered and returns 4-byte checksums")]
    public void XxHash32_Provider_Registered()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        Assert.NotNull(provider);
        Assert.Equal(4, provider.ChecksumByteLength);
    }

    [Fact(DisplayName = "XxHash64 provider registered and returns 8-byte checksums")]
    public void XxHash64_Provider_Registered()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        Assert.NotNull(provider);
        Assert.Equal(8, provider.ChecksumByteLength);
    }

    [Fact(DisplayName = "XxHash32 provider matches direct Compute for known vector")]
    public void XxHash32_Provider_MatchesDirect()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        byte[] data = Encoding.ASCII.GetBytes("abc");
        byte[] providerResult = provider.Compute(new ReadOnlySequence<byte>(data));
        uint direct = XxHash32.Compute(data);
        Assert.Equal(direct, BinaryPrimitives.ReadUInt32LittleEndian(providerResult));
    }

    [Fact(DisplayName = "XxHash64 provider matches direct Compute for known vector")]
    public void XxHash64_Provider_MatchesDirect()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        byte[] data = Encoding.ASCII.GetBytes("abc");
        byte[] providerResult = provider.Compute(new ReadOnlySequence<byte>(data));
        ulong direct = XxHash64.Compute(data);
        Assert.Equal(direct, BinaryPrimitives.ReadUInt64LittleEndian(providerResult));
    }

    // ─── Multi-segment (provider path) ──────────────────────────

    [Fact(DisplayName = "XxHash32 provider: single vs multi-segment produce identical output")]
    public void XxHash32_Provider_SingleVsMultiSegment()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        byte[] data = new byte[500];
        new Random(42).NextBytes(data);

        byte[] single = provider.Compute(new ReadOnlySequence<byte>(data));
        byte[] multi = provider.Compute(MultiSegment(data, 7));
        byte[] multiLargeChunks = provider.Compute(MultiSegment(data, 128));

        Assert.Equal(single, multi);
        Assert.Equal(single, multiLargeChunks);
    }

    [Fact(DisplayName = "XxHash64 provider: single vs multi-segment produce identical output")]
    public void XxHash64_Provider_SingleVsMultiSegment()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        byte[] data = new byte[500];
        new Random(42).NextBytes(data);

        byte[] single = provider.Compute(new ReadOnlySequence<byte>(data));
        byte[] multi = provider.Compute(MultiSegment(data, 7));
        byte[] multiLargeChunks = provider.Compute(MultiSegment(data, 128));

        Assert.Equal(single, multi);
        Assert.Equal(single, multiLargeChunks);
    }

    // ─── Verify ─────────────────────────────────────────────────

    [Fact(DisplayName = "XxHash32 provider.Verify returns true for correct checksum")]
    public void XxHash32_Provider_Verify_True()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        byte[] data = Encoding.ASCII.GetBytes("message digest");
        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "XxHash32 provider.Verify returns false for wrong checksum")]
    public void XxHash32_Provider_Verify_False()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        byte[] data = Encoding.ASCII.GetBytes("test");
        Assert.False(provider.Verify(new ReadOnlySequence<byte>(data), new byte[4]));
    }

    [Fact(DisplayName = "XxHash32 provider.Verify returns false for wrong-length checksum")]
    public void XxHash32_Provider_Verify_WrongLength()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        Assert.False(provider.Verify(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes("x")), [1, 2]));
    }

    [Fact(DisplayName = "XxHash64 provider.Verify returns true for correct checksum")]
    public void XxHash64_Provider_Verify_True()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        byte[] data = Encoding.ASCII.GetBytes("message digest");
        byte[] checksum = provider.Compute(new ReadOnlySequence<byte>(data));
        Assert.True(provider.Verify(new ReadOnlySequence<byte>(data), checksum));
    }

    [Fact(DisplayName = "XxHash64 provider.Verify returns false for wrong checksum")]
    public void XxHash64_Provider_Verify_False()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        byte[] data = Encoding.ASCII.GetBytes("test");
        Assert.False(provider.Verify(new ReadOnlySequence<byte>(data), new byte[8]));
    }

    // ─── Determinism ────────────────────────────────────────────

    [Fact(DisplayName = "XxHash32 is deterministic across repeated calls")]
    public void XxHash32_Deterministic()
    {
        byte[] d = Encoding.ASCII.GetBytes("determinism-check-payload-v1");
        uint h1 = XxHash32.Compute(d);
        uint h2 = XxHash32.Compute(d);
        Assert.Equal(h1, h2);
    }

    [Fact(DisplayName = "XxHash64 is deterministic across repeated calls")]
    public void XxHash64_Deterministic()
    {
        byte[] d = Encoding.ASCII.GetBytes("determinism-check-payload-v1");
        ulong h1 = XxHash64.Compute(d);
        ulong h2 = XxHash64.Compute(d);
        Assert.Equal(h1, h2);
    }

    // ─── All-zero and all-0xFF payloads ─────────────────────────

    [Fact(DisplayName = "XxHash32 all-zero payload produces non-zero non-crashing result")]
    public void XxHash32_AllZero()
    {
        byte[] d = new byte[256];
        uint h = XxHash32.Compute(d);
        Assert.NotEqual(0u, h);
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 all-zero payload produces non-zero non-crashing result")]
    public void XxHash64_AllZero()
    {
        byte[] d = new byte[256];
        ulong h = XxHash64.Compute(d);
        Assert.NotEqual(0ul, h);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    [Fact(DisplayName = "XxHash32 all-0xFF payload produces non-zero non-crashing result")]
    public void XxHash32_AllFF()
    {
        byte[] d = new byte[256];
        Array.Fill(d, (byte)0xFF);
        uint h = XxHash32.Compute(d);
        Assert.NotEqual(0u, h);
        Assert.Equal(h, XxHash32.Compute(d));
    }

    [Fact(DisplayName = "XxHash64 all-0xFF payload produces non-zero non-crashing result")]
    public void XxHash64_AllFF()
    {
        byte[] d = new byte[256];
        Array.Fill(d, (byte)0xFF);
        ulong h = XxHash64.Compute(d);
        Assert.NotEqual(0ul, h);
        Assert.Equal(h, XxHash64.Compute(d));
    }

    // ─── Thread safety for providers ────────────────────────────

    [Fact(DisplayName = "XxHash32 provider is thread-safe")]
    public void XxHash32_Provider_ThreadSafe()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash32);
        byte[] data = new byte[1000];
        new Random(42).NextBytes(data);
        byte[] expected = provider.Compute(new ReadOnlySequence<byte>(data));

        System.Threading.Tasks.Parallel.For(0, 100, _ =>
        {
            byte[] result = provider.Compute(new ReadOnlySequence<byte>(data));
            Assert.Equal(expected, result);
        });
    }

    [Fact(DisplayName = "XxHash64 provider is thread-safe")]
    public void XxHash64_Provider_ThreadSafe()
    {
        var provider = CodecRegistry.Default.GetChecksumProvider(ChecksumAlgorithms.XxHash64);
        byte[] data = new byte[1000];
        new Random(42).NextBytes(data);
        byte[] expected = provider.Compute(new ReadOnlySequence<byte>(data));

        System.Threading.Tasks.Parallel.For(0, 100, _ =>
        {
            byte[] result = provider.Compute(new ReadOnlySequence<byte>(data));
            Assert.Equal(expected, result);
        });
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private static ReadOnlySequence<byte> MultiSegment(byte[] data, int chunkSize)
    {
        if (data.Length == 0) return new ReadOnlySequence<byte>([]);
        var first = new MemorySegment(data.AsMemory(0, Math.Min(chunkSize, data.Length)));
        MemorySegment last = first;
        int offset = chunkSize;
        while (offset < data.Length)
        {
            int len = Math.Min(chunkSize, data.Length - offset);
            last = last.Append(data.AsMemory(offset, len));
            offset += len;
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class MemorySegment : ReadOnlySequenceSegment<byte>
    {
        public MemorySegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public MemorySegment Append(ReadOnlyMemory<byte> memory)
        {
            var seg = new MemorySegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = seg;
            return seg;
        }
    }
}
