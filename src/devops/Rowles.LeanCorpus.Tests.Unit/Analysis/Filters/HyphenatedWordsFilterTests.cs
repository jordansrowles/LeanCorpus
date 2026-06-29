using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Filters;

[Trait("Category", "Analysis")]
public class HyphenatedWordsFilterTests
{
    [Fact(DisplayName = "HyphenatedWordsFilter: joins tokens at the same position")]
    public void JoinsTokensAtSamePosition()
    {
        var filter = new HyphenatedWordsFilter();
        var sink = new MaterialisingTokenSink();

        // First token at position 0
        filter.Apply("state", 0, 5, "term", 1, null, sink);

        // Subsequent tokens at same position (posInc=0)
        filter.Apply("of", 6, 8, "term", 0, null, sink);
        filter.Apply("the", 9, 12, "term", 0, null, sink);
        filter.Apply("art", 13, 16, "term", 0, null, sink);

        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("state-of-the-art", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(16, tokens[0].EndOffset);
    }

    [Fact(DisplayName = "HyphenatedWordsFilter: tokens at different positions are not joined")]
    public void TokensAtDifferentPositionsNotJoined()
    {
        var filter = new HyphenatedWordsFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("hello", 0, 5, "term", 1, null, sink);
        filter.Apply("world", 6, 11, "term", 1, null, sink);
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    [Fact(DisplayName = "HyphenatedWordsFilter: single token passes through unchanged")]
    public void SingleTokenPassesThroughUnchanged()
    {
        var filter = new HyphenatedWordsFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("word", 0, 4, "term", 1, null, sink);
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("word", tokens[0].Text);
    }

    [Fact(DisplayName = "HyphenatedWordsFilter: empty input produces nothing")]
    public void EmptyInputProducesNothing()
    {
        var filter = new HyphenatedWordsFilter();
        var sink = new MaterialisingTokenSink();

        filter.Finish(sink);

        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "HyphenatedWordsFilter: custom separator is respected")]
    public void CustomSeparatorIsRespected()
    {
        var filter = new HyphenatedWordsFilter(separator: '+');
        var sink = new MaterialisingTokenSink();

        filter.Apply("a", 0, 1, "term", 1, null, sink);
        filter.Apply("b", 1, 2, "term", 0, null, sink);
        filter.Finish(sink);

        Assert.Equal("a+b", sink.Tokens[0].Text);
    }

    [Fact(DisplayName = "HyphenatedWordsFilter: multiple groups at same position")]
    public void MultipleGroupsAtSamePosition()
    {
        var filter = new HyphenatedWordsFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("one", 0, 3, "term", 1, null, sink);
        filter.Apply("two", 3, 6, "term", 0, null, sink);
        filter.Apply("three", 6, 11, "term", 1, null, sink);
        filter.Apply("four", 11, 15, "term", 0, null, sink);
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("one-two", tokens[0].Text);
        Assert.Equal("three-four", tokens[1].Text);
    }
}
