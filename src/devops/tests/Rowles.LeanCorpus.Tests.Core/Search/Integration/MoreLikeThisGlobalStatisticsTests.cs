using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>MLT selection uses exact global frequencies even when the source segment is unrepresentative.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class MoreLikeThisGlobalStatisticsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MoreLikeThisGlobalStatisticsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory(DisplayName = "MoreLikeThis: Global Frequency Limits And Ranking Survive Segmentation And Merge")]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 2, true)]
    [InlineData(1, int.MaxValue, true)]
    [InlineData(3, 2, false)]
    [InlineData(1, 1, false)]
    public void MoreLikeThis_GlobalFrequencyLimits_AreInvariant(int minimum, int maximum, bool hasTerms)
    {
        using var directory = new MMapDirectory(Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N")));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            StoreTermVectors = true,
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            for (int i = 0; i < 16; i++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture)));
                document.Add(new TextField("body", i == 0 ? "alpha alpha beta beta beta" : i == 1 ? "beta beta" : "alpha"));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        var parameters = new MoreLikeThisParameters { MinDocFreq = minimum, MaxDocFreq = maximum, MaxQueryTerms = 1 };
        string[] segmented;
        float[] scores;
        using (var searcher = new IndexSearcher(directory))
        {
            Assert.True(searcher.GetSegmentReaders().Count > 1);
            (segmented, scores) = Check(searcher, parameters, hasTerms);
            Assert.Equal(segmented, Check(searcher, parameters, hasTerms).Ids);
        }

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance }))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }
        using var merged = new IndexSearcher(directory);
        Assert.Single(merged.GetSegmentReaders());
        var actual = Check(merged, parameters, hasTerms);
        Assert.Equal(segmented, actual.Ids);
        Assert.Equal(scores.Length, actual.Scores.Length);
        for (int i = 0; i < scores.Length; i++)
            Assert.Equal(scores[i], actual.Scores[i], precision: 5);
    }

    private static (string[] Ids, float[] Scores) Check(IndexSearcher searcher, MoreLikeThisParameters parameters, bool hasTerms)
    {
        int source = Assert.Single(searcher.Search(new TermQuery("id", "0"), 1).ScoreDocs).DocId;
        var query = new MoreLikeThisQuery(source, ["body"], parameters);
        var terms = searcher.ExtractMoreLikeThisTerms(query);
        if (hasTerms)
        {
            // beta occurs in two documents; alpha in fifteen. Local DF * segment count
            // would instead prefer alpha's smaller source TF under a negative estimated IDF.
            var term = Assert.Single(terms);
            Assert.Equal("beta", term.Term);
            Assert.Equal("body", term.Field);
            Assert.Equal(3f * MathF.Log(16f / 3f), term.Score, precision: 5);
        }
        else
        {
            Assert.Empty(terms);
        }

        var hits = searcher.MoreLikeThis(source, ["body"], 20, parameters);
        Assert.Equal(hasTerms ? 1 : 0, hits.TotalHits);
        string[] ids = hits.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).ToArray();
        Assert.Equal(hasTerms ? ["1"] : Array.Empty<string>(), ids);
        return (ids, hits.ScoreDocs.Select(static hit => hit.Score).ToArray());
    }
}
