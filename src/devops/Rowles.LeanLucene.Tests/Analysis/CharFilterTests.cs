using Rowles.LeanLucene.Analysis.Filters;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Char Filter.
/// </summary>
public class CharFilterTests : IDisposable
{
    private readonly string _dir;

    public CharFilterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "charfilter_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { }
    }

    /// <summary>
    /// Verifies the HTML Strip Char Filter: Removes Tags scenario.
    /// </summary>
    [Fact(DisplayName = "HTML Strip Char Filter: Removes Tags")]
    public void HtmlStripCharFilter_RemovesTags()
    {
        var filter = new HtmlStripCharFilter();
        var result = filter.Filter("<p>Hello <b>world</b></p>".AsSpan());
        Assert.DoesNotContain("<", result);
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
    }

    /// <summary>
    /// Verifies the HTML Strip Char Filter: Removes Entities scenario.
    /// </summary>
    [Fact(DisplayName = "HTML Strip Char Filter: Removes Entities")]
    public void HtmlStripCharFilter_RemovesEntities()
    {
        var filter = new HtmlStripCharFilter();
        var result = filter.Filter("rock &amp; roll".AsSpan());
        Assert.DoesNotContain("&amp;", result);
    }

    /// <summary>
    /// Verifies the Pattern Replace Char Filter: Replaces Pattern scenario.
    /// </summary>
    [Fact(DisplayName = "Pattern Replace Char Filter: Replaces Pattern")]
    public void PatternReplaceCharFilter_ReplacesPattern()
    {
        var filter = new PatternReplaceCharFilter(@"\d+", "#");
        var result = filter.Filter("abc123def456".AsSpan());
        Assert.Equal("abc#def#", result);
    }

    /// <summary>
    /// Verifies the Mapping Char Filter: Maps Characters scenario.
    /// </summary>
    [Fact(DisplayName = "Mapping Char Filter: Maps Characters")]
    public void MappingCharFilter_MapsCharacters()
    {
        var mappings = new Dictionary<string, string>
        {
            ["\u201C"] = "\"", // left smart quote → straight
            ["\u201D"] = "\"", // right smart quote → straight
            ["\u2019"] = "'",  // right single smart quote → apostrophe
        };
        var filter = new MappingCharFilter(mappings);
        var result = filter.Filter("\u201CHello\u201D".AsSpan());
        Assert.Equal("\"Hello\"", result);
    }

    /// <summary>
    /// Verifies the Mapping Char Filter: No Mapping Returns Input scenario.
    /// </summary>
    [Fact(DisplayName = "Mapping Char Filter: No Mapping Returns Input")]
    public void MappingCharFilter_NoMapping_ReturnsInput()
    {
        var mappings = new Dictionary<string, string>();
        var filter = new MappingCharFilter(mappings);
        var result = filter.Filter("unchanged".AsSpan());
        Assert.Equal("unchanged", result);
    }

    /// <summary>
    /// Verifies the HTML Strip Char Filter: Integrated With Index Writer scenario.
    /// </summary>
    [Fact(DisplayName = "HTML Strip Char Filter: Integrated With Index Writer")]
    public void HtmlStripCharFilter_IntegratedWithIndexWriter()
    {
        var config = new IndexWriterConfig
        {
            CharFilters = [new HtmlStripCharFilter()]
        };

        using var writer = new IndexWriter(new MMapDirectory(_dir), config);
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "<p>searchable <b>content</b> here</p>"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // Should find "content" (tags stripped before indexing)
        var results = searcher.Search(new TermQuery("body", "content"), 10);
        Assert.True(results.TotalHits >= 1);

        // Should NOT find literal HTML tags
        var tagResults = searcher.Search(new TermQuery("body", "<p>"), 10);
        Assert.Equal(0, tagResults.TotalHits);
    }

    /// <summary>
    /// Verifies the Pattern Replace: Integrated With Index Writer scenario.
    /// </summary>
    [Fact(DisplayName = "Pattern Replace: Integrated With Index Writer")]
    public void PatternReplace_IntegratedWithIndexWriter()
    {
        var config = new IndexWriterConfig
        {
            CharFilters = [new PatternReplaceCharFilter(@"[_\-]", " ")]
        };

        using var writer = new IndexWriter(new MMapDirectory(_dir), config);
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello-world_test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // Hyphens/underscores replaced with spaces → separate tokens
        var results = searcher.Search(new TermQuery("body", "world"), 10);
        Assert.True(results.TotalHits >= 1);
    }

    /// <summary>
    /// Verifies the Multiple Char Filters: Chained In Order scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple Char Filters: Chained In Order")]
    public void MultipleCharFilters_ChainedInOrder()
    {
        // First strip HTML, then replace digits
        var config = new IndexWriterConfig
        {
            CharFilters =
            [
                new HtmlStripCharFilter(),
                new PatternReplaceCharFilter(@"\d+", "")
            ]
        };

        using var writer = new IndexWriter(new MMapDirectory(_dir), config);
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "<span>item123 test456</span>"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        // "item" should be found (digits stripped after HTML strip)
        var results = searcher.Search(new TermQuery("body", "item"), 10);
        Assert.True(results.TotalHits >= 1);
    }
}
