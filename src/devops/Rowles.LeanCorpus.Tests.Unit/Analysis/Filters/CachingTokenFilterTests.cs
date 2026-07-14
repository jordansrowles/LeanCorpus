using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Filters;

[Trait("Category", "Analysis")]
public class CachingTokenFilterTests
{
    [Fact(DisplayName = "CachingTokenFilter: captures tokens as they pass through")]
    public void CapturesTokensAsTheyPassThrough()
    {
        var filter = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("alpha", 0, 5, "term", 1, null, sink);
        filter.Apply("beta", 6, 10, "term", 1, null, sink);
        filter.Apply("gamma", 11, 16, "term", 1, null, sink);

        // Tokens should be forwarded to sink AND captured in filter.
        Assert.Equal(3, sink.Tokens.Count);
        Assert.Equal("alpha", sink.Tokens[0].Text);
        Assert.Equal("beta", sink.Tokens[1].Text);
        Assert.Equal("gamma", sink.Tokens[2].Text);

        Assert.Equal(3, filter.Tokens.Count);
        Assert.Equal("alpha", filter.Tokens[0].Text);
        Assert.Equal("beta", filter.Tokens[1].Text);
        Assert.Equal("gamma", filter.Tokens[2].Text);
    }

    [Fact(DisplayName = "CachingTokenFilter: captured tokens have correct offsets")]
    public void CapturedTokensHaveCorrectOffsets()
    {
        var filter = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("test", 10, 14, "term", 1, null, sink);

        Assert.Equal(10, filter.Tokens[0].StartOffset);
        Assert.Equal(14, filter.Tokens[0].EndOffset);
    }

    [Fact(DisplayName = "CachingTokenFilter: Reset clears captured tokens")]
    public void ResetClearsCapturedTokens()
    {
        var filter = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("first", 0, 5, "term", 1, null, sink);
        Assert.Single(filter.Tokens);

        filter.Reset();
        Assert.Empty(filter.Tokens);

        filter.Apply("second", 0, 6, "term", 1, null, sink);
        Assert.Single(filter.Tokens);
        Assert.Equal("second", filter.Tokens[0].Text);
    }

    [Fact(DisplayName = "CachingTokenFilter: captured token text is independent snapshot")]
    public void CapturedTokenTextIsIndependentSnapshot()
    {
        var filter = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();

        // Use a mutable span source to verify text is materialised
        filter.Apply("immutable", 0, 9, "term", 1, null, sink);

        string captured = filter.Tokens[0].Text;
        Assert.Equal("immutable", captured);
    }

    [Fact(DisplayName = "CachingTokenFilter: position increment is captured")]
    public void PositionIncrementIsCaptured()
    {
        var filter = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("token", 0, 5, "term", 3, null, sink);

        Assert.Equal(3, filter.Tokens[0].PositionIncrement);
    }
}
