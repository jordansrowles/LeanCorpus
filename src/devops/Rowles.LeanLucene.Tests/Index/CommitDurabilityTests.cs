using System.Text.Json;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Regression tests for C5: durable atomic commit.
/// Verifies that <see cref="IndexWriterConfig.DurableCommits"/> ensures committed
/// data round-trips intact through a writer restart, and that disabling the flag
/// does not regress correctness for the happy path.
/// </summary>
[Trait("Category", "Index")]
public class CommitDurabilityTests : IDisposable
{
    private readonly string _dir;

    public CommitDurabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ll_durable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>
    /// Verifies the Durable Commits: Defaults To True scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commits: Defaults To True")]
    public void DurableCommits_DefaultsToTrue()
    {
        Assert.True(new IndexWriterConfig().DurableCommits);
    }

    /// <summary>
    /// Verifies the Durable Commit: Round-trip Preserves All Documents scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commit: Round-trip Preserves All Documents")]
    public void DurableCommit_RoundTrip_PreservesAllDocuments()
    {
        // Arrange — write three commits with durability ON
        var config = new IndexWriterConfig { DurableCommits = true };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"payload number {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        // Act — re-open and search
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // Assert — every committed document survives the writer restart
        for (int i = 0; i < 3; i++)
        {
            var results = searcher.Search(new TermQuery("body", $"{i}"), 10);
            Assert.Equal(1, results.TotalHits);
        }
    }

    /// <summary>
    /// Verifies the Durable Commit: All Referenced Segment Files Present After Dispose scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commit: All Referenced Segment Files Present After Dispose")]
    public void DurableCommit_AllReferencedSegmentFilesPresentAfterDispose()
    {
        var config = new IndexWriterConfig { DurableCommits = true };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"durable {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var segmentsFile = Path.Combine(_dir, "segments_1");
        AssertNonEmptyFile(segmentsFile);
        AssertNonEmptyFile(Path.Combine(_dir, "stats_1.json"));

        var json = Rowles.LeanLucene.Index.CommitFileFormat.ReadJson(segmentsFile);
        var commit = JsonSerializer.Deserialize<JsonElement>(json);
        var segments = commit.GetProperty("Segments");
        Assert.NotEmpty(segments.EnumerateArray());

        foreach (var segmentElement in segments.EnumerateArray())
        {
            var segmentId = segmentElement.GetString()!;
            foreach (var extension in new[] { ".seg", ".dic", ".pos", ".nrm", ".fdt", ".fdx", ".fln", ".stats.json" })
                AssertNonEmptyFile(Path.Combine(_dir, segmentId + extension));
        }
    }

    /// <summary>
    /// Verifies the Durable Commits Disabled: Still Works scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commits Disabled: Still Works")]
    public void DurableCommitsDisabled_StillWorks()
    {
        // Arrange — ensure the opt-out path remains functional
        var config = new IndexWriterConfig { DurableCommits = false };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "non-durable but valid"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.Search(new TermQuery("body", "valid"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    private static void AssertNonEmptyFile(string path)
    {
        Assert.True(File.Exists(path), $"Expected {path} to exist");
        Assert.True(new FileInfo(path).Length > 0, $"Expected {path} to be non-empty");
    }
}
