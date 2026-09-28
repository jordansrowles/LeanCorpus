using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserAllocationShapeTests
{
    [Fact(DisplayName = "Query lexer: common term text remains backed by the source until consumed")]
    public void Lexer_DefersMaterialisingPlainTermText()
    {
        const string queryText = "quick brown";
        var lexer = new QueryLexer(QueryParserOptions.Default, limitsAreComplexity: false);

        List<QueryToken> tokens = lexer.Lex(queryText);

        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsSourceBackedValue);
        Assert.Equal(new QuerySourceSpan(0, 5), tokens[0].SourceSpan);
        Assert.Equal("quick", tokens[0].Value);
        Assert.True(tokens[0].IsSourceBackedValue);
        Assert.Equal("brown", tokens[1].Value);

        QueryToken escaped = lexer.Lex(@"quick\*")[0];
        Assert.False(escaped.IsSourceBackedValue);
        Assert.Equal(@"quick\*", escaped.Raw);
        Assert.Equal("quick*", escaped.Value);

        QueryToken phrase = lexer.Lex("\"quick brown\"")[0];
        Assert.True(phrase.IsSourceBackedValue);
        Assert.Equal(new QuerySourceSpan(0, 13), phrase.SourceSpan);
        Assert.Equal("quick brown", phrase.Value);
        Assert.Equal("quick brown", phrase.Raw);
    }

    [Fact(DisplayName = "Query analysis: zero tokens require no overflow storage")]
    public void AnalyseTerm_ZeroTokensUsesInlineStorage()
    {
        var parser = new QueryParser("body", new EmptyAnalyser());

        QueryAnalysisTokenBuffer tokens = parser.AnalyseTermForCompilation("body", "discarded");

        Assert.Empty(tokens);
        Assert.False(tokens.UsesOverflowStorage);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = tokens[0]);
    }

    [Fact(DisplayName = "Query analysis: the common single-token result avoids overflow storage")]
    public void AnalyseTerm_SingleTokenUsesInlineStorage()
    {
        var parser = new QueryParser("body", new StandardAnalyser());

        QueryAnalysisTokenBuffer tokens = parser.AnalyseTermForCompilation("body", "quick");

        Assert.Single(tokens);
        Assert.False(tokens.UsesOverflowStorage);
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(5, tokens[0].EndOffset);
        Assert.Equal(1, tokens[0].PositionIncrement);
        Assert.Equal(1, tokens[0].PositionLength);
    }

    [Fact(DisplayName = "Query analysis: multiple tokens promote storage without losing graph metadata")]
    public void AnalyseTerm_MultipleTokensPromoteStorageAndPreserveTokens()
    {
        var parser = new QueryParser("body", new GraphAnalyser());

        QueryAnalysisTokenBuffer tokens = parser.AnalyseTermForCompilation("body", "quick brown");

        Assert.Equal(2, tokens.Count);
        Assert.True(tokens.UsesOverflowStorage);
        Assert.Collection(
            tokens,
            token => Assert.Equal(("quick", 0, 5, 1, 2),
                (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement, token.PositionLength)),
            token => Assert.Equal(("fast", 0, 5, 0, 1),
                (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement, token.PositionLength)));
    }

    private sealed class EmptyAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
        }
    }

    private sealed class GraphAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            sink.Add("quick".AsSpan(), 0, 5, Token.DefaultType, 1, 2, payload: null);
            sink.Add("fast".AsSpan(), 0, 5, Token.DefaultType, 0, 1, payload: null);
        }
    }
}
