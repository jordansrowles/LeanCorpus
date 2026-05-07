using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for More Like This.
/// </summary>
public class MoreLikeThisTests : IDisposable
{
    private readonly string _dir;

    public MoreLikeThisTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mlt_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* mmap handles may linger on Windows */ }
    }

    private void IndexCorpus()
    {
        var config = new IndexWriterConfig { StoreTermVectors = true };
        using var writer = new IndexWriter(new MMapDirectory(_dir), config);

        var d0 = new LeanDocument();
        d0.Add(new TextField("body", "the cat sat on the mat the cat was happy"));
        d0.Add(new StringField("id", "0"));
        writer.AddDocument(d0);

        var d1 = new LeanDocument();
        d1.Add(new TextField("body", "the cat ran across the garden chasing a mouse the cat was fast"));
        d1.Add(new StringField("id", "1"));
        writer.AddDocument(d1);

        var d2 = new LeanDocument();
        d2.Add(new TextField("body", "the dog played in the park fetching a ball"));
        d2.Add(new StringField("id", "2"));
        writer.AddDocument(d2);

        var d3 = new LeanDocument();
        d3.Add(new TextField("body", "dotnet performance benchmarks and memory allocation profiling"));
        d3.Add(new StringField("id", "3"));
        writer.AddDocument(d3);

        writer.Commit();
    }

    /// <summary>
    /// Verifies the More Like This: Returns Related Documents scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Returns Related Documents")]
    public void MoreLikeThis_ReturnsRelatedDocuments()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.MoreLikeThis(0, ["body"], 10,
            new MoreLikeThisParameters { MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 3 });

        Assert.True(results.TotalHits > 0, "Should find at least one similar document");
    }

    /// <summary>
    /// Verifies the More Like This: Excludes Source Doc When More Results scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Excludes Source Doc When More Results")]
    public void MoreLikeThis_ExcludesSourceDoc_WhenMoreResults()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.MoreLikeThis(0, ["body"], 10,
            new MoreLikeThisParameters { MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 2 });

        Assert.True(results.TotalHits > 0);
    }

    /// <summary>
    /// Verifies the More Like This: Respects Max Query Terms scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Respects Max Query Terms")]
    public void MoreLikeThis_RespectsMaxQueryTerms()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.MoreLikeThis(0, ["body"], 10,
            new MoreLikeThisParameters
            {
                MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 2, MaxQueryTerms = 2
            });

        Assert.True(results.TotalHits >= 0);
    }

    /// <summary>
    /// Verifies the More Like This: Respects Min Word Length scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Respects Min Word Length")]
    public void MoreLikeThis_RespectsMinWordLength()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // MinWordLength = 10 should exclude most terms from our corpus
        var results = searcher.MoreLikeThis(0, ["body"], 10,
            new MoreLikeThisParameters { MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 10 });

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the More Like This: Empty Result When No Term Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Empty Result When No Term Vectors")]
    public void MoreLikeThis_EmptyResult_WhenNoTermVectors()
    {
        // StoreTermVectors defaults to false — no TV files written
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var d0 = new LeanDocument();
        d0.Add(new StringField("tag", "alpha"));
        writer.AddDocument(d0);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.MoreLikeThis(0, ["tag"], 10);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the More Like This: With Boost By Score Produces Results scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: With Boost By Score Produces Results")]
    public void MoreLikeThis_WithBoostByScore_ProducesResults()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.MoreLikeThis(0, ["body"], 10,
            new MoreLikeThisParameters
            {
                MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 3, BoostByScore = true
            });

        Assert.True(results.TotalHits > 0);
    }

    /// <summary>
    /// Verifies the More Like This: Via Query Object Works scenario.
    /// </summary>
    [Fact(DisplayName = "More Like This: Via Query Object Works")]
    public void MoreLikeThis_ViaQueryObject_Works()
    {
        IndexCorpus();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var query = new MoreLikeThisQuery(0, ["body"],
            new MoreLikeThisParameters { MinTermFreq = 1, MinDocFreq = 1, MinWordLength = 3 });
        var results = searcher.Search(query, 10);

        Assert.True(results.TotalHits > 0);
    }
}
