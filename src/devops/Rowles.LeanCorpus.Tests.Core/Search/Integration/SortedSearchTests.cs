using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Suggestions;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Contains unit tests for Sorted Search.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
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

    [Fact(DisplayName = "Search: Sorted Numeric Max Selector Uses Highest Value")]
    public void Search_SortedNumericMaxSelector_UsesHighestValue()
    {
        var path = SubDir("sort_multival_numeric_max");
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("body", "item"));
            doc1.Add(new StringField("id", "doc1"));
            doc1.Add(new NumericField("rank", 1));
            doc1.Add(new NumericField("rank", 50));
            writer.AddDocument(doc1);

            var doc2 = new LeanDocument();
            doc2.Add(new TextField("body", "item"));
            doc2.Add(new StringField("id", "doc2"));
            doc2.Add(new NumericField("rank", 10));
            writer.AddDocument(doc2);
            writer.Commit();
        }

        foreach (var pathToDelete in Directory.GetFiles(path, "seg_*.dvn")
                     .Concat(Directory.GetFiles(path, "seg_*.num")))
        {
            File.Delete(pathToDelete);
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new TermQuery("body", "item"),
            10,
            SortField.SortedNumeric("rank", SortValueSelector.Max));

        Assert.Equal("doc2", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact(DisplayName = "Search: Multiple Sort Fields Apply In Order")]
    public void Search_MultipleSortFields_ApplyInOrder()
    {
        var dir = new MMapDirectory(SubDir("sort_multiple_fields"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, group, rank) in new[]
                     {
                         ("a", "one", 1),
                         ("b", "one", 3),
                         ("c", "two", 2)
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "item"));
                doc.Add(new StringField("id", id));
                doc.Add(new StringField("group", group));
                doc.Add(new NumericField("rank", rank));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new TermQuery("body", "item"),
            10,
            SortField.String("group"),
            SortField.Numeric("rank", descending: true));
        var ids = results.ScoreDocs
            .Select(scoreDoc => searcher.GetStoredFields(scoreDoc.DocId)["id"][0])
            .ToArray();

        Assert.Equal(new[] { "b", "a", "c" }, ids);
    }

    [Fact(DisplayName = "Search After: Returns Stable Consecutive Pages")]
    public void SearchAfter_ReturnsStableConsecutivePages()
    {
        var dir = new MMapDirectory(SubDir("search_after"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int rank = 0; rank < 5; rank++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "item"));
                doc.Add(new NumericField("rank", rank));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var sort = SortField.Numeric("rank");
        var first = searcher.Search(new TermQuery("body", "item"), 2, sort);
        var second = searcher.SearchAfter(
            first.ScoreDocs[^1],
            new TermQuery("body", "item"),
            2,
            sort);

        Assert.Equal(new[] { 0, 1 }, first.ScoreDocs.Select(static scoreDoc => scoreDoc.DocId));
        Assert.Equal(new[] { 2, 3 }, second.ScoreDocs.Select(static scoreDoc => scoreDoc.DocId));
        Assert.Equal(5, second.TotalHits);
    }

    [Fact(DisplayName = "Query Rescorer: Second Pass Promotes Matching Documents")]
    public void QueryRescorer_SecondPassPromotesMatchingDocuments()
    {
        var dir = new MMapDirectory(SubDir("query_rescorer"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (id, body) in new[]
                     {
                         ("plain", "common"),
                         ("preferred", "common preferred")
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new TextField("body", body));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var firstPass = searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken);
        var rescored = new QueryRescorer(
            new TermQuery("body", "preferred"),
            weight: 5).Rescore(searcher, firstPass, 10);

        Assert.Equal(
            "preferred",
            searcher.GetStoredFields(rescored.ScoreDocs[0].DocId)["id"][0]);
    }

    [Fact(DisplayName = "Sort Rescorer: Reranks Only First Pass Candidates")]
    public void SortRescorer_ReranksOnlyFirstPassCandidates()
    {
        var dir = new MMapDirectory(SubDir("sort_rescorer"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int rank = 1; rank <= 3; rank++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "item"));
                doc.Add(new NumericField("rank", rank));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var firstPass = searcher.Search(new TermQuery("body", "item"), 2, TestContext.Current.CancellationToken);
        var rescored = new SortRescorer(
            SortField.Numeric("rank", descending: true))
            .Rescore(searcher, firstPass, 2);

        Assert.Equal(1, rescored.ScoreDocs[0].DocId);
        Assert.DoesNotContain(rescored.ScoreDocs, static scoreDoc => scoreDoc.DocId == 2);
    }

    [Fact(DisplayName = "Analysing Suggester: Applies Analysis And Context Filter")]
    public void AnalysingSuggester_AppliesAnalysisAndContextFilter()
    {
        var dir = new MMapDirectory(SubDir("analysing_suggester"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var (body, category) in new[]
                     {
                         ("apple", "fruit"),
                         ("application", "software")
                     })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", body));
                doc.Add(new StringField("category", category));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var suggestions = AnalysingSuggester.Suggest(
            searcher,
            "APP",
            "body",
            new StandardAnalyser(),
            contextFilter: new TermQuery("category", "fruit"));

        Assert.Single(suggestions);
        Assert.Equal("apple", suggestions[0].Term);
    }

    [Fact(DisplayName = "Free Text Suggester: Uses Phrase Context")]
    public void FreeTextSuggester_UsesPhraseContext()
    {
        var dir = new MMapDirectory(SubDir("free_text_suggester"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            foreach (var body in new[] { "new york", "new york", "new jersey", "old town" })
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", body));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var suggestions = FreeTextSuggester.Suggest(
            searcher,
            "new ",
            "body",
            new StandardAnalyser(),
            topN: 2);

        Assert.Equal(new[] { "york", "jersey" }, suggestions.Select(static value => value.Term));
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
        var defaultResults = searcher.Search(new TermQuery("body", "search"), 3, TestContext.Current.CancellationToken);
        var sortedResults = searcher.Search(new TermQuery("body", "search"), 3, SortField.Score);

        // Assert
        Assert.Equal(defaultResults.TotalHits, sortedResults.TotalHits);
    }
}
