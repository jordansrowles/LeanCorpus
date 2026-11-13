using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// MoreLikeThis results must not depend on cache warmth: a warm search runs exactly the query a
/// cold one does.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MoreLikeThisCacheTests : IClassFixture<TestDirectoryFixture>
{
    private const int DocumentCount = MoreLikeThisTestCorpus.DocumentCount;

    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisCacheTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<int, int, int, int, int, bool> Parameters => new()
    {
        // MaxQueryTerms, MinTermFreq, MinDocFreq, MaxDocFreq, MinWordLength, BoostByScore
        { 25, 1, 1, int.MaxValue, 3, true },
        { 5, 1, 1, int.MaxValue, 3, true },
        { 5, 1, 1, int.MaxValue, 3, false },
        { 8, 2, 1, int.MaxValue, 3, true },
        { 10, 1, 5, int.MaxValue, 4, true },
        { 10, 1, 1, 120, 3, true },
        { 3, 1, 2, 200, 5, false }
    };

    [Theory(DisplayName = "MoreLikeThis: Cold And Warm Searches Are Identical")]
    [MemberData(nameof(Parameters))]
    public void MoreLikeThis_ColdAndWarm_AreIdentical(
        int maxQueryTerms, int minTermFreq, int minDocFreq, int maxDocFreq, int minWordLength, bool boostByScore)
    {
        var parameters = new MoreLikeThisParameters
        {
            MaxQueryTerms = maxQueryTerms,
            MinTermFreq = minTermFreq,
            MinDocFreq = minDocFreq,
            MaxDocFreq = maxDocFreq,
            MinWordLength = minWordLength,
            BoostByScore = boostByScore
        };
        using var directory = Build("mlt_cold_warm", segments: 4);
        using var searcher = new IndexSearcher(directory);
        foreach (int source in new[] { 3, 77, 250 })
        {
            int docId = MoreLikeThisTestCorpus.DocId(searcher, source);
            var query = new MoreLikeThisQuery(docId, ["body", "title"], parameters);
            var extracted = searcher.ExtractMoreLikeThisTerms(query);

            var cold = searcher.MoreLikeThis(docId, ["body", "title"], 20, parameters);
            Assert.True(searcher.TryGetCachedMoreLikeThisTerms(query, out var cached));
            Assert.Equal(extracted, cached);
            var warm = searcher.MoreLikeThis(docId, ["body", "title"], 20, parameters);

            Assert.Equal(cold.TotalHits, warm.TotalHits);
            Assert.Equal(cold.ScoreDocs.Select(static h => h.DocId), warm.ScoreDocs.Select(static h => h.DocId));
            for (int i = 0; i < cold.ScoreDocs.Length; i++)
                Assert.Equal(cold.ScoreDocs[i].Score, warm.ScoreDocs[i].Score, 5);
        }
    }

    private MMapDirectory Build(string name, int segments) => MoreLikeThisTestCorpus.Build(_fixture.Path, name, segments);
}
