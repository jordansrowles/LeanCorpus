using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Excluding the source must preserve exact totals, fill top-N and exclude it from side collectors.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MoreLikeThisSourceExclusionTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisSourceExclusionTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory(DisplayName = "MoreLikeThis: Source Exclusion Preserves Totals And Fills Top N")]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void MoreLikeThis_SourceExclusion_PreservesTotalsAndTopN(bool sourceRanksFirst, bool deletedSource)
    {
        foreach (int segmentSize in new[] { 1, 4 })
        foreach (bool wand in new[] { false, true })
        {
            using var directory = Build(sourceRanksFirst, deletedSource, segmentSize);
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { EnableBlockMaxWand = wand });
            var reference = searcher.Search(new TermQuery("body", "alpha"), 4);
            if (!deletedSource)
                Assert.Equal(sourceRanksFirst, reference.ScoreDocs[0].DocId == 0);
            var expected = reference.ScoreDocs.Where(static hit => hit.DocId != 0).ToArray();
            var parameters = new MoreLikeThisParameters { BoostByScore = false };
            foreach (int topN in new[] { 0, 1, 2, 10, int.MaxValue })
            {
                // The query is exercised cold and then warm for every requested page size.
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var actual = searcher.MoreLikeThis(0, ["body"], topN, parameters);
                    Assert.Equal(3, actual.TotalHits);
                    Assert.Equal(expected.Take(topN).Select(static hit => hit.DocId), actual.ScoreDocs.Select(static hit => hit.DocId));
                    Assert.Equal(expected.Take(topN).Select(static hit => hit.Score), actual.ScoreDocs.Select(static hit => hit.Score));
                    Assert.False(actual.IsPartial);
                }
            }

            var (_, aggregations) = searcher.SearchWithAggregations(
                new MoreLikeThisQuery(0, ["body"], parameters), 1, [new AggregationRequest("prices", "price")]);
            Assert.Equal(3, Assert.Single(aggregations).Count);
            Assert.Equal(6d, Assert.Single(aggregations).Sum);
        }
    }

    private MMapDirectory Build(bool sourceRanksFirst, bool deletedSource, int segmentSize)
    {
        var directory = new MMapDirectory(Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N")));
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = segmentSize,
            MergePolicy = NoMergePolicy.Instance,
            StoreTermVectors = true
        });
        for (int i = 0; i < 4; i++)
        {
            bool shortBody = (i == 0) == sourceRanksFirst;
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture)));
            document.Add(new TextField("body", shortBody ? "alpha alpha alpha" : "alpha " + string.Join(' ', Enumerable.Repeat("x", 80))));
            document.Add(new NumericField("price", i));
            writer.AddDocument(document);
        }
        writer.Commit();
        if (deletedSource)
        {
            writer.DeleteDocuments(new TermQuery("id", "0"));
            writer.Commit();
        }
        return directory;
    }
}
