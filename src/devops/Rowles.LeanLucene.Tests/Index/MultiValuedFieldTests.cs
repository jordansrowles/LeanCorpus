using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Codecs.DocValues;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Multi Valued Field.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "MultiValuedField")]
public sealed class MultiValuedFieldTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MultiValuedFieldTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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
    /// Verifies the Multi Valued Text Field: All Values Stored scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Text Field: All Values Stored")]
    public void MultiValuedTextField_AllValuesStored()
    {
        var dir = new MMapDirectory(SubDir("multival_text"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("tags", "apple"));
        doc.Add(new TextField("tags", "banana"));
        doc.Add(new TextField("tags", "cherry"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stored = searcher.GetStoredFields(0);

        Assert.True(stored.ContainsKey("tags"));
        Assert.Equal(3, stored["tags"].Count);
        Assert.Equal("apple", stored["tags"][0]);
        Assert.Equal("banana", stored["tags"][1]);
        Assert.Equal("cherry", stored["tags"][2]);
    }

    /// <summary>
    /// Verifies the Multi Valued String Field: All Values Stored scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued String Field: All Values Stored")]
    public void MultiValuedStringField_AllValuesStored()
    {
        var dir = new MMapDirectory(SubDir("multival_string"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new StringField("category", "fruit"));
        doc.Add(new StringField("category", "food"));
        doc.Add(new StringField("category", "organic"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stored = searcher.GetStoredFields(0);

        Assert.True(stored.ContainsKey("category"));
        Assert.Equal(3, stored["category"].Count);
        Assert.Equal("fruit", stored["category"][0]);
        Assert.Equal("food", stored["category"][1]);
        Assert.Equal("organic", stored["category"][2]);
    }

    /// <summary>
    /// Verifies repeated string and numeric fields are preserved in richer DocValues.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Fields: Richer Doc Values Preserve Values")]
    public void MultiValuedFields_RicherDocValues_PreserveValues()
    {
        var path = SubDir("multival_docvalues");
        var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new StringField("category", "fruit"));
        doc.Add(new StringField("category", "food"));
        doc.Add(new NumericField("price", 10));
        doc.Add(new NumericField("price", 5));
        doc.Add(new StoredField("note", "first"));
        doc.Add(new StoredField("note", "second"));
        writer.AddDocument(doc);
        writer.Commit();

        var segmentId = Path.GetFileNameWithoutExtension(Directory.GetFiles(path, "seg_*.seg").Single());
        var sortedSet = SortedSetDocValuesReader.Read(Path.Combine(path, segmentId + ".dss"));
        var sortedNumeric = SortedNumericDocValuesReader.Read(Path.Combine(path, segmentId + ".dsn"));
        var binary = BinaryDocValuesReader.Read(Path.Combine(path, segmentId + ".dvb"));

        Assert.Equal(["food", "fruit"], sortedSet["category"][0]);
        Assert.Equal([5, 10], sortedNumeric["price"][0]);
        Assert.Equal(["first", "second"], binary["note"][0].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
    }

    /// <summary>
    /// Verifies the Multi Valued Field: Postings Generated For Each Value scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Field: Postings Generated For Each Value")]
    public void MultiValuedField_PostingsGeneratedForEachValue()
    {
        var dir = new MMapDirectory(SubDir("multival_postings"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Document with 3 occurrences of field "tags"
        var doc = new LeanDocument();
        doc.Add(new TextField("tags", "apple"));
        doc.Add(new TextField("tags", "banana"));
        doc.Add(new TextField("tags", "cherry"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        
        // Each tag value should be searchable
        var appleResults = searcher.Search(new TermQuery("tags", "apple"), 10);
        Assert.Equal(1, appleResults.TotalHits);

        var bananaResults = searcher.Search(new TermQuery("tags", "banana"), 10);
        Assert.Equal(1, bananaResults.TotalHits);

        var cherryResults = searcher.Search(new TermQuery("tags", "cherry"), 10);
        Assert.Equal(1, cherryResults.TotalHits);
    }

    /// <summary>
    /// Verifies the Multi Valued Field: Mixed With Single Value scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Field: Mixed With Single Value")]
    public void MultiValuedField_MixedWithSingleValue()
    {
        var dir = new MMapDirectory(SubDir("multival_mixed"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "My Document"));
        doc.Add(new TextField("tags", "apple"));
        doc.Add(new TextField("tags", "banana"));
        doc.Add(new TextField("author", "John Doe"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stored = searcher.GetStoredFields(0);

        // Single-value fields should have 1 value
        Assert.Single(stored["title"]);
        Assert.Equal("My Document", stored["title"][0]);
        Assert.Single(stored["author"]);
        Assert.Equal("John Doe", stored["author"][0]);

        // Multi-value field should have 2 values
        Assert.Equal(2, stored["tags"].Count);
        Assert.Equal("apple", stored["tags"][0]);
        Assert.Equal("banana", stored["tags"][1]);
    }

    /// <summary>
    /// Verifies the Multi Valued Numeric Field: All Values Stored scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Numeric Field: All Values Stored")]
    public void MultiValuedNumericField_AllValuesStored()
    {
        var dir = new MMapDirectory(SubDir("multival_numeric"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new NumericField("score", 10.5));
        doc.Add(new NumericField("score", 20.5));
        doc.Add(new NumericField("score", 30.5));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stored = searcher.GetStoredFields(0);

        Assert.True(stored.ContainsKey("score"));
        Assert.Equal(3, stored["score"].Count);
        Assert.Equal("10.5", stored["score"][0]);
        Assert.Equal("20.5", stored["score"][1]);
        Assert.Equal("30.5", stored["score"][2]);
    }

    /// <summary>
    /// Verifies the Multi Valued Field: Multiple Documents scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Field: Multiple Documents")]
    public void MultiValuedField_MultipleDocuments()
    {
        var dir = new MMapDirectory(SubDir("multival_multidoc"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Document 1 with 2 tags
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("id", "doc1"));
        doc1.Add(new TextField("tags", "red"));
        doc1.Add(new TextField("tags", "blue"));
        writer.AddDocument(doc1);

        // Document 2 with 3 tags
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("id", "doc2"));
        doc2.Add(new TextField("tags", "green"));
        doc2.Add(new TextField("tags", "yellow"));
        doc2.Add(new TextField("tags", "orange"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        
        var stored1 = searcher.GetStoredFields(0);
        Assert.Equal(2, stored1["tags"].Count);
        Assert.Equal("red", stored1["tags"][0]);
        Assert.Equal("blue", stored1["tags"][1]);

        var stored2 = searcher.GetStoredFields(1);
        Assert.Equal(3, stored2["tags"].Count);
        Assert.Equal("green", stored2["tags"][0]);
        Assert.Equal("yellow", stored2["tags"][1]);
        Assert.Equal("orange", stored2["tags"][2]);
    }

    /// <summary>
    /// Verifies the Multi Valued Field: Empty List Not Created scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Valued Field: Empty List Not Created")]
    public void MultiValuedField_EmptyListNotCreated()
    {
        var dir = new MMapDirectory(SubDir("multival_empty"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "Test Document"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var stored = searcher.GetStoredFields(0);

        // Should only have "title" field
        Assert.Single(stored);
        Assert.True(stored.ContainsKey("title"));
        Assert.False(stored.ContainsKey("tags"));
    }
}
