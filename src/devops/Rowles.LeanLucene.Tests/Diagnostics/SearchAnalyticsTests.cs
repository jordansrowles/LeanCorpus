using Rowles.LeanLucene.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Diagnostics;

/// <summary>
/// Contains unit tests for Search Analytics.
/// </summary>
public sealed class SearchAnalyticsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public SearchAnalyticsTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Search Events: Are Captured scenario.
    /// </summary>
    [Fact(DisplayName = "Search Events: Are Captured")]
    public void SearchEvents_AreCaptured()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(SearchEvents_AreCaptured));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var analytics = new SearchAnalytics(capacity: 100);
        var config = new IndexSearcherConfig { SearchAnalytics = analytics };
        using var searcher = new IndexSearcher(mmap, config);

        // Act
        searcher.Search(new TermQuery("body", "hello"), 10);
        searcher.Search(new TermQuery("body", "world"), 10);

        // Assert
        var events = analytics.GetRecentEvents(10);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("TermQuery", e.QueryType));
    }

    /// <summary>
    /// Verifies the Ring Buffer: Drops Oldest When Full scenario.
    /// </summary>
    [Fact(DisplayName = "Ring Buffer: Drops Oldest When Full")]
    public void RingBuffer_DropsOldestWhenFull()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(RingBuffer_DropsOldestWhenFull));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var analytics = new SearchAnalytics(capacity: 2);
        var config = new IndexSearcherConfig { SearchAnalytics = analytics };
        using var searcher = new IndexSearcher(mmap, config);

        // Act — perform 3 searches, only the latest 2 should be retained
        searcher.Search(new TermQuery("body", "hello"), 10);
        searcher.Search(new TermQuery("body", "world"), 10);
        searcher.Search(new TermQuery("body", "hello"), 5);

        // Assert
        var events = analytics.GetRecentEvents(10);
        Assert.True(events.Count <= 2);
    }

    /// <summary>
    /// Verifies the Export JSON: Produces Valid JSON Array scenario.
    /// </summary>
    [Fact(DisplayName = "Export JSON: Produces Valid JSON Array")]
    public void ExportJson_ProducesValidJsonArray()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(ExportJson_ProducesValidJsonArray));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "test"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var analytics = new SearchAnalytics(capacity: 10);
        var config = new IndexSearcherConfig { SearchAnalytics = analytics };
        using var searcher = new IndexSearcher(mmap, config);

        searcher.Search(new TermQuery("body", "test"), 10);

        // Act
        var sw = new StringWriter();
        analytics.ExportJson(sw);
        var json = sw.ToString();

        // Assert
        Assert.StartsWith("[", json);
        Assert.EndsWith("]", json);
        Assert.Contains("TermQuery", json);
    }
}
