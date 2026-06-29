using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecResultAndFailureTests
{
    [Fact(DisplayName = "CodecResult.Success produces IsSuccess with correct Value")]
    public void Success_ReturnsCorrectValue()
    {
        var result = CodecResult<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact(DisplayName = "CodecResult.Fail(CodecFailure) produces IsFailure with correct Failure")]
    public void Fail_WithCodecFailure_ReturnsCorrectFailure()
    {
        var failure = new CodecFailure(CodecErrorCode.Truncated, 10, "test", "Insufficient data");

        var result = CodecResult<int>.Fail(failure);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(failure, result.Failure);
        Assert.Equal(CodecErrorCode.Truncated, result.Failure.Code);
        Assert.Equal(10, result.Failure.ByteOffset);
        Assert.Equal("test", result.Failure.Path);
        Assert.Equal("Insufficient data", result.Failure.Message);
    }

    [Fact(DisplayName = "CodecResult.Fail(CodecException) extracts ErrorCode and properties")]
    public void Fail_WithCodecException_ExtractsProperties()
    {
        var ex = new InsufficientDataException(5, "path", 8, 2);

        var result = CodecResult<int>.Fail(ex);

        Assert.True(result.IsFailure);
        Assert.Equal(CodecErrorCode.Truncated, result.Failure.Code);
        Assert.Equal(5, result.Failure.ByteOffset);
        Assert.Equal("path", result.Failure.Path);
        Assert.Same(ex, result.Failure.InnerException);
    }

    [Fact(DisplayName = "Accessing Value on failure throws InvalidOperationException")]
    public void Value_OnFailure_Throws()
    {
        var result = CodecResult<int>.Fail(new CodecFailure(CodecErrorCode.Truncated, 0, "", "fail"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact(DisplayName = "Accessing Failure on success throws InvalidOperationException")]
    public void Failure_OnSuccess_Throws()
    {
        var result = CodecResult<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Failure);
    }

    [Fact(DisplayName = "CodecFailure is immutable record")]
    public void CodecFailure_IsRecord()
    {
        var a = new CodecFailure(CodecErrorCode.Truncated, 0, "", "msg");
        var b = new CodecFailure(CodecErrorCode.Truncated, 0, "", "msg");
        var c = new CodecFailure(CodecErrorCode.Truncated, 1, "", "msg");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact(DisplayName = "CodecFailure carries InnerException")]
    public void CodecFailure_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var failure = new CodecFailure(CodecErrorCode.Truncated, 0, "p", "m", inner);

        Assert.Same(inner, failure.InnerException);
    }

    [Fact(DisplayName = "Codec.TryDecode returns CodecResult.Success for valid data")]
    public void TryDecode_ValidData_ReturnsSuccess()
    {
        byte[] data = [0x01];
        var result = Codec.TryDecode(Codec.Bool, data);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact(DisplayName = "Codec.TryDecode returns CodecResult.Fail for invalid data")]
    public void TryDecode_InvalidData_ReturnsFailure()
    {
        byte[] data = [0x02];
        var result = Codec.TryDecode(Codec.Bool, data);

        Assert.True(result.IsFailure);
        Assert.Equal(CodecErrorCode.InvalidBoolean, result.Failure.Code);
    }
}
