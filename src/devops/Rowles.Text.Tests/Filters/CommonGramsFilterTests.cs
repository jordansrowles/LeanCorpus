using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.Text.Tests.Filters;
[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
public class CommonGramsFilterTests
{
    [Fact(DisplayName = "CommonGramsFilter: emits bigram for two consecutive common words")]
    public void EmitsBigramForTwoConsecutiveCommonWords()
    {
        var filter = new CommonGramsFilter(["the", "quick"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("quick", 4, 9, "term", 1, null, sink);
        filter.Finish(sink);

        var graph = MaterialiseGraph(sink.Tokens);

        Assert.Equal(
            [
                ("the", 0, 1, 0, 3, 1, 1),
                ("the_quick", 0, 2, 0, 9, 0, 2),
                ("quick", 1, 2, 4, 9, 1, 1)
            ],
            DescribeEdges(graph));
    }

    [Fact(DisplayName = "CommonGramsFilter: emits adjacent bigrams across three common words")]
    public void EmitsAdjacentBigramsAcrossThreeCommonWords()
    {
        var filter = new CommonGramsFilter(["the", "quick", "brown"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("quick", 4, 9, "term", 1, null, sink);
        filter.Apply("brown", 10, 15, "term", 1, null, sink);
        filter.Finish(sink);

        var graph = MaterialiseGraph(sink.Tokens);

        Assert.Equal(
            [
                ("the", 0, 1, 0, 3, 1, 1),
                ("the_quick", 0, 2, 0, 9, 0, 2),
                ("quick", 1, 2, 4, 9, 1, 1),
                ("quick_brown", 1, 3, 4, 15, 0, 2),
                ("brown", 2, 3, 10, 15, 1, 1)
            ],
            DescribeEdges(graph));
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

    [Fact(DisplayName = "CommonGramsFilter: common and non-common transitions keep unit edges")]
    public void CommonAndNonCommonTransitionsKeepUnitEdges()
    {
        var filter = new CommonGramsFilter(["the", "quick"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("fox", 4, 7, "term", 1, null, sink);
        filter.Apply("quick", 8, 13, "term", 1, null, sink);
        filter.Finish(sink);

        var graph = MaterialiseGraph(sink.Tokens);

        Assert.Equal(
            [
                ("the", 0, 1, 0, 3, 1, 1),
                ("fox", 1, 2, 4, 7, 1, 1),
                ("quick", 2, 3, 8, 13, 1, 1)
            ],
            DescribeEdges(graph));
    }

    [Fact(DisplayName = "CommonGramsFilter: non-unit incoming increment anchors both graph edges")]
    public void NonUnitIncomingIncrementAnchorsBothGraphEdges()
    {
        var filter = new CommonGramsFilter(["the", "quick"]);
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 2, null, sink);
        filter.Apply("quick", 4, 9, "term", 1, null, sink);
        filter.Finish(sink);

        var graph = MaterialiseGraph(sink.Tokens);

        Assert.Equal(
            [
                ("the", 1, 2, 0, 3, 2, 1),
                ("the_quick", 1, 3, 0, 9, 0, 2),
                ("quick", 2, 3, 4, 9, 1, 1)
            ],
            DescribeEdges(graph));
    }

    [Fact(DisplayName = "CommonGramsFilter: graph replay preserves edges and flattening makes unit edges")]
    public void GraphReplayPreservesEdgesAndFlatteningMakesUnitEdges()
    {
        var filter = new CommonGramsFilter(["the", "quick"]);
        var source = new MaterialisingTokenSink();
        filter.Apply("the", 0, 3, "term", 1, null, source);
        filter.Apply("quick", 4, 9, "term", 1, null, source);
        filter.Finish(source);

        var graph = MaterialiseGraph(source.Tokens);
        var replay = new MaterialisingTokenSink();
        graph.Emit(replay);
        var replayedGraph = MaterialiseGraph(replay.Tokens);

        Assert.Equal(DescribeEdges(graph), DescribeEdges(replayedGraph));

        var flattened = new MaterialisingTokenSink();
        var flattenFilter = new FlattenGraphFilter();
        foreach (var token in replay.Tokens)
        {
            flattenFilter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                token.Type, token.PositionIncrement, token.PositionLength, token.Payload, flattened);
        }
        flattenFilter.Finish(flattened);
        var flattenedGraph = MaterialiseGraph(flattened.Tokens);

        Assert.Equal(["the", "the_quick", "quick"], flattened.Tokens.Select(static token => token.Text));
        Assert.Equal([1, 0, 1], flattened.Tokens.Select(static token => token.PositionIncrement));
        Assert.All(flattenedGraph.Edges, static edge => Assert.Equal(1, edge.Token.PositionLength));
        Assert.Equal(
            [("the", 0, 1), ("the_quick", 0, 1), ("quick", 1, 2)],
            flattenedGraph.Edges.Select(static edge => (edge.Token.Text, edge.StartPosition, edge.EndPosition)));
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
        Assert.Equal(3, tokens.Count);
        Assert.Equal(["the", "the_QUICK", "QUICK"], tokens.Select(static token => token.Text));
        Assert.Equal([1, 0, 1], tokens.Select(static token => token.PositionIncrement));
        Assert.Equal([1, 2, 1], tokens.Select(static token => token.PositionLength));
    }

    [Fact(DisplayName = "CommonGramsFilter: custom separator is respected")]
    public void CustomSeparatorIsRespected()
    {
        var filter = new CommonGramsFilter(["the", "quick"], separator: " ");
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, "term", 1, null, sink);
        filter.Apply("quick", 4, 9, "term", 1, null, sink);
        filter.Finish(sink);

        Assert.Equal("the quick", sink.Tokens[1].Text);
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
}
