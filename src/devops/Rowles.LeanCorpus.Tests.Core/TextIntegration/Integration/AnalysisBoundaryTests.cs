namespace Rowles.LeanCorpus.Tests.Core.TextIntegration.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.TextIntegration)]
public sealed class AnalysisBoundaryTests
{
    [Fact]
    public void StandardAnalyser_produces_terms_consumable_by_LeanCorpus()
    {
        var analyser = new StandardAnalyser();
        var sink = new MaterialisingTokenSink();

        analyser.Analyse("The QUICK fox", sink);

        Assert.Equal(["quick", "fox"], sink.Tokens.Select(static token => token.Text));
    }
}
