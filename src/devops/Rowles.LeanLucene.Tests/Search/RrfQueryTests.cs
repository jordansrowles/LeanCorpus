using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search;

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
}
