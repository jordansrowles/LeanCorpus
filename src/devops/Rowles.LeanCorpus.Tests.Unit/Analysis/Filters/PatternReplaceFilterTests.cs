using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Filters;

[Trait("Category", "Analysis")]
public class PatternReplaceFilterTests
{
    [Fact(DisplayName = "PatternReplaceFilter: replaces digits with a placeholder")]
    public void ReplacesDigitsWithPlaceholder()
    {
        var filter = new PatternReplaceFilter("[0-9]+", "#");
        var sink = new MaterialisingTokenSink();

        filter.Apply("call12345now", 0, 12, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("call#now", tokens[0].Text);
    }

    [Fact(DisplayName = "PatternReplaceFilter: no-op when pattern does not match")]
    public void NoOpWhenPatternDoesNotMatch()
    {
        var filter = new PatternReplaceFilter("[0-9]+", "#");
        var sink = new MaterialisingTokenSink();

        filter.Apply("hello", 0, 5, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("hello", tokens[0].Text);
    }

    [Fact(DisplayName = "PatternReplaceFilter: supports regex group substitution")]
    public void SupportsRegexGroupSubstitution()
    {
        var filter = new PatternReplaceFilter("(foo)(bar)", "$2$1");
        var sink = new MaterialisingTokenSink();

        filter.Apply("foobar", 0, 6, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("barfoo", tokens[0].Text);
    }

    [Fact(DisplayName = "PatternReplaceFilter: empty replacement removes matches")]
    public void EmptyReplacementRemovesMatches()
    {
        var filter = new PatternReplaceFilter("[aeiou]", "");
        var sink = new MaterialisingTokenSink();

        filter.Apply("hello", 0, 5, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("hll", tokens[0].Text);
    }

    [Fact(DisplayName = "PatternReplaceFilter: pre-compiled regex constructor works")]
    public void PreCompiledRegexConstructorWorks()
    {
        var regex = new System.Text.RegularExpressions.Regex(
            @"\s+",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var filter = new PatternReplaceFilter(regex, "-");
        var sink = new MaterialisingTokenSink();

        filter.Apply("hello world", 0, 11, "term", 1, null, sink);

        var tokens = sink.Tokens;
        Assert.Single(tokens);
        Assert.Equal("hello-world", tokens[0].Text);
    }
}
