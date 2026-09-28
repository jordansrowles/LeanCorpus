using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ComplexPhraseQueryParserBudgetTests
{
    [Fact]
    public void Parse_ChargesAlternativeTermsToThePhraseTokenBudget()
    {
        var options = new QueryParserOptions { MaxPhraseTokens = 1 };
        var parser = new ComplexPhraseQueryParser("body", new StandardAnalyser(), options);

        Assert.Throws<QueryParseException>(() => parser.Parse("\"(quick OR fast)\""));
    }
}
