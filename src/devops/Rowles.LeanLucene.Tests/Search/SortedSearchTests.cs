using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Sorted Search.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Sorting")]
public sealed class SortedSearchTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SortedSearchTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Search: Sort By Doc ID Returns In Doc ID Order scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Doc ID Returns In Doc ID Order")]
    public void Search_SortByDocId_ReturnsInDocIdOrder()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("sort_docid"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "search engine"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act
        var results = searcher.Search(new TermQuery("body", "search"), 5, SortField.DocId);

        // Assert
        Assert.Equal(5, results.TotalHits);
        for (int i = 1; i < results.ScoreDocs.Length; i++)
            Assert.True(results.ScoreDocs[i].DocId > results.ScoreDocs[i - 1].DocId);
    }

    /// <summary>
    /// Verifies the Search: Sort By Numeric Field Returns Sorted By Value scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Numeric Field Returns Sorted By Value")]
    public void Search_SortByNumericField_ReturnsSortedByValue()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("sort_numeric"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var prices = new[] { 29.99, 9.99, 49.99, 19.99, 39.99 };
        for (int i = 0; i < prices.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "product item"));
            doc.Add(new NumericField("price", prices[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act — sort by price ascending
        var results = searcher.Search(new TermQuery("body", "product"), 5, SortField.Numeric("price"));

        // Assert — should be sorted by price ascending
        Assert.Equal(5, results.TotalHits);
        var sortedPrices = new List<double>();
        foreach (var sd in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(sd.DocId);
            sortedPrices.Add(double.Parse(stored["price"][0], System.Globalization.CultureInfo.InvariantCulture));
        }
        for (int i = 1; i < sortedPrices.Count; i++)
            Assert.True(sortedPrices[i] >= sortedPrices[i - 1],
                $"Expected {sortedPrices[i]} >= {sortedPrices[i - 1]} at position {i}");
    }

    /// <summary>
    /// Verifies string sorting uses the minimum sorted-set value when single-valued DocValues are absent.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Multi Valued String Uses Minimum Value")]
    public void Search_SortByMultiValuedString_UsesMinimumValue()
    {
        var path = SubDir("sort_multival_string");
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("body", "item"));
            doc1.Add(new StringField("id", "doc1"));
            doc1.Add(new StringField("tag", "zulu"));
            doc1.Add(new StringField("tag", "alpha"));
            writer.AddDocument(doc1);

            var doc2 = new LeanDocument();
            doc2.Add(new TextField("body", "item"));
            doc2.Add(new StringField("id", "doc2"));
            doc2.Add(new StringField("tag", "bravo"));
            writer.AddDocument(doc2);
            writer.Commit();
        }

        foreach (var pathToDelete in Directory.GetFiles(path, "seg_*.dvs"))
            File.Delete(pathToDelete);

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "item"), 10, SortField.String("tag"));

        Assert.Equal("doc1", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
    }

    /// <summary>
    /// Verifies numeric sorting uses the minimum sorted-numeric value when single-valued DocValues are absent.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Multi Valued Numeric Uses Minimum Value")]
    public void Search_SortByMultiValuedNumeric_UsesMinimumValue()
    {
        var path = SubDir("sort_multival_numeric");
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("body", "item"));
            doc1.Add(new StringField("id", "doc1"));
            doc1.Add(new NumericField("rank", 50));
            doc1.Add(new NumericField("rank", 1));
            writer.AddDocument(doc1);

            var doc2 = new LeanDocument();
            doc2.Add(new TextField("body", "item"));
            doc2.Add(new StringField("id", "doc2"));
            doc2.Add(new NumericField("rank", 10));
            writer.AddDocument(doc2);
            writer.Commit();
        }

        foreach (var pathToDelete in Directory.GetFiles(path, "seg_*.dvn").Concat(Directory.GetFiles(path, "seg_*.num")))
            File.Delete(pathToDelete);

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "item"), 10, SortField.Numeric("rank"));

        Assert.Equal("doc1", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
    }

    /// <summary>
    /// Verifies the Search: Sort By Numeric Descending Returns Highest First scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Numeric Descending Returns Highest First")]
    public void Search_SortByNumericDescending_ReturnsHighestFirst()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("sort_numeric_desc"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 1; i <= 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "item"));
            doc.Add(new NumericField("rank", i));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act — sort descending
        var results = searcher.Search(new TermQuery("body", "item"), 5, SortField.Numeric("rank", descending: true));

        // Assert — highest rank first
        Assert.Equal(5, results.TotalHits);
        var stored0 = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.Equal("5", stored0["rank"][0]);
    }

    /// <summary>
    /// Verifies the Search: Sort By Score Same As Default Search scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Sort By Score Same As Default Search")]
    public void Search_SortByScore_SameAsDefaultSearch()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("sort_score"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "search test"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act
        var defaultResults = searcher.Search(new TermQuery("body", "search"), 3);
        var sortedResults = searcher.Search(new TermQuery("body", "search"), 3, SortField.Score);

        // Assert
        Assert.Equal(defaultResults.TotalHits, sortedResults.TotalHits);
    }
}
