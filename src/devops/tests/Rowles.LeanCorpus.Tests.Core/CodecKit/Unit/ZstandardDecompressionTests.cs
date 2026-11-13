using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.LeanCorpus.Tests.Core.CodecKit;

/// <summary>
/// Zstandard decompression decodes straight into one output buffer of the declared size and
/// still rejects input whose decoded length or framing is wrong.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class ZstandardDecompressionTests
{
    private readonly ZstandardCompressionCodec _codec = new();

    [Theory(DisplayName = "Zstandard Decompression: Round Trips Are Byte Identical")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4 * 1024)]
    [InlineData(64 * 1024)]
    [InlineData(1024 * 1024)]
    public void RoundTrip_ReturnsOriginalBytes(int size)
    {
        byte[] payload = CreatePayload(size);

        byte[] restored = _codec.Decompress(_codec.Compress(payload), size);

        Assert.Equal(payload, restored);
    }

    [Theory(DisplayName = "Zstandard Decompression: Wrong Original Size Is Rejected")]
    [InlineData(-1)]
    [InlineData(+1)]
    [InlineData(-4096)]
    [InlineData(+4096)]
    public void WrongOriginalSize_IsRejected(int delta)
    {
        byte[] payload = CreatePayload(64 * 1024);
        byte[] compressed = _codec.Compress(payload);

        AssertRejected(() => _codec.Decompress(compressed, payload.Length + delta));
    }

    [Fact(DisplayName = "Zstandard Decompression: Negative Original Size Is Rejected")]
    public void NegativeOriginalSize_IsRejected()
    {
        byte[] compressed = _codec.Compress(CreatePayload(16));

        Assert.Throws<InvalidDataException>(() => _codec.Decompress(compressed, -1));
    }

    [Theory(DisplayName = "Zstandard Decompression: Truncated Input Is Rejected")]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    public void TruncatedInput_IsRejected(int removedBytes)
    {
        byte[] payload = CreateTextPayload(64 * 1024);
        byte[] compressed = _codec.Compress(payload);

        AssertRejected(() => _codec.Decompress(compressed.AsSpan(0, compressed.Length - removedBytes), payload.Length));
    }

    [Fact(DisplayName = "Zstandard Decompression: Corrupt Frame Header Is Rejected")]
    public void CorruptFrameHeader_IsRejected()
    {
        byte[] payload = CreateTextPayload(64 * 1024);
        byte[] compressed = _codec.Compress(payload);
        compressed[0] ^= 0xFF;

        AssertRejected(() => _codec.Decompress(compressed, payload.Length));
    }

    [Fact(DisplayName = "Zstandard Decompression: Corrupt Block Is Rejected")]
    public void CorruptBlock_IsRejected()
    {
        byte[] payload = CreateTextPayload(64 * 1024);
        byte[] compressed = _codec.Compress(payload);
        // Overwrite the compressed body after the frame and block headers with noise.
        new Random(99).NextBytes(compressed.AsSpan(12, Math.Min(64, compressed.Length - 12)));

        AssertRejected(() => _codec.Decompress(compressed, payload.Length));
    }

    [Fact(DisplayName = "Zstandard Decompression: Allocates About One Output Buffer")]
    public void Decompress_AllocatesAboutOneOutputBuffer()
    {
        const int size = 64 * 1024;
        byte[] payload = CreateTextPayload(size);
        byte[] compressed = _codec.Compress(payload);
        for (int warmup = 0; warmup < 3; warmup++)
            _ = _codec.Decompress(compressed, size);

        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] restored = _codec.Decompress(compressed, size);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(payload, restored);
        // One 65,536-byte array is 65,560 bytes with its header. A second copy would double it.
        Assert.InRange(allocated, size, size + 1024);
    }

    private static void AssertRejected(Action decompress)
    {
        var exception = Record.Exception(decompress);
        Assert.NotNull(exception);
        Assert.True(exception is InvalidDataException or ZstdSharp.ZstdException,
            $"Unexpected exception type {exception.GetType().FullName}: {exception.Message}");
    }

    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        new Random(17 + size).NextBytes(payload);
        return payload;
    }

    private static byte[] CreateTextPayload(int size)
    {
        var random = new Random(23 + size);
        string[] words = ["index", "segment", "postings", "vector", "stored", "field", "codec", "frame"];
        var builder = new System.Text.StringBuilder(size);
        while (builder.Length < size)
            builder.Append(words[random.Next(words.Length)]).Append(' ');
        return System.Text.Encoding.ASCII.GetBytes(builder.ToString(0, size));
    }
}
