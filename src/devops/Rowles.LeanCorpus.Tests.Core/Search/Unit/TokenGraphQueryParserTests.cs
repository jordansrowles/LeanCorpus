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
}
