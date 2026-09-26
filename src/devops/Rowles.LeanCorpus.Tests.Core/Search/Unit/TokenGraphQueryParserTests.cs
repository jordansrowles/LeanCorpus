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

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive emitted path count")]
    public void Parse_PhraseGraphExceedingPathBudget_ThrowsQueryParseException()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 257, tailLength: 1));

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"graph\""));

        Assert.Contains("configured maximum of 256 paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "QueryParser: phrase graph rejects excessive compiled clause count")]
    public void Parse_PhraseGraphExceedingCompiledClauseBudget_ThrowsQueryParseException()
    {
        var parser = new QueryParser("body", new BranchingPhraseAnalyser(branchCount: 513, tailLength: 1), maxGraphPaths: 1_024);

        var exception = Assert.Throws<QueryParseException>(() => parser.Parse("\"graph\""));

        Assert.Contains("compiled phrase query clause count exceeds the maximum of 512", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BranchingPhraseAnalyser(int branchCount, int tailLength) : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            for (int branch = 0; branch < branchCount; branch++)
            {
                sink.Add("branch".AsSpan(), 0, 6, Token.DefaultType,
                    positionIncrement: branch == 0 ? 1 : 0, positionLength: 1, payload: null);
            }

            for (int tail = 0; tail < tailLength; tail++)
                sink.Add("tail".AsSpan(), 0, 4, Token.DefaultType, positionIncrement: 1, positionLength: 1, payload: null);
        }
    }
}
