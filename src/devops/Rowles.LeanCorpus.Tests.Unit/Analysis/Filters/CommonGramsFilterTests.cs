using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Filters;

[Trait("Category", "Analysis")]
public class CommonGramsFilterTests
{
    [Fact(DisplayName = "CommonGramsFilter: emits bigram for two consecutive common words")]
    public void EmitsBigramForTwoConsecutiveCommonWords()
    {
        var filter = new CommonGramsFilter(["the", "quick"]);
        var sink = new MaterialisingTokenSink();

        // "the" → buffered (common)
        filter.Apply("the", 0, 3, "term", 1, null, sink);
        Assert.Empty(sink.Tokens);

        // "quick" → also common → bigram "the_quick" + "the" + buffer "quick"
        filter.Apply("quick", 4, 9, "term", 1, null, sink);

        // "brown" → not common → emit "quick" + buffer "brown"
        filter.Apply("brown", 10, 15, "term", 1, null, sink);

        // Finish → emit "brown"
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Equal(4, tokens.Count);
        Assert.Equal("the_quick", tokens[0].Text);
        Assert.Equal(0, tokens[0].PositionIncrement); // bigram at same position
        Assert.Equal("the", tokens[1].Text);
        Assert.Equal(1, tokens[1].PositionIncrement);
        Assert.Equal("quick", tokens[2].Text);
        Assert.Equal(1, tokens[2].PositionIncrement);
        Assert.Equal("brown", tokens[3].Text);
    }

    [Fact(DisplayName = "CommonGramsFilter: no bigram when only one of two is common")]
    public void NoBigramWhenOnlyOneCommon()
    {
        var filter = new CommonGramsFilter(["the"]);
        var sink = new MaterialisingTokenSink();

        // "the" → common → buffered
        filter.Apply("the", 0, 3, "term", 1, null, sink);

        // "fox" → not common → emit "the" (previous), buffer "fox"
        filter.Apply("fox", 4, 7, "term", 1, null, sink);

        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("the", tokens[0].Text);
        Assert.Equal("fox", tokens[1].Text);
    }

    [Fact(DisplayName = "CommonGramsFilter: common word not in set passes through")]
    public void CommonWordNotInSetPassesThrough()
    {
        var filter = new CommonGramsFilter(["the"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("quick", 0, 5, "term", 1, null, sink);
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("quick", tokens[0].Text);
    }

    [Fact(DisplayName = "CommonGramsFilter: empty input produces nothing")]
    public void EmptyInputProducesNothing()
    {
        var filter = new CommonGramsFilter(["the"]);
        var sink = new MaterialisingTokenSink();

        filter.Finish(sink);

        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "CommonGramsFilter: case-insensitive common word matching")]
    public void CaseInsensitiveCommonWordMatching()
    {
        var filter = new CommonGramsFilter(["THE", "Quick"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("QUICK", 4, 9, "term", 1, null, sink);
        filter.Finish(sink);

        var tokens = sink.Tokens;
        Assert.Equal(3, tokens.Count); // bigram + the + QUICK
        Assert.Equal("the_QUICK", tokens[0].Text);
    }

    [Fact(DisplayName = "CommonGramsFilter: custom separator is respected")]
    public void CustomSeparatorIsRespected()
    {
        var filter = new CommonGramsFilter(["the", "quick"], separator: " ");
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("quick", 4, 9, "term", 1, null, sink);
        filter.Finish(sink);

        Assert.Equal("the quick", sink.Tokens[0].Text);
    }
}
