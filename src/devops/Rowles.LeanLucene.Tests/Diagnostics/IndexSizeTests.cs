using Rowles.LeanLucene.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Diagnostics;

/// <summary>
/// Contains unit tests for Index Size.
/// </summary>
public class IndexSizeTests : IDisposable
{
    private readonly string _dir;

    public IndexSizeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "size_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { }
    }

    /// <summary>
    /// Verifies the Calculate: Reports Non Zero Size scenario.
    /// </summary>
    [Fact(DisplayName = "Calculate: Reports Non Zero Size")]
    public void Calculate_ReportsNonZeroSize()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var report = IndexSizeCalculator.Calculate(_dir);

        Assert.True(report.TotalSizeBytes > 0);
        Assert.True(report.SegmentCount >= 1);
        Assert.True(report.CommitFileSizeBytes > 0);
    }

    /// <summary>
    /// Verifies the Calculate: Segment Breakdown Has Files scenario.
    /// </summary>
    [Fact(DisplayName = "Calculate: Segment Breakdown Has Files")]
    public void Calculate_SegmentBreakdown_HasFiles()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "testing segment sizes"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var report = IndexSizeCalculator.Calculate(_dir);

        Assert.NotEmpty(report.Segments);
        var seg = report.Segments[0];
        Assert.True(seg.TotalSizeBytes > 0);
        Assert.True(seg.FileSizes.Count > 0);
        // Should have at least .dic and .pos
        Assert.True(seg.FileSizes.ContainsKey(".dic") || seg.FileSizes.ContainsKey(".seg"));
    }

    /// <summary>
    /// Verifies the Calculate: Total Equals Sum scenario.
    /// </summary>
    [Fact(DisplayName = "Calculate: Total Equals Sum")]
    public void Calculate_TotalEqualsSum()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i} with some words"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        writer.Dispose();

        var report = IndexSizeCalculator.Calculate(_dir);

        long segTotal = report.Segments.Sum(s => s.TotalSizeBytes);
        long otherTotal = report.CommitFileSizeBytes + report.StatsFileSizeBytes;
        Assert.Equal(report.TotalSizeBytes, segTotal + otherTotal);
    }

    /// <summary>
    /// Verifies the Calculate: Formatted Size Is Readable scenario.
    /// </summary>
    [Fact(DisplayName = "Calculate: Formatted Size Is Readable")]
    public void Calculate_FormattedSize_IsReadable()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "format test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var report = IndexSizeCalculator.Calculate(_dir);
        Assert.False(string.IsNullOrEmpty(report.TotalSizeFormatted));
    }

    /// <summary>
    /// Verifies the Calculate: Missing Directory Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Calculate: Missing Directory Throws")]
    public void Calculate_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => IndexSizeCalculator.Calculate(Path.Combine(_dir, "nonexistent")));
    }

    /// <summary>
    /// Verifies the Searcher Get Index Size: Works scenario.
    /// </summary>
    [Fact(DisplayName = "Searcher Get Index Size: Works")]
    public void SearcherGetIndexSize_Works()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "searcher api test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var report = searcher.GetIndexSize();

        Assert.True(report.TotalSizeBytes > 0);
        Assert.Equal(_dir, report.DirectoryPath);
    }
}
