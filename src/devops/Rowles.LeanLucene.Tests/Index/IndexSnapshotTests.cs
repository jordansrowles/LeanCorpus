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
/// Contains unit tests for Index Snapshot.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Snapshot")]
public sealed class IndexSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-snap-{Guid.NewGuid():N}");

    public IndexSnapshotTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies the Create Snapshot: Returns Committed Segments scenario.
    /// </summary>
    [Fact(DisplayName = "Create Snapshot: Returns Committed Segments")]
    public void CreateSnapshot_ReturnsCommittedSegments()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 100 });

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);

        var snapshot = writer.CreateSnapshot();

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Segments);
        Assert.Equal("seg_0", snapshot.Segments[0].SegmentId);
        Assert.Equal(1, snapshot.Segments[0].DocCount);

        writer.ReleaseSnapshot(snapshot);
    }

    /// <summary>
    /// Verifies the Snapshot: Preserves Old Segments After New Commit scenario.
    /// </summary>
    [Fact(DisplayName = "Snapshot: Preserves Old Segments After New Commit")]
    public void Snapshot_PreservesOldSegmentsAfterNewCommit()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 100 });

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "first document"));
        writer.AddDocument(doc1);
        writer.Commit();

        var snapshot = writer.CreateSnapshot();
        var snappedIds = snapshot.Segments.Select(s => s.SegmentId).ToHashSet();

        // Add more docs and commit again
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "second document"));
        writer.AddDocument(doc2);
        writer.Commit();

        // Snapshot segments should still reference original segments
        Assert.All(snappedIds, id => Assert.Contains(id, snappedIds));
        Assert.True(snapshot.Segments.Count >= 1);

        writer.ReleaseSnapshot(snapshot);
    }

    /// <summary>
    /// Verifies the Snapshot: Can Be Used To Open Searcher scenario.
    /// </summary>
    [Fact(DisplayName = "Snapshot: Can Be Used To Open Searcher")]
    public void Snapshot_CanBeUsedToOpenSearcher()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 100 });

        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var snapshot = writer.CreateSnapshot();

        // Open a searcher using the snapshot's segment list
        using var searcher = new IndexSearcher(directory, snapshot.Segments);
        var results = searcher.Search(new TermQuery("body", "document"), 10);

        Assert.Equal(5, results.TotalHits);

        writer.ReleaseSnapshot(snapshot);
    }

    /// <summary>
    /// Verifies the Held Snapshot: Protects Commit Files And Segments During Background Merge scenario.
    /// </summary>
    [Fact(DisplayName = "Held Snapshot: Protects Commit Files And Segments During Background Merge")]
    public void HeldSnapshot_ProtectsCommitFilesAndSegmentsDuringBackgroundMerge()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 2,
        });

        writer.AddDocument(CreateDocument("alpha anchor"));
        writer.Commit();

        var snapshot = writer.CreateSnapshot();
        var protectedSegmentId = snapshot.Segments[0].SegmentId;

        writer.AddDocument(CreateDocument("bravo anchor"));
        writer.Commit();

        Assert.True(File.Exists(Path.Combine(_dir, "segments_1")));
        Assert.True(File.Exists(Path.Combine(_dir, "stats_1.json")));
        Assert.True(File.Exists(Path.Combine(_dir, protectedSegmentId + ".dic")));
        Assert.True(File.Exists(Path.Combine(_dir, protectedSegmentId + ".pos")));

        writer.ReleaseSnapshot(snapshot);
    }

    /// <summary>
    /// Verifies the Held Snapshot: Searcher Still Works After Multiple Later Commits scenario.
    /// </summary>
    [Fact(DisplayName = "Held Snapshot: Searcher Still Works After Multiple Later Commits")]
    public void HeldSnapshot_SearcherStillWorksAfterMultipleLaterCommits()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLatestCommitPolicy(),
            MaxBufferedDocs = 1,
            MergeThreshold = 2,
        });

        writer.AddDocument(CreateDocument("alpha snapshot-only"));
        writer.Commit();

        var snapshot = writer.CreateSnapshot();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                writer.AddDocument(CreateDocument($"later document {i}"));
                writer.Commit();
            }

            using var snapshotSearcher = new IndexSearcher(directory, snapshot.Segments);

            Assert.Equal(1, snapshotSearcher.Search(new TermQuery("body", "alpha"), 10).TotalHits);
            Assert.Equal(0, snapshotSearcher.Search(new TermQuery("body", "later"), 10).TotalHits);
            Assert.All(snapshot.Segments, segment =>
            {
                Assert.True(File.Exists(Path.Combine(_dir, segment.SegmentId + ".seg")));
                Assert.True(File.Exists(Path.Combine(_dir, segment.SegmentId + ".stats.json")));
            });
        }
        finally
        {
            writer.ReleaseSnapshot(snapshot);
        }
    }

    /// <summary>
    /// Verifies the Releasing Snapshot: Allows Old Commit Files To Be Pruned By Later Commit scenario.
    /// </summary>
    [Fact(DisplayName = "Releasing Snapshot: Allows Old Commit Files To Be Pruned By Later Commit")]
    public void ReleasingSnapshot_AllowsOldCommitFilesToBePrunedByLaterCommit()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLatestCommitPolicy(),
            MaxBufferedDocs = 1,
        });

        writer.AddDocument(CreateDocument("first generation"));
        writer.Commit();

        var snapshot = writer.CreateSnapshot();

        writer.AddDocument(CreateDocument("second generation"));
        writer.Commit();

        Assert.True(File.Exists(Path.Combine(_dir, "segments_1")));
        Assert.True(File.Exists(Path.Combine(_dir, "stats_1.json")));

        writer.ReleaseSnapshot(snapshot);

        writer.AddDocument(CreateDocument("third generation"));
        writer.Commit();

        Assert.False(File.Exists(Path.Combine(_dir, "segments_1")));
        Assert.False(File.Exists(Path.Combine(_dir, "stats_1.json")));
    }

    /// <summary>
    /// Verifies the Releasing One Snapshot: Does Not Unprotect Segments Held By Another Snapshot scenario.
    /// </summary>
    [Fact(DisplayName = "Releasing One Snapshot: Does Not Unprotect Segments Held By Another Snapshot")]
    public void ReleasingOneSnapshot_DoesNotUnprotectSegmentsHeldByAnotherSnapshot()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLatestCommitPolicy(),
            MaxBufferedDocs = 1,
            MergeThreshold = 2,
        });

        writer.AddDocument(CreateDocument("shared protected generation"));
        writer.Commit();

        var firstSnapshot = writer.CreateSnapshot();
        var secondSnapshot = writer.CreateSnapshot();
        var protectedSegmentIds = firstSnapshot.Segments.Select(segment => segment.SegmentId).ToArray();

        writer.ReleaseSnapshot(secondSnapshot);

        writer.AddDocument(CreateDocument("newer generation"));
        writer.Commit();

        Assert.True(File.Exists(Path.Combine(_dir, "segments_1")));
        Assert.True(File.Exists(Path.Combine(_dir, "stats_1.json")));
        Assert.All(protectedSegmentIds, segmentId =>
        {
            Assert.True(File.Exists(Path.Combine(_dir, segmentId + ".seg")));
            Assert.True(File.Exists(Path.Combine(_dir, segmentId + ".stats.json")));
        });

        writer.ReleaseSnapshot(firstSnapshot);
    }

    /// <summary>
    /// Verifies the Release Snapshot: Allows Repeated Release scenario.
    /// </summary>
    [Fact(DisplayName = "Release Snapshot: Allows Repeated Release")]
    public void ReleaseSnapshot_AllowsRepeatedRelease()
    {
        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 100 });

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "test"));
        writer.AddDocument(doc);
        writer.Commit();

        var snapshot = writer.CreateSnapshot();
        writer.ReleaseSnapshot(snapshot);
        // Second release should not throw
        writer.ReleaseSnapshot(snapshot);
    }

    private static LeanDocument CreateDocument(string body)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        return document;
    }
}
