using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class RankingProfilesAndRulesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lc_ranking_{Guid.NewGuid():N}");
    public RankingProfilesAndRulesTests() => Directory.CreateDirectory(_path);
    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void Rules_NormaliseExactTextAndRespectPriority()
    {
        using var searcher = CreateSearcher();
        var rules = new QueryRuleSet([
            new QueryRule("low", 1, new QueryRuleMatch("quick fox"), [new ScoreQueryRuleAction([0], 0f)]),
            new QueryRule("high", 2, new QueryRuleMatch("QUICK   FOX"), [new ScoreQueryRuleAction([1], 4f)])]);
        var result = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 3, new RankingProfile("web", "1"), rules, new RankingRequestContext(" Quick fox ")));
        Assert.Equal(["high", "medium", "low"], Ids(searcher, result));
        Assert.Equal(["high", "low"], result.MatchedRuleIds);
    }

    [Fact]
    public void Rules_FilterBeforeRetrievalAndPinsUseAbsolutePositions()
    {
        using var searcher = CreateSearcher();
        var rule = new QueryRule("curated", 10, new QueryRuleMatch("quick"), [
            new FilterQueryRuleAction(new TermQuery("category", "public")),
            new PinQueryRuleAction(new Dictionary<int, int> { [1] = 1 })]);
        var result = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 3, new RankingProfile("web", "1"), new QueryRuleSet([rule]), new RankingRequestContext("quick")));
        Assert.Equal(["high", "low"], Ids(searcher, result));
        Assert.Equal(2, result.TopDocs.TotalHits);
    }

    [Fact]
    public void Pipeline_AppliesNumericFunctionWithinCandidateBudget()
    {
        using var searcher = CreateSearcher();
        var pipeline = new RankingPipeline([new ScoreFunctionStage("rank", DoubleValuesSource.FromDoubleField("rank"), RankingScoreCombination.Add, 3)]);
        var result = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 3, new RankingProfile("web", "1", pipeline)));
        Assert.Equal(["high", "medium", "low"], Ids(searcher, result));
    }

    [Fact]
    public void RulesetAndProfileChangesProduceDifferentCompatibilityIdentities()
    {
        using var searcher = CreateSearcher(cache: true);
        var first = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 2, new RankingProfile("web", "1"), context: new RankingRequestContext(safeCacheIdentity: "safe")));
        var second = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 2, new RankingProfile("web", "2"), context: new RankingRequestContext(safeCacheIdentity: "safe")));
        var changedRules = new QueryRuleSet([new QueryRule("x", 1, new QueryRuleMatch(), [new ScoreQueryRuleAction([0], 0f)])]);
        var third = searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 2, new RankingProfile("web", "2"), changedRules, new RankingRequestContext(safeCacheIdentity: "safe")));
        Assert.NotEqual(first.CompatibilityIdentity, second.CompatibilityIdentity);
        Assert.NotEqual(second.CompatibilityIdentity, third.CompatibilityIdentity);
    }

    [Fact]
    public void ProfileRejectsSimilarityThatDoesNotMatchSearcher()
    {
        using var searcher = CreateSearcher();
        var profile = new RankingProfile("web", "1", defaultSimilarity: new Bm25PlusSimilarity());
        Assert.Throws<InvalidOperationException>(() => searcher.Search(new RankingSearchRequest(new TermQuery("body", "quick"), 1, profile)));
    }

    [Fact]
    public void ProfileAndRulesAreImmutableSnapshots()
    {
        var weights = new Dictionary<string, float> { ["title"] = 2f };
        var profile = new RankingProfile("web", "1", fieldWeights: weights);
        weights["title"] = 9f;
        var rules = new List<QueryRule> { new("a", 1, new QueryRuleMatch(), []) };
        var set = new QueryRuleSet(rules);
        rules.Clear();
        Assert.Equal(2f, profile.FieldWeights["title"]);
        Assert.Single(set.Rules);
    }

    private IndexSearcher CreateSearcher(bool cache = false)
    {
        var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            Add(writer, "low", "quick", "public", 1);
            Add(writer, "high", "quick quick", "public", 10);
            Add(writer, "medium", "quick", "private", 5);
            writer.Commit();
        }
        return new IndexSearcher(directory, new IndexSearcherConfig { EnableQueryCache = cache });
    }
    private static void Add(IndexWriter writer, string id, string body, string category, double rank)
    { var document = new LeanDocument(); document.Add(new StringField("id", id)); document.Add(new TextField("body", body)); document.Add(new StringField("category", category)); document.Add(new NumericField("rank", rank)); writer.AddDocument(document); }
    private static string[] Ids(IndexSearcher searcher, RankingSearchResult result) => result.TopDocs.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).ToArray();
}
