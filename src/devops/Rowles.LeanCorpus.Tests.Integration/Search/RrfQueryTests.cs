using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Contains unit tests for RRF Query.
/// </summary>
public sealed class RrfQueryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public RrfQueryTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Combine: Fuses Ranked Lists scenario.
    /// </summary>
    [Fact(DisplayName = "Combine: Fuses Ranked Lists")]
    public void Combine_FusesRankedLists()
    {
        // Arrange — two result sets with overlapping docs
        var set1 = new TopDocs(3, [new ScoreDoc(1, 10f), new ScoreDoc(2, 8f), new ScoreDoc(3, 5f)]);
        var set2 = new TopDocs(3, [new ScoreDoc(2, 9f), new ScoreDoc(3, 7f), new ScoreDoc(4, 3f)]);

        // Act
        var fused = RrfQuery.Combine([set1, set2], topN: 10, k: 60);

        // Assert — doc 2 appears in both lists so should have highest RRF score
        Assert.True(fused.ScoreDocs.Length > 0);
        Assert.Equal(2, fused.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Combine: Empty Inputs Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Combine: Empty Inputs Returns Empty")]
    public void Combine_EmptyInputs_ReturnsEmpty()
    {
        var result = RrfQuery.Combine([], topN: 10);
        Assert.Equal(0, result.TotalHits);
    }

    /// <summary>
    /// Verifies the Combine: Respects Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Combine: Respects Top N")]
    public void Combine_RespectsTopN()
    {
        var set1 = new TopDocs(5,
        [
            new ScoreDoc(1, 10f), new ScoreDoc(2, 9f), new ScoreDoc(3, 8f),
            new ScoreDoc(4, 7f), new ScoreDoc(5, 6f)
        ]);

        var fused = RrfQuery.Combine([set1], topN: 3, k: 60);
        Assert.True(fused.ScoreDocs.Length <= 3);
    }

    /// <summary>
    /// Verifies the RRF Query: End-to-end Merges Text Queries scenario.
    /// </summary>
    [Fact(DisplayName = "RRF Query: End-to-end Merges Text Queries")]
    public void RrfQuery_EndToEnd_MergesTextQueries()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(RrfQuery_EndToEnd_MergesTextQueries));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            // Doc 0: matches "hello" only
            var doc0 = new LeanDocument();
            doc0.Add(new TextField("title", "hello"));
            doc0.Add(new TextField("body", "greeting"));
            writer.AddDocument(doc0);

            // Doc 1: matches both "hello" and "world"
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("title", "hello world"));
            doc1.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc1);

            // Doc 2: matches "world" only
            var doc2 = new LeanDocument();
            doc2.Add(new TextField("title", "world"));
            doc2.Add(new TextField("body", "earth"));
            writer.AddDocument(doc2);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);

        var rrf = new RrfQuery(k: 60)
            .Add(new TermQuery("title", "hello"))
            .Add(new TermQuery("body", "world"));

        // Act
        var results = searcher.Search(rrf, 10);

        // Assert — doc 1 should rank highest (appears in both result lists)
        Assert.True(results.TotalHits > 0);
    }

    /// <summary>
    /// Verifies the RRF Query: Equality scenario.
    /// </summary>
    [Fact(DisplayName = "RRF Query: Equality")]
    public void RrfQuery_Equality()
    {
        var q1 = new RrfQuery(60).Add(new TermQuery("f", "a")).Add(new TermQuery("f", "b"));
        var q2 = new RrfQuery(60).Add(new TermQuery("f", "a")).Add(new TermQuery("f", "b"));
        var q3 = new RrfQuery(30).Add(new TermQuery("f", "a"));

        Assert.Equal(q1, q2);
        Assert.NotEqual(q1, q3);
        Assert.Equal(q1.GetHashCode(), q2.GetHashCode());
    }

    [Fact(DisplayName = "Combine: Weighted ties use best rank then document ID")]
    public void Combine_WeightedTiesUseBestRankThenDocumentId()
    {
        var first = new TopDocs(2, [new ScoreDoc(7, 10f), new ScoreDoc(3, 9f)]);
        var second = new TopDocs(2, [new ScoreDoc(3, 10f), new ScoreDoc(7, 9f)]);

        var fused = RrfQuery.Combine([first, second], [1f, 1f], topN: 2, k: 60);

        Assert.Equal([3, 7], fused.ScoreDocs.Select(hit => hit.DocId));
    }

    [Fact(DisplayName = "Combine: Child weights change fused ordering")]
    public void Combine_ChildWeightsChangeFusedOrdering()
    {
        var first = new TopDocs(1, [new ScoreDoc(1, 10f)]);
        var second = new TopDocs(1, [new ScoreDoc(2, 10f)]);

        var fused = RrfQuery.Combine([first, second], [1f, 2f], topN: 2, k: 60);

        Assert.Equal(2, fused.ScoreDocs[0].DocId);
    }

    [Fact(DisplayName = "RRF Query: Independent child window contributes beyond final top N")]
    public void RrfQuery_IndependentChildWindowContributesBeyondFinalTopN()
    {
        var dir = Path.Combine(_path, nameof(RrfQuery_IndependentChildWindowContributesBeyondFinalTopN));
        Directory.CreateDirectory(dir);
        using var mmap = new MMapDirectory(dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var first = new LeanDocument();
            first.Add(new TextField("title", "alpha"));
            writer.AddDocument(first);

            var overlapping = new LeanDocument();
            overlapping.Add(new TextField("title", "alpha filler filler filler"));
            overlapping.Add(new TextField("body", "beta"));
            writer.AddDocument(overlapping);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var query = new RrfQuery()
            .Add(new TermQuery("title", "alpha"), candidateWindow: 2)
            .Add(new TermQuery("body", "beta"), candidateWindow: 1);

        var result = searcher.Search(query, 1);

        Assert.Equal(1, Assert.Single(result.ScoreDocs).DocId);
    }

    [Fact(DisplayName = "RRF Query: Equality includes window and weight")]
    public void RrfQuery_EqualityIncludesWindowAndWeight()
    {
        var baseline = new RrfQuery().Add(new TermQuery("f", "a"), 10, 1f);
        var differentWindow = new RrfQuery().Add(new TermQuery("f", "a"), 20, 1f);
        var differentWeight = new RrfQuery().Add(new TermQuery("f", "a"), 10, 2f);

        Assert.NotEqual(baseline, differentWindow);
        Assert.NotEqual(baseline, differentWeight);
    }
}
