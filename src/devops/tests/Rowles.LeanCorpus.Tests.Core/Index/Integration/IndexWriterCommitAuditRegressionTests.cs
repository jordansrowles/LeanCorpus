using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexWriterCommitAuditRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ll-writer-audit-{Guid.NewGuid():N}");

    public IndexWriterCommitAuditRegressionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_root);

    [Fact(DisplayName = "PrepareCommit: Writes next-generation stats without changing published generation")]
    public void PrepareCommit_WritesNextGenerationStatsWithoutChangingPublishedGeneration()
    {
        string path = SubDir(nameof(PrepareCommit_WritesNextGenerationStatsWithoutChangingPublishedGeneration));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLastNCommitsPolicy(2),
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            MaxBufferedDocs = 100,
            UseCompoundFile = false,
        });

        writer.AddDocument(CreateDocument("alpha beta"));
        writer.Commit();

        string generationOneStatsPath = IndexStats.GetStatsPath(path, 1);
        byte[] generationOneStats = File.ReadAllBytes(generationOneStatsPath);
        Assert.Equal(1, IndexStats.TryLoadFrom(generationOneStatsPath)!.LiveDocCount);

        writer.AddDocument(CreateDocument("gamma delta epsilon"));
        writer.PrepareCommit();

        Assert.Equal(generationOneStats, File.ReadAllBytes(generationOneStatsPath));
        string generationTwoStatsPath = IndexStats.GetStatsPath(path, 2);
        Assert.True(File.Exists(generationTwoStatsPath));
        Assert.Equal(2, IndexStats.TryLoadFrom(generationTwoStatsPath)!.LiveDocCount);
        Assert.Equal(1, writer.CommitGeneration);

        using (var publishedSearcher = new IndexSearcher(new MMapDirectory(path)))
        {
            Assert.Equal(1, publishedSearcher.CommitGeneration);
            Assert.Equal(1, publishedSearcher.Stats.LiveDocCount);
            Assert.Equal(1, publishedSearcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        }

        writer.Commit();

        using var nextSearcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(2, nextSearcher.CommitGeneration);
        Assert.Equal(2, nextSearcher.Stats.LiveDocCount);
        Assert.Equal(2, nextSearcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(generationOneStats, File.ReadAllBytes(generationOneStatsPath));
        Assert.Equal(2, IndexStats.TryLoadFrom(generationTwoStatsPath)!.LiveDocCount);
    }

    [Fact(DisplayName = "PrepareCommit rollback: Keeps published per-segment statistics")]
    public void PrepareCommitRollback_KeepsPublishedPerSegmentStatistics()
    {
        string path = SubDir(nameof(PrepareCommitRollback_KeepsPublishedPerSegmentStatistics));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLastNCommitsPolicy(2),
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            MaxBufferedDocs = 100,
            UseCompoundFile = false,
        });

        writer.AddDocument(CreateDocument("keep alpha beta"));
        writer.AddDocument(CreateDocument("remove gamma delta epsilon"));
        writer.Commit();

        SegmentInfo publishedSegment = Assert.Single(writer.GetNrtSegments());
        Assert.Null(publishedSegment.DelGeneration);
        string publishedStatsPath = Path.Combine(path, $"{publishedSegment.SegmentId}.stats.json");
        byte[] publishedStats = File.ReadAllBytes(publishedStatsPath);
        Assert.Equal(2, SegmentInfo.ReadFrom(Path.Combine(path, publishedSegment.SegmentId + ".seg")).LiveDocCount);

        writer.DeleteDocuments(new TermQuery("body", "remove"));
        writer.PrepareCommit();

        string preparedStatsPath = Path.Combine(path, $"{publishedSegment.SegmentId}_gen_2.stats.json");
        string preparedDeletesPath = Path.Combine(path, $"{publishedSegment.SegmentId}_gen_2.del");
        Assert.True(File.Exists(preparedStatsPath));
        Assert.True(File.Exists(preparedDeletesPath));
        Assert.Equal(publishedStats, File.ReadAllBytes(publishedStatsPath));
        Assert.Equal(1, SegmentStatsForTests.LiveDocCount(preparedStatsPath));

        writer.Rollback();

        SegmentInfo restoredSegment = Assert.Single(writer.GetNrtSegments());
        Assert.Null(restoredSegment.DelGeneration);
        Assert.Equal(2, restoredSegment.LiveDocCount);
        Assert.Equal(publishedStats, File.ReadAllBytes(publishedStatsPath));
        Assert.False(File.Exists(preparedStatsPath));
        Assert.False(File.Exists(preparedDeletesPath));

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(1, searcher.CommitGeneration);
        Assert.Equal(2, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "remove"), 10, TestContext.Current.CancellationToken).TotalHits);

        writer.DeleteDocuments(new TermQuery("body", "keep"));
        writer.Commit();
        using var afterDelete = new IndexSearcher(new MMapDirectory(path));
        var scanned = SegmentStats.FromSegmentReader(Assert.Single(afterDelete.GetSegmentReaders()));
        Assert.Equal(1, afterDelete.Stats.LiveDocCount);
        Assert.Equal(scanned.FieldLengthSums["body"], afterDelete.Stats.GetFieldLengthSum("body"));
        Assert.Equal(4, afterDelete.Stats.GetFieldLengthSum("body"));
        Assert.Equal(scanned.FieldDocCounts["body"], afterDelete.Stats.GetFieldDocCount("body"));
    }

    [Theory(DisplayName = "Writer startup: Allocates above an orphan retained by an mmap lease")]
    [InlineData(true)]
    [InlineData(false)]
    public void WriterStartup_AllocatesAboveOrphanRetainedByMmapLease(bool hasCommit)
    {
        string path = SubDir(nameof(WriterStartup_AllocatesAboveOrphanRetainedByMmapLease));
        if (hasCommit)
        using (var setupDirectory = new MMapDirectory(path))
        using (var setupWriter = new IndexWriter(setupDirectory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            MaxBufferedDocs = 100,
            UseCompoundFile = false,
        }))
        {
            setupWriter.AddDocument(CreateDocument("committed"));
            setupWriter.Commit();
        }

        const int occupiedOrdinal = 17;
        const string orphanFileName = "seg_17.seg";
        string orphanPath = Path.Combine(path, orphanFileName);
        File.WriteAllBytes(orphanPath, [0x42, 0x43, 0x44]);

        using var leaseDirectory = new MMapDirectory(path);
        using var orphanLease = leaseDirectory.OpenInput(orphanFileName);
        using (var writerDirectory = new MMapDirectory(path))
        using (var writer = new IndexWriter(writerDirectory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            MaxBufferedDocs = 1,
            UseCompoundFile = false,
        }))
        {
            Assert.True(File.Exists(orphanPath), "Startup cleanup should defer deleting the mmap-leased orphan.");

            writer.AddDocument(CreateDocument("newly allocated"));
            SegmentInfo allocated = Assert.Single(writer.GetNrtSegments()
                .Where(segment => !hasCommit || !string.Equals(segment.SegmentId, "seg_0", StringComparison.Ordinal)));
            int allocatedOrdinal = int.Parse(allocated.SegmentId.AsSpan("seg_".Length));
            Assert.True(allocatedOrdinal > occupiedOrdinal,
                $"Allocated {allocated.SegmentId} while occupied orphan seg_{occupiedOrdinal} still exists.");
            writer.Commit();
        }

        Assert.True(File.Exists(orphanPath), "The orphan remains until its last mapped lease is released.");
        if (!hasCommit)
            leaseDirectory.DeleteFile(orphanFileName);
        orphanLease.Dispose();
        Assert.False(File.Exists(orphanPath), "Releasing the final mapping should complete deferred orphan deletion.");
    }

    [Fact(DisplayName = "NRT snapshot: Remains searchable but cannot be backed up as a durable commit")]
    public void NrtSnapshot_RemainsSearchableButRejectsBackupAndManifest()
    {
        string path = SubDir(nameof(NrtSnapshot_RemainsSearchableButRejectsBackupAndManifest));
        string backupPath = Path.Combine(_root, "nrt_snapshot_backup");
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DeletionPolicy = new KeepLastNCommitsPolicy(2),
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            MaxBufferedDocs = 100,
            UseCompoundFile = false,
        });

        writer.AddDocument(CreateDocument("durable first"));
        writer.Commit();
        byte[] durableCommit = File.ReadAllBytes(Path.Combine(path, "segments_1"));
        byte[] durableStats = File.ReadAllBytes(IndexStats.GetStatsPath(path, 1));

        writer.AddDocument(CreateDocument("not yet durable"));
        IndexSnapshot snapshot = writer.CreateSnapshot();
        try
        {
            using (var nrtSearcher = new IndexSearcher(directory, snapshot.Segments))
            {
                Assert.Equal(2, nrtSearcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
            }

            Assert.Throws<InvalidOperationException>(() => writer.CreateBackupManifest(snapshot));
            Assert.Throws<InvalidOperationException>(() => writer.BackupSnapshot(
                snapshot, backupPath, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(1, writer.CommitGeneration);
            Assert.False(File.Exists(Path.Combine(path, "segments_2")));
            Assert.Equal(durableCommit, File.ReadAllBytes(Path.Combine(path, "segments_1")));
            Assert.Equal(durableStats, File.ReadAllBytes(IndexStats.GetStatsPath(path, 1)));
            Assert.False(Directory.Exists(backupPath));
        }
        finally
        {
            writer.ReleaseSnapshot(snapshot);
        }
    }

    [Fact]
    public void SnapshotBeforeFirstCommit_RejectsBackupAndManifest()
    {
        string path = SubDir(nameof(SnapshotBeforeFirstCommit_RejectsBackupAndManifest));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
        writer.AddDocument(CreateDocument("uncommitted"));
        var snapshot = writer.CreateSnapshot();
        try
        {
            using var searcher = new IndexSearcher(directory, snapshot.Segments);
            Assert.Equal(1, searcher.Search(new MatchAllDocsQuery(), 10).TotalHits);
            Assert.Throws<InvalidOperationException>(() => writer.CreateBackupManifest(snapshot));
            Assert.Throws<InvalidOperationException>(() => writer.BackupSnapshot(snapshot, Path.Combine(_root, "before_first")));
            Assert.Equal(0, writer.CommitGeneration);
            Assert.Empty(Directory.GetFiles(path, "segments_*"));
        }
        finally { writer.ReleaseSnapshot(snapshot); }
    }

    [Fact]
    public void OlderPublishedSnapshot_RemainsBackupEligibleAfterLaterCommit()
    {
        string path = SubDir(nameof(OlderPublishedSnapshot_RemainsBackupEligibleAfterLaterCommit));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            DeletionPolicy = new KeepLastNCommitsPolicy(2)
        });
        writer.AddDocument(CreateDocument("first"));
        writer.Commit();
        var snapshot = writer.CreateSnapshot();
        try
        {
            writer.AddDocument(CreateDocument("second"));
            writer.Commit();
            Assert.NotNull(writer.CreateBackupManifest(snapshot));
            var result = writer.BackupSnapshot(snapshot, Path.Combine(_root, "older_backup"));
            Assert.NotNull(result);
            IndexBackup.Restore(Path.Combine(_root, "older_backup"), Path.Combine(_root, "older_restore"),
                cancellationToken: TestContext.Current.CancellationToken);
            using var backupSearcher = new IndexSearcher(new MMapDirectory(Path.Combine(_root, "older_restore")));
            Assert.Equal(1, backupSearcher.Search(new MatchAllDocsQuery(), 10).TotalHits);
        }
        finally { writer.ReleaseSnapshot(snapshot); }
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument CreateDocument(string body)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        return document;
    }

    private static class SegmentStatsForTests
    {
        internal static int LiveDocCount(string path)
        {
            return SegmentStats.TryLoadFrom(path)!.LiveDocCount;
        }
    }
}
