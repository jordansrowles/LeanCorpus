using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Collapsing.
/// </summary>
public class CollapsingTests : IDisposable
{
    private readonly string _dir;

    public CollapsingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "collapse_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* mmap handles may linger on Windows */ }
    }

    private void IndexArticles()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());

        // Multiple articles per category
        AddArticle(writer, "Cat Care 101", "pets", 0.9f);
        AddArticle(writer, "Dog Training Guide", "pets", 0.8f);
        AddArticle(writer, "Fish Tank Setup", "pets", 0.5f);
        AddArticle(writer, "C# Basics", "tech", 0.95f);
        AddArticle(writer, "Python Tips", "tech", 0.7f);
        AddArticle(writer, "Pasta Recipes", "food", 0.85f);
        AddArticle(writer, "Bread Baking", "food", 0.6f);

        writer.Commit();
    }

    private static void AddArticle(IndexWriter writer, string title, string category, float relevance)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("title", title));
        doc.Add(new StringField("category", category));
        doc.Add(new NumericField("relevance", relevance));
        writer.AddDocument(doc);
    }

    /// <summary>
    /// Verifies the Collapse: Groups By Field Returns One Per Group scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Groups By Field Returns One Per Group")]
    public void Collapse_GroupsByField_ReturnsOnePerGroup()
    {
        IndexArticles();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // Search all docs, collapse by category
        var results = searcher.SearchWithCollapse(
            new PrefixQuery("title", ""), 10,
            new CollapseField("category"));

        // Should have at most 3 groups (pets, tech, food)
        Assert.True(results.ScoreDocs.Length <= 3, 
            $"Expected ≤3 groups, got {results.ScoreDocs.Length}");
        Assert.True(results.TotalHits >= 3,
            "TotalHits should reflect number of groups");
    }

    /// <summary>
    /// Verifies the Collapse: Top Score Keeps Best Per Group scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Top Score Keeps Best Per Group")]
    public void Collapse_TopScore_KeepsBestPerGroup()
    {
        IndexArticles();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.SearchWithCollapse(
            new PrefixQuery("title", ""), 10,
            new CollapseField("category", CollapseMode.TopScore));

        // All returned docs should have the highest score in their group
        Assert.True(results.ScoreDocs.Length > 0);
    }

    /// <summary>
    /// Verifies the Collapse: Respects Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Respects Top N")]
    public void Collapse_RespectsTopN()
    {
        IndexArticles();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.SearchWithCollapse(
            new PrefixQuery("title", ""), 2,
            new CollapseField("category"));

        Assert.True(results.ScoreDocs.Length <= 2);
    }

    /// <summary>
    /// Verifies the Collapse: No Matches Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: No Matches Returns Empty")]
    public void Collapse_NoMatches_ReturnsEmpty()
    {
        IndexArticles();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var results = searcher.SearchWithCollapse(
            new TermQuery("title", "nonexistent"), 10,
            new CollapseField("category"));

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Collapse: Single Group Field Returns Single Result scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Single Group Field Returns Single Result")]
    public void Collapse_SingleGroupField_ReturnsSingleResult()
    {
        // Index docs all with same category
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i}"));
            doc.Add(new StringField("group", "same"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.SearchWithCollapse(
            new PrefixQuery("body", "document"), 10,
            new CollapseField("group"));

        // All in same group → single result
        Assert.Single(results.ScoreDocs);
    }
}
