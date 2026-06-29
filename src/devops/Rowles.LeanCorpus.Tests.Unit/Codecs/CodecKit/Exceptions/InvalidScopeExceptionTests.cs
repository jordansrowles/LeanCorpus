namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Exceptions;

[Trait("Category", "CodecKit")]
public sealed class InvalidScopeExceptionTests
{
    [Fact(DisplayName = "InvalidScopeException: Constructor sets ErrorCode to InvalidScope")]
    public void Constructor_SetsErrorCode()
    {
        var ex = new InvalidScopeException(42, "/root/block", "No active scope");
        Assert.Equal(CodecErrorCode.InvalidScope, ex.ErrorCode);
    }

    [Fact(DisplayName = "InvalidScopeException: Constructor sets ByteOffset correctly")]
    public void Constructor_SetsByteOffset()
    {
        var ex = new InvalidScopeException(99, "/root/block", "No active scope");
        Assert.Equal(99, ex.ByteOffset);
    }

    [Fact(DisplayName = "InvalidScopeException: Constructor sets Path correctly")]
    public void Constructor_SetsPath()
    {
        var ex = new InvalidScopeException(0, "/data/segment", "No active scope");
        Assert.Equal("/data/segment", ex.Path);
    }

    [Fact(DisplayName = "InvalidScopeException: Constructor sets Message correctly")]
    public void Constructor_SetsMessage()
    {
        var ex = new InvalidScopeException(5, "test", "Custom scope message");
        Assert.Equal("Custom scope message", ex.Message);
    }

    [Fact(DisplayName = "InvalidScopeException: Inherits from CodecResourceException")]
    public void Inherits_FromCodecResourceException()
    {
        var ex = new InvalidScopeException(0, "", "msg");
        Assert.IsAssignableFrom<CodecResourceException>(ex);
    }

    [Fact(DisplayName = "InvalidScopeException: Inherits from CodecException")]
    public void Inherits_FromCodecException()
    {
        var ex = new InvalidScopeException(0, "", "msg");
        Assert.IsAssignableFrom<CodecException>(ex);
    }

    [Fact(DisplayName = "InvalidScopeException: Can be caught as CodecException")]
    public void Catch_AsCodecException()
    {
        InvalidScopeException thrown = null!;
        try
        {
            throw new InvalidScopeException(10, "path", "test");
        }
        catch (CodecException ex) when (ex is InvalidScopeException ise)
        {
            thrown = ise;
        }

        Assert.NotNull(thrown);
        Assert.Equal(CodecErrorCode.InvalidScope, thrown.ErrorCode);
    }

    [Fact(DisplayName = "InvalidScopeException: ToString includes the message")]
    public void ToString_IncludesMessage()
    {
        var ex = new InvalidScopeException(0, "", "critical error");
        Assert.Contains("critical error", ex.ToString());
    }

    [Fact(DisplayName = "InvalidScopeException: Can be thrown and caught as Exception")]
    public void ThrowAndCatch_AsException()
    {
        Exception caught = null!;
        try
        {
            throw new InvalidScopeException(7, "offset", "out of range");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsType<InvalidScopeException>(caught);
        Assert.Equal(CodecErrorCode.InvalidScope, ((InvalidScopeException)caught).ErrorCode);
    }
}
