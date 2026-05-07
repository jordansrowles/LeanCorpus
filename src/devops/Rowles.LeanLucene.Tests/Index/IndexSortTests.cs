using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Index Sort.
/// </summary>
public sealed class IndexSortTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public IndexSortTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Numeric Sort: Documents Returned In Sorted Order scenario.
    /// </summary>
    [Fact(DisplayName = "Numeric Sort: Documents Returned In Sorted Order")]
    public void NumericSort_DocumentsReturnedInSortedOrder()
    {
        var dir = Path.Combine(_path, nameof(NumericSort_DocumentsReturnedInSortedOrder));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.Numeric("price"))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            AddDocWithPrice(writer, "expensive", 30.0);
            AddDocWithPrice(writer, "cheap", 10.0);
            AddDocWithPrice(writer, "mid", 20.0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new WildcardQuery("title", "*"), 10, SortField.Numeric("price"));

        Assert.Equal(3, results.TotalHits);

        var prices = GetStoredDoubles(searcher, results, "price");
        Assert.Equal([10.0, 20.0, 30.0], prices);
    }

    /// <summary>
    /// Verifies the String Sort: Documents Returned In Sorted Order scenario.
    /// </summary>
    [Fact(DisplayName = "String Sort: Documents Returned In Sorted Order")]
    public void StringSort_DocumentsReturnedInSortedOrder()
    {
        var dir = Path.Combine(_path, nameof(StringSort_DocumentsReturnedInSortedOrder));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.String("category"))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            AddDocWithCategory(writer, "cherry", "fruit");
            AddDocWithCategory(writer, "almond", "nut");
            AddDocWithCategory(writer, "banana", "fruit");
            AddDocWithCategory(writer, "cashew", "nut");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new WildcardQuery("name", "*"), 10, SortField.String("category"));

        Assert.Equal(4, results.TotalHits);

        var categories = GetStoredStrings(searcher, results, "category");
        Assert.Equal(["fruit", "fruit", "nut", "nut"], categories);
    }

    /// <summary>
    /// Verifies the Descending Sort: Documents Returned In Reverse Order scenario.
    /// </summary>
    [Fact(DisplayName = "Descending Sort: Documents Returned In Reverse Order")]
    public void DescendingSort_DocumentsReturnedInReverseOrder()
    {
        var dir = Path.Combine(_path, nameof(DescendingSort_DocumentsReturnedInReverseOrder));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.Numeric("price", descending: true))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            AddDocWithPrice(writer, "cheap", 10.0);
            AddDocWithPrice(writer, "expensive", 50.0);
            AddDocWithPrice(writer, "mid", 25.0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(
            new WildcardQuery("title", "*"), 10,
            SortField.Numeric("price", descending: true));

        var prices = GetStoredDoubles(searcher, results, "price");
        Assert.Equal([50.0, 25.0, 10.0], prices);
    }

    /// <summary>
    /// Verifies the Index Sort: Segment Info Persists Sort Metadata scenario.
    /// </summary>
    [Fact(DisplayName = "Index Sort: Segment Info Persists Sort Metadata")]
    public void IndexSort_SegmentInfoPersistsSortMetadata()
    {
        var dir = Path.Combine(_path, nameof(IndexSort_SegmentInfoPersistsSortMetadata));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.Numeric("price"))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            AddDocWithPrice(writer, "item", 10.0);
            writer.Commit();
        }

        var segFiles = Directory.GetFiles(dir, "*.seg");
        Assert.NotEmpty(segFiles);
        var segInfo = Rowles.LeanLucene.Index.Segment.SegmentInfo.ReadFrom(segFiles[0]);
        Assert.NotNull(segInfo.IndexSortFields);
        Assert.Single(segInfo.IndexSortFields);
        Assert.Equal("Numeric:price:False", segInfo.IndexSortFields[0]);
    }

    /// <summary>
    /// Verifies the Index Sort: Early Termination Matches Sort Order scenario.
    /// </summary>
    [Fact(DisplayName = "Index Sort: Early Termination Matches Sort Order")]
    public void IndexSort_EarlyTermination_MatchesSortOrder()
    {
        var dir = Path.Combine(_path, nameof(IndexSort_EarlyTermination_MatchesSortOrder));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.Numeric("price"))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            for (int i = 100; i >= 1; i--)
                AddDocWithPrice(writer, $"item{i}", i);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new WildcardQuery("title", "*"), 5, SortField.Numeric("price"));

        Assert.True(results.TotalHits >= 5);
        var prices = GetStoredDoubles(searcher, results, "price");
        Assert.Equal([1.0, 2.0, 3.0, 4.0, 5.0], prices);
    }

    /// <summary>
    /// Verifies the Index Sort: Text Search Still Works scenario.
    /// </summary>
    [Fact(DisplayName = "Index Sort: Text Search Still Works")]
    public void IndexSort_TextSearchStillWorks()
    {
        var dir = Path.Combine(_path, nameof(IndexSort_TextSearchStillWorks));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        var config = new IndexWriterConfig
        {
            IndexSort = new IndexSort(SortField.Numeric("price"))
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            AddDocWithPrice(writer, "hello world", 30.0);
            AddDocWithPrice(writer, "hello there", 10.0);
            AddDocWithPrice(writer, "goodbye world", 20.0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new TermQuery("title", "hello"), 10);
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Index Sort: Constructor Rejects Score Type scenario.
    /// </summary>
    [Fact(DisplayName = "Index Sort: Constructor Rejects Score Type")]
    public void IndexSort_Constructor_RejectsScoreType()
    {
        Assert.Throws<ArgumentException>(() => new IndexSort(SortField.Score));
    }

    /// <summary>
    /// Verifies the Index Sort: Constructor Rejects Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Index Sort: Constructor Rejects Empty")]
    public void IndexSort_Constructor_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new IndexSort());
    }

    private static List<double> GetStoredDoubles(IndexSearcher searcher, TopDocs results, string field)
    {
        var values = new List<double>();
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDoc.DocId);
            if (stored.TryGetValue(field, out var vals) && vals.Count > 0)
                values.Add(double.Parse(vals[0], System.Globalization.CultureInfo.InvariantCulture));
        }
        return values;
    }

    private static List<string> GetStoredStrings(IndexSearcher searcher, TopDocs results, string field)
    {
        var values = new List<string>();
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDoc.DocId);
            if (stored.TryGetValue(field, out var vals) && vals.Count > 0)
                values.Add(vals[0]);
        }
        return values;
    }

    private static void AddDocWithPrice(IndexWriter writer, string title, double price)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("title", title));
        doc.Add(new NumericField("price", price));
        writer.AddDocument(doc);
    }

    private static void AddDocWithCategory(IndexWriter writer, string name, string category)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("name", name));
        doc.Add(new StringField("category", category));
        writer.AddDocument(doc);
    }
}
