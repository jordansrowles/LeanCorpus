using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Analysis;

[Trait("Category", "Chaos")]
[Trait("Category", "Analysis")]
public sealed class AnalysisGuardrailChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public AnalysisGuardrailChaosTests(ChaosDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Hunspell Dictionary: Unsupported Flag Mode Is Rejected")]
    public void HunspellDictionary_UnsupportedFlagMode_IsRejected()
    {
        const string aff = """
SET UTF-8
FLAG long
""";

        Assert.Throws<NotSupportedException>(() => HunspellDictionary.Parse(aff, "1\nplay\n"));
    }

    [Fact(DisplayName = "Analysis Expansion: Token Budget Rejects Runaway Alternates")]
    public void AnalysisExpansion_TokenBudget_RejectsRunawayAlternates()
    {
        var analyser = new Analyser(
            new Uax29UrlEmailTokeniser(),
            new LowercaseFilter(),
            new MetaphoneFilter(),
            new PhoneticAlternatesFilter(),
            new FlattenGraphFilter());

        using var directory = new MMapDirectory(Path.Combine(_fixture.Path, "analysis_budget"));
        var config = new IndexWriterConfig
        {
            DefaultAnalyser = analyser,
            MaxTokensPerDocument = 2,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        };

        using var writer = new IndexWriter(directory, config);
        var document = new LeanDocument();
        document.Add(new TextField("body", "schwarz phone"));

        var exception = Record.Exception(() => writer.AddDocument(document));

        Assert.IsType<TokenBudgetExceededException>(exception);
    }
}
