using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class ChecksumFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public ChecksumFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 50)]
    public void AnyPayload_EncodeWithChecksum_DecodeSucceeds(byte[] payload)
    {
        // Test with Crc32 + Header
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(payload.Length),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Header);

        byte[] encoded = Codec.EncodeToArray(codec, payload);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(payload, decoded);
    }

    [Property(MaxTest = 50)]
    public void AnyPayload_EncodeWithChecksum_CorruptOneByte_Fails(byte[] payload)
    {
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(payload.Length),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Header);

        byte[] encoded = Codec.EncodeToArray(codec, payload);
        if (encoded.Length > 5)
        {
            // Corrupt a byte in the body (after checksum header)
            encoded[4] ^= 0xFF;

            Assert.Throws<ChecksumMismatchException>(() => Codec.Decode(codec, encoded));
        }
    }

    [Property]
    public void DifferentPayloads_DifferentChecksums(byte[] payload1, byte[] payload2)
    {
        if (payload1.SequenceEqual(payload2) || payload1.Length != payload2.Length)
            return;

        var codec = Codec.WithChecksum(
            Codec.BytesOwned(payload1.Length),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Header);

        byte[] encoded1 = Codec.EncodeToArray(codec, payload1);
        byte[] encoded2 = Codec.EncodeToArray(codec, payload2);

        // Extract checksum bytes from the header (first 4 bytes)
        byte[] checksum1 = encoded1[..4];
        byte[] checksum2 = encoded2[..4];

        Assert.NotEqual(checksum1, checksum2);
    }
}
