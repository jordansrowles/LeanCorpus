using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Regression tests for graph-aware quoted-query analysis.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class TokenGraphQueryParserTests
{
    private readonly ITestOutputHelper _output;

    public TokenGraphQueryParserTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Regression coverage for the disconnected ShingleFilter query failure identified
    /// alongside Lucene.NET #943: quoted analysis must preserve complete graph paths.
    /// </summary>
    [Fact(DisplayName = "QueryParser: quoted shingles compile to graph paths")]
    public void Parse_QuotedShingles_CompilesCompleteGraphPaths()
    {
        var parser = new QueryParser("body", new Analyser(new Tokeniser(), new ShingleFilter(2, 2)));

        var query = Assert.IsType<BooleanQuery>(parser.Parse("\"new york\""));

        Assert.Equal(2, query.Clauses.Count);
        Assert.Contains(query.Clauses, static clause => clause.Query is PhraseQuery { Terms: ["new", "york"] });
        Assert.Contains(query.Clauses, static clause => clause.Query is PhraseQuery { Terms: ["new york"] });
    }

    [Fact(DisplayName = "QueryParser: quoted linear phrases preserve positional holes")]
    public void Parse_QuotedLinearPhraseWithPositionHole_PreservesAbsolutePositions()
    {
        var parser = new QueryParser("body", new PositionGapPhraseAnalyser(includeBranch: false));

        var phrase = Assert.IsType<PhraseQuery>(parser.Parse("\"first second\""));

        Assert.Equal(["first", "second"], phrase.Terms);
        Assert.Equal([0, 3], phrase.Positions);
    }

    [Fact(DisplayName = "QueryParser: quoted branching phrases preserve positional holes")]
    public void Parse_QuotedBranchingPhraseWithPositionHole_PreservesPathsAndPositions()
    {
        var parser = new QueryParser("body", new PositionGapPhraseAnalyser(includeBranch: true));

        var query = Assert.IsType<BooleanQuery>(parser.Parse("\"new york\""));
        var phrases = query.Clauses.Select(static clause => Assert.IsType<PhraseQuery>(clause.Query)).ToArray();

        Assert.Collection(
            phrases,
            phrase =>
            {
                Assert.Equal(["new", "york"], phrase.Terms);
                Assert.Equal([0, 3], phrase.Positions);
            },
            phrase =>
            {
                Assert.Equal(["nyc", "york"], phrase.Terms);
                Assert.Equal([0, 3], phrase.Positions);
            });
    }

    [Fact(DisplayName = "QueryParser: long linear phrase preserves every term position")]
    public void Parse_LongLinearPhrase_PreservesEveryTermPosition()
    {
        const int termCount = 4_096;
        string phraseText = string.Join(' ', Enumerable.Repeat("token", termCount));
        var parser = new QueryParser("body", new WhitespaceAnalyser());

        var phrase = Assert.IsType<PhraseQuery>(parser.Parse($"\"{phraseText}\""));

        Assert.Equal(Enumerable.Repeat("token", termCount), phrase.Terms);
        Assert.Equal(Enumerable.Range(0, termCount), phrase.Positions);
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive analysed token count")]
    public void Parse_PhraseExceedingAnalysedTokenBudget_ThrowsQueryParseException()
    {
        const int termCount = 16_385;
        string phraseText = string.Join(' ', Enumerable.Repeat("token", termCount));
        var parser = new QueryParser("body", new WhitespaceAnalyser());

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse($"\"{phraseText}\""));

        Assert.Contains("phrase token count exceeds the maximum of 16384", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive edge count")]
    public void Parse_PhraseExceedingGraphEdgeBudget_ThrowsQueryParseException()
    {
        const int termCount = 8_193;
        string phraseText = string.Join(' ', Enumerable.Repeat("token", termCount));
        var parser = new QueryParser("body", new WhitespaceAnalyser());

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse($"\"{phraseText}\""));

        Assert.Contains("graph edge count exceeds the maximum of 8192", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive traversal steps")]
    public void Parse_PhraseGraphExceedingTraversalBudget_ThrowsWithinAllocationBudgetAndCanBeReused()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 64, tailLength: 1_100));
        const string queryText = "\"graph\"";

        Assert.Throws<QueryParseException>(() => parser.Parse(queryText));

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var exception = Assert.Throws<QueryParseException>(() => parser.Parse(queryText));
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains("traversal steps exceed the maximum of 65536", exception.Message, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"Traversal-budget rejection allocated {allocatedBytes:N0} bytes.");
        Assert.True(allocatedBytes < 16 * 1024 * 1024, $"Traversal-budget rejection allocated {allocatedBytes:N0} bytes.");
    }

    [Fact(DisplayName = "QueryParser: can parse after phrase traversal budget failure")]
    public void Parse_AfterPhraseTraversalBudgetFailure_CanBeReused()
    {
        var parser = new QueryParser(
            "body",
            new BranchingPhraseAnalyser(branchCount: 64, tailLength: 1_100, switchToSingleTokenAfterFirstAnalysis: true));

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"graph\""));

        Assert.Contains("traversal steps exceed the maximum of 65536", exception.Message, StringComparison.OrdinalIgnoreCase);

        var phrase = Assert.IsType<PhraseQuery>(parser.Parse("\"safe\""));
        Assert.Equal(["safe"], phrase.Terms);
        Assert.Equal([0], phrase.Positions);
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive emitted path count")]
    public void Parse_PhraseGraphExceedingPathBudget_ThrowsQueryParseException()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 257, tailLength: 1));

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"graph\""));

        Assert.Contains("configured maximum of 256 paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: phrase path budget is shared across one query")]
    public void Parse_MultiplePhraseGraphsExceedSharedPathBudget_ThrowsQueryParseException()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1), maxGraphPaths: 3);

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"first\" OR \"second\""));

        Assert.Contains("configured maximum of 3 paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: options enforce each phrase graph budget")]
    public void Parse_CustomOptionsEnforcePhraseGraphBudgets()
    {
        QueryParseException phraseTokenException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1),
                QueryParserOptions.Default with { MaxPhraseTokens = 1 })
            .Parse("\"graph\""));
        QueryParseException graphEdgeException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1),
                QueryParserOptions.Default with { MaxGraphEdges = 1 })
            .Parse("\"graph\""));
        QueryParseException traversalException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 1, tailLength: 2),
                QueryParserOptions.Default with { MaxGraphTraversalSteps = 1 })
            .Parse("\"graph\""));
        QueryParseException pathException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1),
                QueryParserOptions.Default with { MaxGraphPaths = 1 })
            .Parse("\"graph\""));
        QueryParseException compiledTermException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1),
                QueryParserOptions.Default with { MaxCompiledPhraseTerms = 1 })
            .Parse("\"graph\""));
        QueryParseException compiledClauseException = Assert.Throws<QueryParseException>(() =>
            new QueryParser(
                "body",
                new BranchingPhraseAnalyser(branchCount: 2, tailLength: 1),
                QueryParserOptions.Default with { MaxCompiledPhraseClauses = 1 })
            .Parse("\"graph\""));

        Assert.Contains("phrase token count", phraseTokenException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("graph edge count", graphEdgeException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traversal steps", traversalException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maximum of 1 paths", pathException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compiled phrase term count", compiledTermException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compiled phrase query clause count", compiledClauseException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: phrase graph bounds total compiled path terms")]
    public void Parse_PhraseGraphExceedingCompiledTermBudget_RejectsWithBoundedAllocation()
    {
        const string queryText = "\"graph\"";
        var warmParser = new QueryParser("body", new SharedPrefixBranchingPhraseAnalyser(prefixLength: 4, branchCount: 2));
        warmParser.Parse(queryText);
        warmParser.Parse(queryText);

        var parser = new QueryParser("body", new SharedPrefixBranchingPhraseAnalyser(prefixLength: 1_100, branchCount: 64));
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        object? result = null;
        Exception? failure = null;
        try
        {
            result = parser.Parse(queryText);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        _output.WriteLine($"Shared-prefix phrase graph (64 paths, 1,102 terms each) allocated {allocatedBytes:N0} bytes and returned {failure?.GetType().Name ?? result?.GetType().Name ?? "no result"}.");
        var parseException = Assert.IsType<QueryParseException>(failure);
        Assert.Contains("compiled phrase term count exceeds the maximum of 65536", parseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(allocatedBytes < 2_500_000, $"Budget rejection allocated {allocatedBytes:N0} bytes.");
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive compiled clause count")]
    public void Parse_PhraseGraphExceedingCompiledClauseBudget_ThrowsQueryParseException()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 513, tailLength: 1), maxGraphPaths: 1_024);

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"graph\""));

        Assert.Contains("compiled phrase query clause count exceeds the maximum of 512", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BranchingPhraseAnalyser(
        int branchCount,
        int tailLength,
        bool switchToSingleTokenAfterFirstAnalysis = false) : IAnalyser
    {
        private int _analysisCount;

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (switchToSingleTokenAfterFirstAnalysis && _analysisCount++ > 0)
            {
                sink.Add("safe".AsSpan(), 0, 4, Token.DefaultType,
                    positionIncrement: 1, positionLength: 1, payload: null);
                return;
            }

            for (int branch = 0; branch < branchCount; branch++)
            {
                sink.Add("branch".AsSpan(), 0, 6, Token.DefaultType,
                    positionIncrement: branch == 0 ? 1 : 0, positionLength: 1, payload: null);
            }

            for (int tail = 0; tail < tailLength; tail++)
                sink.Add("tail".AsSpan(), 0, 4, Token.DefaultType, positionIncrement: 1, positionLength: 1, payload: null);
        }
    }

    private sealed class PositionGapPhraseAnalyser(bool includeBranch) : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (!includeBranch)
            {
                sink.Add("first".AsSpan(), 0, 5, Token.DefaultType,
                    positionIncrement: 1, positionLength: 1, payload: null);
                sink.Add("second".AsSpan(), 6, 12, Token.DefaultType,
                    positionIncrement: 3, positionLength: 1, payload: null);
                return;
            }

            sink.Add("new".AsSpan(), 0, 3, Token.DefaultType,
                positionIncrement: 1, positionLength: 1, payload: null);
            sink.Add("nyc".AsSpan(), 0, 3, Token.DefaultType,
                positionIncrement: 0, positionLength: 2, payload: null);
            sink.Add("york".AsSpan(), 4, 8, Token.DefaultType,
                positionIncrement: 3, positionLength: 1, payload: null);
        }
    }

    private sealed class SharedPrefixBranchingPhraseAnalyser(int prefixLength, int branchCount) : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            for (int index = 0; index < prefixLength; index++)
                sink.Add("prefix".AsSpan(), 0, 6, Token.DefaultType,
                    positionIncrement: 1, positionLength: 1, payload: null);

            for (int branch = 0; branch < branchCount; branch++)
                sink.Add("branch".AsSpan(), 0, 6, Token.DefaultType,
                    positionIncrement: branch == 0 ? 1 : 0, positionLength: 1, payload: null);

            sink.Add("tail".AsSpan(), 0, 4, Token.DefaultType,
                positionIncrement: 1, positionLength: 1, payload: null);
        }
    }
}
