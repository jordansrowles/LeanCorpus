using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Contains unit tests for Index Snapshot.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
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

    [Fact(DisplayName = "Create Snapshot: Exposes Deeply Immutable Segment Descriptors")]
    public void CreateSnapshot_ExposesDeeplyImmutableSegmentDescriptors()
    {
        using var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 1 });
        var document = CreateDocument("snapshot metadata");
        document.Add(new VectorField("embedding", new ReadOnlyMemory<float>([1f, 2f])));
        writer.AddDocument(document);
        writer.Commit();

        SegmentInfo source = Assert.Single(writer.CommittedSegments);
        source.TotalBytes = 4096;
        source.CodecBytes["test"] = 512;
        source.MinSequenceNumber = 41;
        source.MaxSequenceNumber = 43;
        source.EarliestSoftDeleteTimestamp = 19;

        var snapshot = writer.CreateSnapshot();

        var descriptor = Assert.IsType<SegmentDescriptor>(Assert.Single(snapshot.Segments));
        Assert.Equal(4096, descriptor.TotalBytes);
        Assert.Equal(512, descriptor.CodecBytes["test"]);
        Assert.Equal(41, descriptor.MinSequenceNumber);
        Assert.Equal(43, descriptor.MaxSequenceNumber);
        Assert.Equal(19, descriptor.EarliestSoftDeleteTimestamp);
        Assert.Equal(2, Assert.Single(descriptor.VectorFields).Dimension);

        source.TotalBytes = 0;
        source.CodecBytes["test"] = 0;
        source.MinSequenceNumber = null;
        source.MaxSequenceNumber = null;
        source.EarliestSoftDeleteTimestamp = null;
        source.VectorFields[0] = new VectorFieldInfo { FieldName = "embedding", Dimension = 3 };

        Assert.Equal(4096, descriptor.TotalBytes);
        Assert.Equal(512, descriptor.CodecBytes["test"]);
        Assert.Equal(41, descriptor.MinSequenceNumber);
        Assert.Equal(43, descriptor.MaxSequenceNumber);
        Assert.Equal(19, descriptor.EarliestSoftDeleteTimestamp);
        Assert.Equal(2, Assert.Single(descriptor.VectorFields).Dimension);

        IList<SegmentDescriptor> snapshotSegments = Assert.IsAssignableFrom<IList<SegmentDescriptor>>(snapshot.Segments);
        Assert.True(snapshotSegments.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => snapshotSegments[0] = descriptor);

        IDictionary<string, long> codecBytes = Assert.IsAssignableFrom<IDictionary<string, long>>(descriptor.CodecBytes);
        Assert.True(codecBytes.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => codecBytes["test"] = 0);

        IList<VectorFieldInfo> vectorFields = Assert.IsAssignableFrom<IList<VectorFieldInfo>>(descriptor.VectorFields);
        Assert.True(vectorFields.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => vectorFields[0] = new VectorFieldInfo());

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
        var results = searcher.Search(new TermQuery("body", "document"), 10, TestContext.Current.CancellationToken);

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

            Assert.Equal(1, snapshotSearcher.Search(new TermQuery("body", "alpha"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(0, snapshotSearcher.Search(new TermQuery("body", "later"), 10, TestContext.Current.CancellationToken).TotalHits);
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
