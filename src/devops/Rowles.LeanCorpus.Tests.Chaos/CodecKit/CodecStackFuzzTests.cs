using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class CodecStackFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public CodecStackFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 30)]
    public void LayeredCodecs_LengthPrefixed_Checksum_FixedFrame_RoundTrip(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > 256) return; // keep sizes manageable, skip empty
        // Layer: LengthPrefixed → WithChecksum(Trailer) → FixedFrame
        var inner = Codec.FixedFrame(payload.Length, Codec.BytesOwnedRemaining(),
            FramePadding.Exact, TrailingDataPolicy.Allow);
        var withChecksum = Codec.WithChecksum(inner, ChecksumAlgorithms.Crc32, ChecksumPlacement.Trailer);
        var outer = Codec.LengthPrefixed(Codec.VarUInt32, withChecksum, TrailingDataPolicy.Allow);

        byte[] encoded = Codec.EncodeToArray(outer, payload);
        byte[] decoded = Codec.Decode(outer, encoded);

        Assert.Equal(payload, decoded);
    }

    [Property(MaxTest = 30)]
    public void LayeredCodecs_Compression_Checksum_RoundTrip(byte[] payload)
    {
        if (payload.Length > 1024) return;

        // Layer: WithCompression → WithChecksum(Header)
        var inner = Codec.WithCompression(Codec.BytesOwned(payload.Length));
        var outer = Codec.WithChecksum(inner, ChecksumAlgorithms.Crc32, ChecksumPlacement.Header);

        byte[] encoded = Codec.EncodeToArray(outer, payload);
        byte[] decoded = Codec.Decode(outer, encoded);

        Assert.Equal(payload, decoded);
    }

    [Fact(DisplayName = "Deeply nested codecs approaching MaxNestingDepth succeeds")]
    public void DeepNesting_NearMaxDepth_Succeeds()
    {
        // Nest LengthPrefixed 30 levels deep (default MaxNestingDepth=64)
        var codec = Codec.BytesOwnedRemaining();
        for (int i = 0; i < 30; i++)
            codec = Codec.LengthPrefixed(Codec.VarUInt32, codec, TrailingDataPolicy.Allow);

        byte[] payload = [0x42];
        byte[] encoded = Codec.EncodeToArray(codec, payload);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(payload, decoded);
    }

    [Fact(DisplayName = "Exceeding MaxNestingDepth throws DepthExceeded")]
    public void ExceedMaxNestingDepth_Throws()
    {
        var opts = new CodecOptions { MaxNestingDepth = 1 };

        // Nest 3 levels deep
        var codec = Codec.BytesOwnedRemaining();
        for (int i = 0; i < 3; i++)
            codec = Codec.LengthPrefixed(Codec.VarUInt32, codec, TrailingDataPolicy.Allow);

        byte[] payload = [0x01];
        byte[] encoded = Codec.EncodeToArray(codec, payload); // encode is fine

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(codec, encoded, options: opts));

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
    }

    [Property(MaxTest = 30)]
    public void FixedFrame_RandomSize_AndPayload_FitsOrThrowsAppropriately(int frameSize, byte[] payload)
    {
        // Constrain sizes to avoid extremes
        if (frameSize <= 0 || frameSize > 256) return;
        if (payload.Length > 256) return;

        var codec = Codec.FixedFrame(frameSize, Codec.BytesOwned(payload.Length),
            FramePadding.ZeroFill, TrailingDataPolicy.Allow);

        if (payload.Length <= frameSize)
        {
            byte[] encoded = Codec.EncodeToArray(codec, payload);
            byte[] decoded = Codec.Decode(codec, encoded);
            Assert.Equal(payload, decoded);
        }
        else
        {
            Assert.Throws<FrameOverflowException>(() =>
                Codec.EncodeToArray(codec, payload));
        }
    }
}
