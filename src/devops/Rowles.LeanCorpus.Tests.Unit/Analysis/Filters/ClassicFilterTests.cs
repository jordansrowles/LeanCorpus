using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Filters;

[Trait("Category", "Analysis")]
public class ClassicFilterTests
{
    [Fact(DisplayName = "ClassicFilter: plain tokens pass through unchanged")]
    public void PlainTokensPassThroughUnchanged()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("quick", 0, 5, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(5, tokens[0].EndOffset);
    }

    [Fact(DisplayName = "ClassicFilter: strips trailing possessive 's")]
    public void StripsTrailingPossessive()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("dogs'", 0, 5, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("dogs", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: strips trailing possessive with right quote")]
    public void StripsTrailingPossessiveWithRightQuote()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("O\u2019Reilly\u2019s", 0, 12, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("O\u2019Reilly", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: removes periods from acronyms")]
    public void RemovesPeriodsFromAcronyms()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("U.S.A.", 0, 6, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("USA", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: strips possessive and periods from acronym with possessive")]
    public void StripsPossessiveAndPeriodsFromAcronymWithPossessive()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("I.B.M.'s", 0, 8, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("IBM", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: does not strip periods from lowercase tokens")]
    public void DoesNotStripPeriodsFromLowercase()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("v1.2", 0, 4, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("v1.2", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: single character token passes through")]
    public void SingleCharacterPassesThrough()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("A", 0, 1, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("A", tokens[0].Text);
    }

    [Fact(DisplayName = "ClassicFilter: token without special chars passes through zero allocation")]
    public void TokenWithoutSpecialCharsPassesThrough()
    {
        var filter = new ClassicFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("hello", 0, 5, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("hello", tokens[0].Text);
    }
}
