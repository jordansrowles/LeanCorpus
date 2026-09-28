using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserAdversarialTests
{
    private const string EscapableSyntaxCharacters = "+-!(){}[]^\"~:\\*?/";

    [Property(
        DisplayName = "QueryParser: generated escaped syntax characters remain literal",
        MaxTest = 200,
        StartSize = 1,
        EndSize = 32)]
    public void Parse_GeneratedEscapedSyntaxCharactersRemainLiteral(NonEmptyArray<byte> generated)
    {
        char[] literalCharacters = generated.Get
            .Take(32)
            .Select(value => EscapableSyntaxCharacters[value % EscapableSyntaxCharacters.Length])
            .ToArray();
        string literal = $"left{new string(literalCharacters)}right";
        string escaped = $"left{string.Concat(literalCharacters.Select(static value => $"\\{value}"))}right";

        var parser = new QueryParser("body", new KeywordAnalyser());
        var query = Assert.IsType<TermQuery>(parser.Parse(escaped));

        Assert.Equal(literal, query.Term);
    }

    [Theory(DisplayName = "QueryParser: stopword operands preserve Boolean operator semantics")]
    [InlineData("the AND corpus", "none", "")]
    [InlineData("the OR corpus", "should", "corpus")]
    [InlineData("corpus AND NOT the", "must", "corpus")]
    [InlineData("NOT the AND corpus", "must", "corpus")]
    public void Parse_StopwordOperandsPreserveBooleanSemantics(
        string queryText,
        string expectedShape,
        string expectedTerm)
    {
        var parser = new QueryParser("body", new StandardAnalyser());
        Query query = parser.Parse(queryText);

        if (expectedShape == "none")
        {
            Assert.IsType<MatchNoDocsQuery>(query);
            return;
        }

        var boolean = Assert.IsType<BooleanQuery>(query);
        var clause = Assert.Single(boolean.Clauses);
        Assert.Equal(expectedShape == "must" ? Occur.Must : Occur.Should, clause.Occur);
        Assert.Equal(expectedTerm, Assert.IsType<TermQuery>(clause.Query).Term);
    }
}
