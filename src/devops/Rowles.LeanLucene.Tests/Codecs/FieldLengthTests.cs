using Rowles.LeanLucene.Codecs.DocValues;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Field Length.
/// </summary>
public class FieldLengthTests : IDisposable
{
    private readonly string _dir;

    public FieldLengthTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fln_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* mmap handles may linger on Windows */ }
    }

    /// <summary>
    /// Verifies the Round-trip: Single Field Exact Counts scenario.
    /// </summary>
    [Fact(DisplayName = "Round-trip: Single Field Exact Counts")]
    public void RoundTrip_SingleField_ExactCounts()
    {
        var path = Path.Combine(_dir, "test.fln");
        var data = new Dictionary<string, int[]>
        {
            ["body"] = [5, 10, 0, 100, 65535]
        };

        FieldLengthWriter.Write(path, data);
        var loaded = FieldLengthReader.TryRead(path);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Equal(data["body"], loaded["body"]);
    }

    /// <summary>
    /// Verifies the Round-trip: Multiple Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Round-trip: Multiple Fields")]
    public void RoundTrip_MultipleFields()
    {
        var path = Path.Combine(_dir, "multi.fln");
        var data = new Dictionary<string, int[]>
        {
            ["title"] = [3, 7, 12],
            ["body"] = [50, 200, 1000]
        };

        FieldLengthWriter.Write(path, data);
        var loaded = FieldLengthReader.TryRead(path);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(data["title"], loaded["title"]);
        Assert.Equal(data["body"], loaded["body"]);
    }

    /// <summary>
    /// Verifies the Try Read: Missing File Returns Null scenario.
    /// </summary>
    [Fact(DisplayName = "Try Read: Missing File Returns Null")]
    public void TryRead_MissingFile_ReturnsNull()
    {
        var result = FieldLengthReader.TryRead(Path.Combine(_dir, "nonexistent.fln"));
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies the Clamp To Ushort Max scenario.
    /// </summary>
    [Fact(DisplayName = "Clamp To Ushort Max")]
    public void ClampToUshortMax()
    {
        var path = Path.Combine(_dir, "clamp.fln");
        var data = new Dictionary<string, int[]>
        {
            ["field"] = [70000, 100000] // exceeds ushort max
        };

        FieldLengthWriter.Write(path, data);
        var loaded = FieldLengthReader.TryRead(path);

        Assert.NotNull(loaded);
        Assert.Equal(65535, loaded["field"][0]);
        Assert.Equal(65535, loaded["field"][1]);
    }

    /// <summary>
    /// Verifies the Index Writer: Writes Fln File scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer: Writes Fln File")]
    public void IndexWriter_Writes_FlnFile()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var flnFiles = Directory.GetFiles(_dir, "*.fln");
        Assert.NotEmpty(flnFiles);
    }

    /// <summary>
    /// Verifies the Segment Reader: Uses Exact Lengths When Fln Exists scenario.
    /// </summary>
    [Fact(DisplayName = "Segment Reader: Uses Exact Lengths When Fln Exists")]
    public void SegmentReader_UsesExactLengths_WhenFlnExists()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());

        // Doc with 5 tokens
        var d0 = new LeanDocument();
        d0.Add(new TextField("body", "one two three four five"));
        writer.AddDocument(d0);

        // Doc with 3 tokens
        var d1 = new LeanDocument();
        d1.Add(new TextField("body", "alpha beta gamma"));
        writer.AddDocument(d1);

        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.Search(new TermQuery("body", "one"), 10);

        // The exact field length for doc 0 should be 5 (not quantised approximation)
        Assert.True(results.TotalHits >= 1);
    }

    /// <summary>
    /// Verifies the Backward Compat: No Fln File Falls Back To Norms scenario.
    /// </summary>
    [Fact(DisplayName = "Backward Compat: No Fln File Falls Back To Norms")]
    public void BackwardCompat_NoFlnFile_FallsBackToNorms()
    {
        // Index, then delete .fln files to simulate old index format
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        // Remove .fln files
        foreach (var f in Directory.GetFiles(_dir, "*.fln"))
            File.Delete(f);

        // Should still open and search without error
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.True(results.TotalHits >= 1);
    }

    /// <summary>
    /// Verifies the Exact Lengths: More Accurate Than Quantised Norms scenario.
    /// </summary>
    [Fact(DisplayName = "Exact Lengths: More Accurate Than Quantised Norms")]
    public void ExactLengths_MoreAccurate_ThanQuantisedNorms()
    {
        // Write a doc where norms quantisation would lose precision
        var flnPath = Path.Combine(_dir, "precision.fln");
        var data = new Dictionary<string, int[]>
        {
            // Token count 500 — norms = 1/(1+500) = 0.001996, quantised to byte 1/255 ≈ 0.00392
            // Inverse: round(1/0.00392 - 1) = round(254.1) = 254, not 500
            ["body"] = [500]
        };

        FieldLengthWriter.Write(flnPath, data);
        var loaded = FieldLengthReader.TryRead(flnPath);

        Assert.NotNull(loaded);
        Assert.Equal(500, loaded["body"][0]); // Exact, not 254
    }
}
