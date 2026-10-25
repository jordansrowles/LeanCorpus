using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserAnalysisBudgetTests
{
    [Fact(DisplayName = "Query analysis: unquoted expansion stops at the remaining clause allowance")]
    public void Parse_UnquotedExpansionExceedingClauseBudget_StopsBeforeBufferingAllTokens()
    {
        const int clauseLimit = 4;
        var analyser = new CountingAnalyser(128);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with { MaxQueryClauses = clauseLimit });

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => ParseWithComplexity(parser, "  expanded"));

        Assert.InRange(analyser.EmittedTokenCount, 1, clauseLimit + 1);
        Assert.Equal(2, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: remaining clause allowance is shared across unquoted terms")]
    public void Parse_MultipleUnquotedExpansions_UsesRemainingClauseAllowanceAtEachTerm()
    {
        const int clauseLimit = 4;
        var analyser = new CountingAnalyser(128, seedEmitsOneToken: true);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with { MaxQueryClauses = clauseLimit });

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => ParseWithComplexity(parser, "seed expanded"));

        Assert.InRange(analyser.EmittedTokenCount, 2, clauseLimit + 1);
        Assert.Equal(5, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: total analysed-token budget stops unquoted expansion before buffering overflow")]
    public void Parse_UnquotedExpansionExceedingAnalysedTokenBudget_StopsBeforeBufferingOverflow()
    {
        const int tokenLimit = 3;
        var analyser = new CountingAnalyser(128);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with
            {
                MaxAnalysedTokens = tokenLimit,
                MaxQueryClauses = int.MaxValue
            });

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => ParseWithComplexity(parser, "expanded"));

        Assert.Equal(tokenLimit + 1, analyser.EmittedTokenCount);
        Assert.Equal(0, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: linear emissions remain bounded when positions advance")]
    public void Parse_LinearExpansionExceedingAnalysedTokenBudget_StopsBeforeBufferingOverflow()
    {
        const int tokenLimit = 3;
        var analyser = new LinearCountingAnalyser(128);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with
            {
                MaxAnalysedTokens = tokenLimit,
                MaxQueryClauses = 1
            });

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => ParseWithComplexity(parser, "linear"));

        Assert.Equal(tokenLimit + 1, analyser.EmittedTokenCount);
        Assert.Contains("analysed token count", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: edge n-gram text is bounded before prefix strings are buffered")]
    public void Parse_EdgeNGramExpansionExceedingTokenCharacterBudget_StopsBeforeBufferingOverflow()
    {
        const int tokenCharacterLimit = 10;
        var analyser = new CountingEdgeNGramAnalyser(maxGram: 200);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with
            {
                MaxAnalysedTokenChars = tokenCharacterLimit,
                MaxQueryClauses = 1
            });

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => ParseWithComplexity(parser, new string('x', 200)));

        Assert.Equal(5, analyser.EmittedTokenCount);
        Assert.Contains("analysed token text character count", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: total analysed-token budget is shared by quoted and unquoted terms")]
    public void Parse_QuotedAndUnquotedAnalysis_UsesOneTokenBudget()
    {
        var analyser = new CountingAnalyser(2);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with { MaxAnalysedTokens = 3 });
        QuerySyntax syntax = parser.ParseSyntax("seed \"phrase\"", limitsAreComplexity: true);

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => parser.CompileSyntax(syntax));

        Assert.Equal(4, analyser.EmittedTokenCount);
        Assert.Equal(5, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: total analysed-token character budget is shared by quoted and unquoted terms")]
    public void Parse_QuotedAndUnquotedAnalysis_UsesOneTokenCharacterBudget()
    {
        var parser = new QueryParser(
            "body",
            new EchoAnalyser(),
            QueryParserOptions.Default with { MaxAnalysedTokenChars = 6 });
        QuerySyntax syntax = parser.ParseSyntax("seed \"phrase\"", limitsAreComplexity: true);

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => parser.CompileSyntax(syntax));

        Assert.Contains("analysed token text character count", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: default and trusted token limits remain compatible")]
    public void Options_DefaultAnalysedTokenLimitIsBoundedAndTrustedLimitIsUnlimited()
    {
        Assert.Equal(16_384, QueryParserOptions.Default.MaxAnalysedTokens);
        Assert.Equal(1_048_576L, QueryParserOptions.Default.MaxAnalysedTokenChars);
        Assert.Equal(int.MaxValue, QueryParserOptions.Trusted.MaxAnalysedTokens);
        Assert.Equal(long.MaxValue, QueryParserOptions.Trusted.MaxAnalysedTokenChars);
    }

    [Fact(DisplayName = "Query analysis: quoted tokens retain their phrase budget and source offset")]
    public void Parse_QuotedExpansionExceedingPhraseBudget_StopsAtTheBudget()
    {
        const int phraseLimit = 3;
        var analyser = new CountingAnalyser(128);
        var parser = new QueryParser(
            "body",
            analyser,
            QueryParserOptions.Default with { MaxPhraseTokens = phraseLimit });
        QuerySyntax syntax = parser.ParseSyntax("\"phrase\"", limitsAreComplexity: true);

        QueryParseLimitException exception = Assert.Throws<QueryParseLimitException>(
            () => parser.CompileSyntax(syntax));

        Assert.Equal(phraseLimit + 1, analyser.EmittedTokenCount);
        Assert.Equal(0, exception.Offset);
    }

    [Fact(DisplayName = "Query analysis: unquoted graph edges retain their complete phrase paths")]
    public void Parse_UnquotedGraphWithinClauseBudget_PreservesCompletePaths()
    {
        var parser = new QueryParser(
            "body",
            new BranchingAnalyser(),
            QueryParserOptions.Default with { MaxQueryClauses = 4 });

        var query = Assert.IsType<BooleanQuery>(parser.Parse("graph"));

        Assert.Collection(
            query.Clauses,
            clause => Assert.Equal(["new"], Assert.IsType<PhraseQuery>(clause.Query).Terms),
            clause => Assert.Equal(["nyc", "york"], Assert.IsType<PhraseQuery>(clause.Query).Terms));
    }

    [Fact(DisplayName = "Query analysis: a single-path unquoted graph consumes one query clause")]
    public void Parse_UnquotedSinglePathGraphFitsSingleClauseBudget()
    {
        var parser = new QueryParser(
            "body",
            new SinglePathGraphAnalyser(),
            QueryParserOptions.Default with { MaxQueryClauses = 1 });

        var phrase = Assert.IsType<PhraseQuery>(parser.Parse("graph"));

        Assert.Equal(["first", "second", "third"], phrase.Terms);
        Assert.Equal([0, 1, 2], phrase.Positions);
    }

    private static void ParseWithComplexity(QueryParser parser, string query)
    {
        QuerySyntax syntax = parser.ParseSyntax(query, limitsAreComplexity: true);
        parser.CompileSyntax(syntax);
    }

    private sealed class CountingAnalyser(int emissionsPerInput, bool seedEmitsOneToken = false) : IAnalyser
    {
        public int EmittedTokenCount { get; private set; }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            int emissionCount = seedEmitsOneToken && input.SequenceEqual("seed")
                ? 1
                : emissionsPerInput;
            for (int index = 0; index < emissionCount; index++)
            {
                EmittedTokenCount++;
                sink.Add(
                    "x".AsSpan(),
                    startOffset: 0,
                    endOffset: Math.Min(input.Length, 1),
                    type: Token.DefaultType,
                    positionIncrement: index == 0 ? 1 : 0,
                    positionLength: 1,
                    payload: null);
            }
        }
    }

    private sealed class LinearCountingAnalyser(int emissionsPerInput) : IAnalyser
    {
        public int EmittedTokenCount { get; private set; }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            for (int index = 0; index < emissionsPerInput; index++)
            {
                EmittedTokenCount++;
                sink.Add(
                    "x".AsSpan(),
                    startOffset: 0,
                    endOffset: Math.Min(input.Length, 1),
                    type: Token.DefaultType,
                    positionIncrement: 1,
                    positionLength: 1,
                    payload: null);
            }
        }
    }

    private sealed class CountingEdgeNGramAnalyser(int maxGram) : IAnalyser
    {
        private readonly EdgeNGramTokeniser _tokeniser = new(minGram: 1, maxGram: maxGram);

        public int EmittedTokenCount { get; private set; }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) =>
            _tokeniser.Tokenise(input, new CountingTokenSink(this, sink));

        private sealed class CountingTokenSink(CountingEdgeNGramAnalyser owner, ISpanTokenSink inner) : ISpanTokenSink
        {
            public void Add(
                ReadOnlySpan<char> text,
                int startOffset,
                int endOffset,
                string type = Token.DefaultType,
                int positionIncrement = 1,
                byte[]? payload = null)
            {
                owner.EmittedTokenCount++;
                inner.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            }
        }
    }

    private sealed class EchoAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) =>
            sink.Add(input, 0, input.Length, Token.DefaultType);
    }

    private sealed class BranchingAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            sink.Add("new".AsSpan(), 0, 3, Token.DefaultType,
                positionIncrement: 1, positionLength: 2, payload: null);
            sink.Add("nyc".AsSpan(), 0, 3, Token.DefaultType,
                positionIncrement: 0, positionLength: 1, payload: null);
            sink.Add("york".AsSpan(), 0, 4, Token.DefaultType,
                positionIncrement: 1, positionLength: 1, payload: null);
        }
    }

    private sealed class SinglePathGraphAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            sink.Add("first".AsSpan(), 0, input.Length, Token.DefaultType,
                positionIncrement: 1, positionLength: 1, payload: null);
            sink.Add("second".AsSpan(), 0, input.Length, Token.DefaultType,
                positionIncrement: 1, positionLength: 1, payload: null);
            sink.Add("third".AsSpan(), 0, input.Length, Token.DefaultType,
                positionIncrement: 1, positionLength: 2, payload: null);
        }
    }
}
