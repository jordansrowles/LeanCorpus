using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecOptionsTests
{
    [Fact(DisplayName = "Default options match expected constants")]
    public void Default_HasExpectedValues()
    {
        var opts = CodecOptions.Default;

        Assert.Equal(64 * 1024 * 1024, opts.MaxFrameBytes);
        Assert.Equal(1_000_000, opts.MaxSequenceElements);
        Assert.Equal(16 * 1024 * 1024, opts.MaxStringBytes);
        Assert.Equal(64 * 1024 * 1024, opts.MaxScratchBufferBytes);
        Assert.Equal(256 * 1024 * 1024, opts.MaxDecompressedBytes);
        Assert.Equal(64, opts.MaxNestingDepth);
        Assert.True(opts.RejectNonCanonicalVarInts);
        Assert.Equal(Utf8ValidationMode.Strict, opts.Utf8Validation);
    }

    [Fact(DisplayName = "Default is always the same instance")]
    public void Default_IsSameInstance()
    {
        Assert.Same(CodecOptions.Default, CodecOptions.Default);
    }

    [Fact(DisplayName = "MaxFrameBytes ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxFrameBytes_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxFrameBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxFrameBytes = -1 });
    }

    [Fact(DisplayName = "MaxSequenceElements ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxSequenceElements_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxSequenceElements = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxSequenceElements = -1 });
    }

    [Fact(DisplayName = "MaxStringBytes ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxStringBytes_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxStringBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxStringBytes = -1 });
    }

    [Fact(DisplayName = "MaxScratchBufferBytes ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxScratchBufferBytes_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxScratchBufferBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxScratchBufferBytes = -1 });
    }

    [Fact(DisplayName = "MaxDecompressedBytes ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxDecompressedBytes_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxDecompressedBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxDecompressedBytes = -1 });
    }

    [Fact(DisplayName = "MaxNestingDepth ≤ 0 throws ArgumentOutOfRangeException")]
    public void MaxNestingDepth_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxNestingDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodecOptions { MaxNestingDepth = -1 });
    }

    [Fact(DisplayName = "RejectNonCanonicalVarInts can be set to false")]
    public void RejectNonCanonicalVarInts_CanBeFalse()
    {
        var opts = new CodecOptions { RejectNonCanonicalVarInts = false };
        Assert.False(opts.RejectNonCanonicalVarInts);
    }

    [Fact(DisplayName = "Utf8Validation can be set to Replace")]
    public void Utf8Validation_CanBeLenient()
    {
        var opts = new CodecOptions { Utf8Validation = Utf8ValidationMode.Replace };
        Assert.Equal(Utf8ValidationMode.Replace, opts.Utf8Validation);
    }

    [Fact(DisplayName = "Equality works as record")]
    public void Options_RecordEquality()
    {
        var a = new CodecOptions { MaxNestingDepth = 32 };
        var b = new CodecOptions { MaxNestingDepth = 32 };
        var c = new CodecOptions { MaxNestingDepth = 16 };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
