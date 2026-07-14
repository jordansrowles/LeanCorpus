namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Exceptions;

[Trait("Category", "CodecKit")]
public sealed class UserCodeExceptionTests
{
    [Fact(DisplayName = "UserCodeException: Constructor sets ErrorCode to UserCodeFailed")]
    public void Constructor_SetsErrorCode()
    {
        var inner = new InvalidOperationException("oops");
        var ex = new UserCodeException(42, "/record/build", inner);
        Assert.Equal(CodecErrorCode.UserCodeFailed, ex.ErrorCode);
    }

    [Fact(DisplayName = "UserCodeException: Constructor sets ByteOffset correctly")]
    public void Constructor_SetsByteOffset()
    {
        var inner = new InvalidOperationException("fail");
        var ex = new UserCodeException(100, "path", inner);
        Assert.Equal(100, ex.ByteOffset);
    }

    [Fact(DisplayName = "UserCodeException: Constructor sets Path correctly")]
    public void Constructor_SetsPath()
    {
        var inner = new InvalidOperationException("fail");
        var ex = new UserCodeException(0, "/root/map", inner);
        Assert.Equal("/root/map", ex.Path);
    }

    [Fact(DisplayName = "UserCodeException: InnerException is preserved")]
    public void Constructor_PreservesInnerException()
    {
        var inner = new ArgumentException("original cause");
        var ex = new UserCodeException(10, "p", inner);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact(DisplayName = "UserCodeException: Message contains the offset")]
    public void Message_ContainsOffset()
    {
        var inner = new InvalidOperationException("bad data");
        var ex = new UserCodeException(77, "path", inner);
        Assert.Contains("77", ex.Message);
    }

    [Fact(DisplayName = "UserCodeException: Message contains the inner exception message")]
    public void Message_ContainsInnerExceptionMessage()
    {
        var inner = new InvalidOperationException("bad data");
        var ex = new UserCodeException(0, "path", inner);
        Assert.Contains("bad data", ex.Message);
    }

    [Fact(DisplayName = "UserCodeException: Inherits from CodecException")]
    public void Inherits_FromCodecException()
    {
        var inner = new Exception("inner");
        var ex = new UserCodeException(0, "", inner);
        Assert.IsAssignableFrom<CodecException>(ex);
    }

    [Fact(DisplayName = "UserCodeException: Can be caught as CodecException")]
    public void Catch_AsCodecException()
    {
        UserCodeException thrown = null!;
        try
        {
            throw new UserCodeException(5, "map", new Exception("cause"));
        }
        catch (CodecException ex) when (ex is UserCodeException uce)
        {
            thrown = uce;
        }

        Assert.NotNull(thrown);
        Assert.Equal(CodecErrorCode.UserCodeFailed, thrown.ErrorCode);
    }

    [Fact(DisplayName = "UserCodeException: Can be thrown and caught")]
    public void ThrowAndCatch()
    {
        Exception caught = null!;
        try
        {
            throw new UserCodeException(3, "decoder", new Exception("inner fail"));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsType<UserCodeException>(caught);
        var uce = (UserCodeException)caught;
        Assert.Equal(CodecErrorCode.UserCodeFailed, uce.ErrorCode);
        Assert.NotNull(uce.InnerException);
    }
}
