using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.Text.Tests.Filters;
[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
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

    [Fact(DisplayName = "CachingTokenFilter: captures and replays graph metadata through a legacy filter")]
    public void CapturesAndReplaysGraphMetadataThroughLegacyFilter()
    {
        var synonyms = new SynonymMap();
        synonyms.Add("new york", ["NYC"]);
        var cache = new CachingTokenFilter();
        var analyser = new Analyser(
            new WhitespaceTokeniser(),
            new SynonymGraphFilter(synonyms),
            new LegacyPassThroughFilter(),
            new LowercaseFilter(),
            cache);
        var live = new MaterialisingTokenSink();

        analyser.Analyse("new york park", live);

        var liveGraph = MaterialiseGraph(live.Tokens);
        var capturedGraph = MaterialiseGraph(cache.Tokens);
        var expected = new[]
        {
            ("new", 0, 1, 0, 3, 1, 1),
            ("nyc", 0, 2, 0, 8, 0, 2),
            ("york", 1, 2, 4, 8, 1, 1),
            ("park", 2, 3, 9, 13, 1, 1)
        };

        Assert.Equal(expected, DescribeEdges(liveGraph));
        Assert.Equal(expected, DescribeEdges(capturedGraph));

        var replay = new MaterialisingTokenSink();
        capturedGraph.Emit(replay);
        Assert.Equal(expected, DescribeEdges(MaterialiseGraph(replay.Tokens)));
    }

    [Fact(DisplayName = "CachingTokenFilter: Clone creates independent capture state")]
    public void CloneCreatesIndependentCaptureState()
    {
        var original = new CachingTokenFilter();
        var sink = new MaterialisingTokenSink();
        original.Apply("alpha", 0, 5, "term", 1, null, sink);

        var clone = Assert.IsType<CachingTokenFilter>(original.Clone());
        clone.Apply("beta", 6, 10, "term", 1, null, sink);

        Assert.NotSame(original, clone);
        Assert.Equal(["alpha"], original.Tokens.Select(static token => token.Text));
        Assert.Equal(["beta"], clone.Tokens.Select(static token => token.Text));
    }

    private static TokenGraph MaterialiseGraph(IEnumerable<Token> tokens)
    {
        var graph = new TokenGraph();
        foreach (var token in tokens)
            graph.Add(token);

        graph.ValidateOrdered();
        return graph;
    }

    private static (string Text, int StartPosition, int EndPosition, int StartOffset, int EndOffset,
        int PositionIncrement, int PositionLength)[] DescribeEdges(TokenGraph graph) =>
        graph.Edges.Select(static edge => (
            edge.Token.Text,
            edge.StartPosition,
            edge.EndPosition,
            edge.Token.StartOffset,
            edge.Token.EndOffset,
            edge.Token.PositionIncrement,
            edge.Token.PositionLength)).ToArray();

    private sealed class LegacyPassThroughFilter : ISpanTokenFilter
    {
        public void Apply(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
            int positionIncrement, byte[]? payload, ISpanTokenSink sink)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
        }

        public ISpanTokenFilter Clone() => new LegacyPassThroughFilter();
    }
}
