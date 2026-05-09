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
/// Contains unit tests for Explain.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Explain")]
public sealed class ExplainTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ExplainTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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
    /// Verifies the Explain: Matching Doc Returns Breakdown scenario.
    /// </summary>
    [Fact(DisplayName = "Explain: Matching Doc Returns Breakdown")]
    public void Explain_MatchingDoc_ReturnsBreakdown()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("explain_match"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "search engine performance"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new TermQuery("body", "search");

        // Act
        var explanation = searcher.Explain(query, 0);

        // Assert
        Assert.NotNull(explanation);
        Assert.True(explanation.Score > 0);
        Assert.Contains("BM25", explanation.Description);
        Assert.True(explanation.Details.Length >= 3);
    }

    /// <summary>
    /// Verifies the Explain: Non Matching Doc Returns Null scenario.
    /// </summary>
    [Fact(DisplayName = "Explain: Non Matching Doc Returns Null")]
    public void Explain_NonMatchingDoc_ReturnsNull()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("explain_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "unrelated content"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = new TermQuery("body", "search");

        // Act
        var explanation = searcher.Explain(query, 0);

        // Assert
        Assert.Null(explanation);
    }

    /// <summary>
    /// Verifies the Explain: Invalid Doc ID Returns Null scenario.
    /// </summary>
    [Fact(DisplayName = "Explain: Invalid Doc ID Returns Null")]
    public void Explain_InvalidDocId_ReturnsNull()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("explain_invalid"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "test document"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act — doc ID 999 doesn't exist
        var explanation = searcher.Explain(new TermQuery("body", "test"), 999);

        // Assert
        Assert.Null(explanation);
    }

    /// <summary>
    /// Verifies the Explain: To String Produces Readable Output scenario.
    /// </summary>
    [Fact(DisplayName = "Explain: To String Produces Readable Output")]
    public void Explain_ToString_ProducesReadableOutput()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("explain_tostring"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "search engine performance"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // Act
        var explanation = searcher.Explain(new TermQuery("body", "search"), 0);

        // Assert
        var text = explanation!.ToString();
        Assert.Contains("idf", text);
        Assert.Contains("termFreq", text);
    }
}
