namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Analysers;

/// <summary>
/// Unit tests for <see cref="CountingTokenSink"/>.
/// </summary>
[Trait("Category", "Analysis")]
[Trait("Category", "UnitTest")]
public sealed class CountingTokenSinkTests
{
    [Fact(DisplayName = "CountingTokenSink: Count starts at zero")]
    public void Count_StartsAtZero()
    {
        var sink = new CountingTokenSink();
        Assert.Equal(0, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Add increments Count")]
    public void Add_IncrementsCount()
    {
        var sink = new CountingTokenSink();
        sink.Add("hello".AsSpan(), 0, 5);
        Assert.Equal(1, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Multiple adds increment Count")]
    public void MultipleAdds_IncrementCount()
    {
        var sink = new CountingTokenSink();
        sink.Add("a".AsSpan(), 0, 1);
        sink.Add("b".AsSpan(), 0, 1);
        sink.Add("c".AsSpan(), 0, 1);
        Assert.Equal(3, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Reset clears Count to zero")]
    public void Reset_ClearsCount()
    {
        var sink = new CountingTokenSink();
        sink.Add("x".AsSpan(), 0, 1);
        Assert.Equal(1, sink.Count);
        sink.Reset();
        Assert.Equal(0, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Add after Reset starts from zero")]
    public void AddAfterReset_StartsFromZero()
    {
        var sink = new CountingTokenSink();
        sink.Add("x".AsSpan(), 0, 1);
        sink.Reset();
        sink.Add("y".AsSpan(), 0, 1);
        Assert.Equal(1, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Add with all parameters does not throw")]
    public void Add_WithAllParameters_DoesNotThrow()
    {
        var sink = new CountingTokenSink();
        sink.Add("token".AsSpan(), 0, 5, "word", 1, [1, 2, 3]);
        Assert.Equal(1, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Add with default parameters does not throw")]
    public void Add_WithDefaults_DoesNotThrow()
    {
        var sink = new CountingTokenSink();
        sink.Add("test".AsSpan(), 0, 4);
        Assert.Equal(1, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Empty token text increments Count")]
    public void EmptyToken_IncrementsCount()
    {
        var sink = new CountingTokenSink();
        sink.Add("".AsSpan(), 0, 0);
        Assert.Equal(1, sink.Count);
    }

    [Fact(DisplayName = "CountingTokenSink: Large number of tokens counting is correct")]
    public void LargeTokenCount_IsCorrect()
    {
        var sink = new CountingTokenSink();
        for (int i = 0; i < 1000; i++)
            sink.Add("x".AsSpan(), 0, 1);
        Assert.Equal(1000, sink.Count);
    }
}
