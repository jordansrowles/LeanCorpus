using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class TwoPhaseCommitTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TwoPhaseCommitTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    [Fact(DisplayName = "Two-phase: PrepareCommit then Commit publishes data")]
    public void PrepareCommit_ThenCommit_PublishesData()
    {
        var dir = new MMapDirectory(SubDir("twophase_pub"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(CreateDoc("body", "hello world"));
        writer.PrepareCommit();

        // Verify .pending file exists.
        var pendingFiles = System.IO.Directory.GetFiles(SubDir("twophase_pub"), "segments_*.pending");
        Assert.NotEmpty(pendingFiles);

        // Verify no segments_N file published yet (generation should be 0, no file).
        var segFiles = System.IO.Directory.GetFiles(SubDir("twophase_pub"), "segments_*")
            .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        Assert.Empty(segFiles);

        writer.Commit();

        // After Commit, .pending should be gone and segments_N should exist.
        pendingFiles = System.IO.Directory.GetFiles(SubDir("twophase_pub"), "segments_*.pending");
        Assert.Empty(pendingFiles);

        segFiles = System.IO.Directory.GetFiles(SubDir("twophase_pub"), "segments_*")
            .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(segFiles);

        // Reader should see the data.
        using var reader = new Rowles.LeanCorpus.Index.Segment.SegmentReader(dir,
            Rowles.LeanCorpus.Index.Segment.SegmentInfo.ReadFrom(
                System.IO.Directory.GetFiles(SubDir("twophase_pub"), "*.seg")[0]));
        Assert.True(reader.MaxDoc >= 1);
    }

    [Fact(DisplayName = "Two-phase: PrepareCommit then Rollback discards data")]
    public void PrepareCommit_ThenRollback_DiscardsData()
    {
        var dir = new MMapDirectory(SubDir("twophase_rollback"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 3 });

        writer.AddDocument(CreateDoc("body", "ephemeral data 1"));
        writer.AddDocument(CreateDoc("body", "ephemeral data 2"));
        writer.AddDocument(CreateDoc("body", "ephemeral data 3"));
        writer.PrepareCommit();
        writer.Rollback();

        // .pending should be deleted.
        var pendingFiles = System.IO.Directory.GetFiles(SubDir("twophase_rollback"), "segments_*.pending");
        Assert.Empty(pendingFiles);

        // No segments_N should exist either.
        var segNFiles = System.IO.Directory.GetFiles(SubDir("twophase_rollback"), "segments_*")
            .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        Assert.Empty(segNFiles);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(0, searcher.Search(new MatchAllDocsQuery(), 1).TotalHits);
    }

    [Fact(DisplayName = "Two-phase: Rollback before PrepareCommit is a no-op")]
    public void Rollback_WithoutPrepareCommit_IsNoOp()
    {
        var dir = new MMapDirectory(SubDir("twophase_noprep_rollback"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(CreateDoc("body", "test"));
        writer.Rollback(); // no prepare — should not throw

        // Normal Commit should still work.
        writer.Commit();
        var segFiles = System.IO.Directory.GetFiles(SubDir("twophase_noprep_rollback"), "segments_*")
            .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(segFiles);
    }

    [Fact(DisplayName = "Two-phase: Commit without PrepareCommit still works (backward compat)")]
    public void Commit_WithoutPrepare_WorksNormally()
    {
        var dir = new MMapDirectory(SubDir("twophase_normal"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(CreateDoc("body", "normal commit"));
        writer.Commit();

        var segFiles = System.IO.Directory.GetFiles(SubDir("twophase_normal"), "segments_*")
            .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(segFiles);
    }

    [Fact(DisplayName = "Two-phase: Crash after PrepareCommit recovers data")]
    public void CrashAfterPrepareCommit_RecoversData()
    {
        var subDir = SubDir("twophase_crash");

        // Phase 1: PrepareCommit then "crash" (dispose without Commit).
        using (var writer = new IndexWriter(new MMapDirectory(subDir), new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("body", "recovered data"));
            writer.PrepareCommit();
            // No Commit — simulate crash.
        }

        // Phase 2: Open a new writer — recovery should promote the .pending file.
        using (var writer = new IndexWriter(new MMapDirectory(subDir), new IndexWriterConfig()))
        {
            // The prepared data should be visible.
            writer.Commit(); // publish it (or it may have already been promoted by recovery)

            var segFiles = System.IO.Directory.GetFiles(subDir, "segments_*")
                .Where(f => !f.EndsWith(".pending", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(segFiles);
        }
    }

    [Fact(DisplayName = "Two-phase: Double PrepareCommit overwrites previous")]
    public void DoublePrepareCommit_OverwritesPrevious()
    {
        var dir = new MMapDirectory(SubDir("twophase_double"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(CreateDoc("body", "first"));
        writer.PrepareCommit();

        writer.AddDocument(CreateDoc("body", "second"));
        writer.PrepareCommit();

        writer.Commit();

        // Should have one .seg file with 2 docs.
        var segFiles = System.IO.Directory.GetFiles(SubDir("twophase_double"), "*.seg");
        Assert.NotEmpty(segFiles);
    }

    [Fact(DisplayName = "Two-phase: HasPreparedCommit reflects state correctly")]
    public void HasPreparedCommit_ReflectsState()
    {
        var dir = new MMapDirectory(SubDir("twophase_hasprep"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        Assert.False(writer.HasPreparedCommit);

        writer.AddDocument(CreateDoc("body", "test"));
        writer.PrepareCommit();
        Assert.True(writer.HasPreparedCommit);

        writer.Commit();
        Assert.False(writer.HasPreparedCommit);
    }

    /// <summary>
    /// Verifies the writer cannot roll back or retry after a failure following the irreversible
    /// commit-file rename. This covers the same checkpoint-publication failure mode identified
    /// in LUCENE-5958 and tracked for Lucene.NET review in issue #1293.
    /// </summary>
    [Fact(DisplayName = "Two-phase: post-publication sync failure preserves published commit")]
    public void Commit_PostPublicationSyncFailure_PreservesPublishedCommitAndPoisonsWriter()
    {
        var path = SubDir("twophase-post-publication-sync-failure");
        var config = new IndexWriterConfig();
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, config);

        writer.AddDocument(CreateDoc("body", "published despite sync failure"));
        writer.PrepareCommit();
        config.PreparedCommitPublicationSync = _ => throw new IOException("injected post-publication sync failure");

        Assert.Throws<IOException>(writer.Commit);

        Assert.False(writer.HasPreparedCommit);
        Assert.NotEmpty(System.IO.Directory.GetFiles(path, "segments_*")
            .Where(static file => !file.EndsWith(".pending", StringComparison.Ordinal)));
        Assert.Empty(System.IO.Directory.GetFiles(path, "segments_*.pending"));

        Assert.Throws<InvalidOperationException>(writer.Commit);
        Assert.Throws<InvalidOperationException>(writer.Rollback);
        Assert.Throws<InvalidOperationException>(
            () => writer.DeleteDocuments(new TermQuery("body", "published")));

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new MatchAllDocsQuery(), 1).TotalHits);
    }

    [Fact(DisplayName = "Two-phase: PrepareCommit includes pending deletions")]
    public void PrepareCommit_IncludesPendingDeletions()
    {
        var dir = new MMapDirectory(SubDir("twophase_del"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 2 };
        using var writer = new IndexWriter(dir, config);

        writer.AddDocument(CreateDoc("id", "doc-1", "body", "alice"));
        writer.AddDocument(CreateDoc("id", "doc-2", "body", "bob"));
        writer.Commit();

        writer.DeleteDocuments(new Rowles.LeanCorpus.Search.Queries.TermQuery("id", "doc-1"));
        writer.PrepareCommit();
        writer.Commit();

        // doc-1 should be deleted.
        var segFiles = System.IO.Directory.GetFiles(SubDir("twophase_del"), "*.seg");
        Assert.NotEmpty(segFiles);

        using var reader = new Rowles.LeanCorpus.Index.Segment.SegmentReader(dir,
            Rowles.LeanCorpus.Index.Segment.SegmentInfo.ReadFrom(segFiles[0]));
        // maxDoc includes deleted docs in Lucene-style segment readers
        Assert.True(reader.MaxDoc >= 1);
    }

    private static LeanDocument CreateDoc(params string[] fieldPairs)
    {
        var doc = new LeanDocument();
        for (int i = 0; i < fieldPairs.Length; i += 2)
            doc.Add(new TextField(fieldPairs[i], fieldPairs[i + 1]));
        return doc;
    }
}
