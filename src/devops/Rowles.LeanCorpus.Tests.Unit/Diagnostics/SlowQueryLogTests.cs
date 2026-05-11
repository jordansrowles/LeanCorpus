using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Diagnostics;

/// <summary>
/// Contains unit tests for Slow Query Log.
/// </summary>
public sealed class SlowQueryLogTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public SlowQueryLogTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Queries Exceeding Threshold: Are Logged scenario.
    /// </summary>
    [Fact(DisplayName = "Queries Exceeding Threshold: Are Logged")]
    public void QueriesExceedingThreshold_AreLogged()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(QueriesExceedingThreshold_AreLogged));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var sw = new StringWriter();
        using var log = new SlowQueryLog(thresholdMs: 0, sw); // 0ms = log everything

        var config = new IndexSearcherConfig { SlowQueryLog = log };
        using var searcher = new IndexSearcher(mmap, config);

        // Act
        searcher.Search(new TermQuery("body", "hello"), 10);

        // Assert
        var output = sw.ToString();
        Assert.Contains("TermQuery", output);
        Assert.Contains("ElapsedMs", output);
    }

    /// <summary>
    /// Verifies the Queries Below Threshold: Are Not Logged scenario.
    /// </summary>
    [Fact(DisplayName = "Queries Below Threshold: Are Not Logged")]
    public void QueriesBelowThreshold_AreNotLogged()
    {
        // Arrange
        var dir = Path.Combine(_path, nameof(QueriesBelowThreshold_AreNotLogged));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var sw = new StringWriter();
        // Very high threshold — nothing should be logged
        using var log = new SlowQueryLog(thresholdMs: 999_999, sw);

        var config = new IndexSearcherConfig { SlowQueryLog = log };
        using var searcher = new IndexSearcher(mmap, config);

        // Act
        searcher.Search(new TermQuery("body", "hello"), 10);

        // Assert
        Assert.Empty(sw.ToString());
    }
}
