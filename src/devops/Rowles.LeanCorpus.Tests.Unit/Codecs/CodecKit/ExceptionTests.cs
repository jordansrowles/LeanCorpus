using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class ExceptionTests
{
    [Fact(DisplayName = "MagicMismatchException carries Expected and Actual magic")]
    public void MagicMismatchException_Properties()
    {
        byte[] expected = [0xDE, 0xAD];
        byte[] actual = [0xBE, 0xEF];
        var ex = new MagicMismatchException(0, "", expected, actual);

        Assert.Equal(CodecErrorCode.MagicMismatch, ex.ErrorCode);
        Assert.Equal(expected, ex.Expected);
        Assert.Equal(actual, ex.Actual);
    }

    [Fact(DisplayName = "InvalidPaddingException carries ExpectedByte")]
    public void InvalidPaddingException_SingleArg_Properties()
    {
        var ex = new InvalidPaddingException(10, "path", (byte)0xAB);

        Assert.Equal(CodecErrorCode.InvalidPadding, ex.ErrorCode);
        Assert.Equal(0xAB, ex.ExpectedByte);
        Assert.Null(ex.ActualByte);
    }

    [Fact(DisplayName = "InvalidPaddingException carries ExpectedByte and ActualByte")]
    public void InvalidPaddingException_TwoArgs_Properties()
    {
        var ex = new InvalidPaddingException(10, "path", (byte)0xAB, (byte)0xCD);

        Assert.Equal(CodecErrorCode.InvalidPadding, ex.ErrorCode);
        Assert.Equal((byte)0xAB, ex.ExpectedByte);
        Assert.Equal((byte?)0xCD, ex.ActualByte);
    }

    [Fact(DisplayName = "TrailingDataException carries TrailingBytes")]
    public void TrailingDataException_Properties()
    {
        var ex = new TrailingDataException(20, "path", 42);

        Assert.Equal(CodecErrorCode.TrailingData, ex.ErrorCode);
        Assert.Equal(42, ex.TrailingBytes);
    }

    [Fact(DisplayName = "UnknownDiscriminatorException carries DiscriminatorValue")]
    public void UnknownDiscriminatorException_Properties()
    {
        var ex = new UnknownDiscriminatorException(5, "path", 99);

        Assert.Equal(CodecErrorCode.UnknownDiscriminator, ex.ErrorCode);
        Assert.Equal((object)99, ex.DiscriminatorValue);
    }

    [Fact(DisplayName = "UnknownVersionException carries VersionValue")]
    public void UnknownVersionException_Properties()
    {
        var ex = new UnknownVersionException(0, "path", (byte)5);

        Assert.Equal(CodecErrorCode.UnknownVersion, ex.ErrorCode);
        Assert.Equal((byte)5, ex.VersionValue);
    }

    [Fact(DisplayName = "ChecksumMismatchException carries AlgorithmId, Expected, Actual")]
    public void ChecksumMismatchException_Properties()
    {
        byte[] expected = [0x01, 0x02, 0x03, 0x04];
        byte[] actual = [0xFF, 0xFF, 0xFF, 0xFF];
        var ex = new ChecksumMismatchException(0, "path", "crc32", expected, actual);

        Assert.Equal(CodecErrorCode.ChecksumMismatch, ex.ErrorCode);
        Assert.Equal("crc32", ex.AlgorithmId);
        Assert.Equal(expected, ex.ExpectedChecksum);
        Assert.Equal(actual, ex.ActualChecksum);
    }

    [Fact(DisplayName = "InvalidBooleanException carries ActualValue")]
    public void InvalidBooleanException_Properties()
    {
        var ex = new InvalidBooleanException(0, "", 0xFF);

        Assert.Equal(CodecErrorCode.InvalidBoolean, ex.ErrorCode);
        Assert.Equal(0xFF, ex.ActualValue);
    }

    [Fact(DisplayName = "InvalidUtf8Exception has correct ErrorCode")]
    public void InvalidUtf8Exception_Properties()
    {
        var ex = new InvalidUtf8Exception(0, "");

        Assert.Equal(CodecErrorCode.InvalidUtf8, ex.ErrorCode);
    }

    [Fact(DisplayName = "LimitExceededException carries LimitName, ActualValue, LimitValue")]
    public void LimitExceededException_WithLimit_Properties()
    {
        var ex = new LimitExceededException(CodecErrorCode.DepthExceeded, 0, "", "MaxNestingDepth", 65, 64);

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
        Assert.Equal("MaxNestingDepth", ex.LimitName);
        Assert.Equal(65, ex.ActualValue);
        Assert.Equal(64, ex.LimitValue);
    }

    [Fact(DisplayName = "LimitExceededException with description sets LimitValue to -1")]
    public void LimitExceededException_WithDescription_Properties()
    {
        var ex = new LimitExceededException(CodecErrorCode.DecompressionLimitExceeded, 0, "", "DecompressedBytes", 500_000_000, "256MB max");

        Assert.Equal(CodecErrorCode.DecompressionLimitExceeded, ex.ErrorCode);
        Assert.Equal("DecompressedBytes", ex.LimitName);
        Assert.Equal(500_000_000, ex.ActualValue);
        Assert.Equal(-1, ex.LimitValue);
    }

    [Fact(DisplayName = "FrameOverflowException carries FrameSize and PayloadSize")]
    public void FrameOverflowException_Properties()
    {
        var ex = new FrameOverflowException(0, "", 100, 150);

        Assert.Equal(CodecErrorCode.FrameOverflow, ex.ErrorCode);
        Assert.Equal(100, ex.FrameSize);
        Assert.Equal(150, ex.PayloadSize);
    }

    [Fact(DisplayName = "CodecValidationException carries ValidationMessage")]
    public void CodecValidationException_Properties()
    {
        var ex = new CodecValidationException(0, "", "value must be positive");

        Assert.Equal(CodecErrorCode.ValidationFailed, ex.ErrorCode);
        Assert.Equal("value must be positive", ex.ValidationMessage);
    }

    [Fact(DisplayName = "InsufficientDataException carries ExpectedBytes and AvailableBytes")]
    public void InsufficientDataException_Properties()
    {
        var ex = new InsufficientDataException(0, "", 8, 3);

        Assert.Equal(CodecErrorCode.Truncated, ex.ErrorCode);
        Assert.Equal(8, ex.ExpectedBytes);
        Assert.Equal(3, ex.AvailableBytes);
    }

    [Fact(DisplayName = "CodecException ByteOffset and Path are preserved")]
    public void CodecException_ByteOffsetAndPath_Preserved()
    {
        var ex = new InsufficientDataException(42, "Header > Body", 4, 1);

        Assert.Equal(42, ex.ByteOffset);
        Assert.Equal("Header > Body", ex.Path);
    }

    [Fact(DisplayName = "All exception types inherit from CodecException")]
    public void AllExceptionTypes_InheritFromCodecException()
    {
        Assert.IsAssignableFrom<CodecException>(new CodecFormatException(CodecErrorCode.MagicMismatch, 0, "", ""));
        Assert.IsAssignableFrom<CodecException>(new MagicMismatchException(0, "", [], []));
        Assert.IsAssignableFrom<CodecException>(new InvalidPaddingException(0, "", 0x00));
        Assert.IsAssignableFrom<CodecException>(new TrailingDataException(0, "", 0));
        Assert.IsAssignableFrom<CodecException>(new UnknownDiscriminatorException(0, "", 0));
        Assert.IsAssignableFrom<CodecException>(new UnknownVersionException(0, "", 1));
        Assert.IsAssignableFrom<CodecException>(new CodecIntegrityException(CodecErrorCode.ChecksumMismatch, 0, "", ""));
        Assert.IsAssignableFrom<CodecException>(new ChecksumMismatchException(0, "", "", [], []));
        Assert.IsAssignableFrom<CodecException>(new CodecValidationException(CodecErrorCode.InvalidBoolean, 0, "", ""));
        Assert.IsAssignableFrom<CodecException>(new InvalidBooleanException(0, "", 0x02));
        Assert.IsAssignableFrom<CodecException>(new InvalidUtf8Exception(0, ""));
        Assert.IsAssignableFrom<CodecException>(new LimitExceededException(CodecErrorCode.DepthExceeded, 0, "", "", 1, 1));
        Assert.IsAssignableFrom<CodecException>(new FrameOverflowException(0, "", 100, 200));
        Assert.IsAssignableFrom<CodecException>(new CodecValidationException(0, "", ""));
        Assert.IsAssignableFrom<CodecException>(new InsufficientDataException(0, "", 1, 0));
    }
}
