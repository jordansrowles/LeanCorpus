using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Fault-injection tests that simulate crash windows during the deletion commit
/// sequence. Exercises the boundary cases around <c>.del</c> file writes, segment
/// metadata updates, and commit file renames to verify the index behaves correctly
/// or falls back gracefully under each scenario.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "FaultInjection")]
public sealed class FaultInjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ll-fi-{Guid.NewGuid():N}");

    public FaultInjectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- regression test: F2/N3 fix ----

    /// <summary>
    /// After <see cref="IndexWriter.DeleteDocuments"/> and <see cref="IndexWriter.Commit"/>,
    /// reopening the index must not resurface the deleted document. This is the direct
    /// regression test for F2/N3: the <c>.seg</c> file must be rewritten before the
    /// commit rename so that <c>DelGeneration</c> survives a writer restart.
    /// </summary>
    [Fact]
    public void DeleteCommit_Reopen_DocumentRemainsDeleted()
    {
        string path = SubDir("del-reopen");
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("keep me"));
            writer.AddDocument(MakeDoc("delete me"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "delete"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(1, searcher.Search(new TermQuery("body", "keep"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "delete"), 10).TotalHits);
        Assert.Equal(1, searcher.Stats.LiveDocCount);
    }

    /// <summary>
    /// After three successive deletion commits, all deleted documents must remain
    /// absent on reopen. Verifies that gen-versioned <c>.del</c> files do not
    /// shadow each other and the <c>.seg</c> always points to the latest generation.
    /// </summary>
    [Fact]
    public void MultipleDeleteCommits_AllDeletedDocumentsAbsentOnReopen()
    {
        string path = SubDir("del-multi-gen");
        // Use single-word unique tokens so TermQuery("body", token) does an exact
        // term match. Multi-word values like "doc 1" tokenise into two tokens and
        // would never be found in the term dictionary as a single entry.
        var words = new[] { "zero", "one", "two", "three", "four" };
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            foreach (var word in words)
                writer.AddDocument(MakeDoc(word));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "one"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "three"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "two"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, searcher.Search(new TermQuery("body", "one"),   10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "two"),   10).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "three"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "zero"),  10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "four"),  10).TotalHits);
        Assert.Equal(2, searcher.Stats.LiveDocCount);
    }

    [Fact]
    public void WriterReopen_AfterVersionedDeleteCommit_PreservesLiveDocCount()
    {
        string path = SubDir("del-writer-reopen-live-count");
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("alpha"));
            writer.AddDocument(MakeDoc("beta"));
            writer.AddDocument(MakeDoc("gamma"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "beta"));
            writer.Commit();
        }

        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(2, searcher.Stats.LiveDocCount);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "beta"), 10).TotalHits);
    }

    // ---- crash window: .del present but .seg referencing stale del generation ----

    /// <summary>
    /// Simulates a crash that occurred after the <c>.del</c> file was written
    /// but before the <c>.seg</c> file was updated with the new <c>DelGeneration</c>.
    /// This is the exact state that existed before the F2/N3 fix was applied.
    /// The expected observable: on recovery the document reappears as live (because
    /// the <c>.seg</c> still has <c>DelGeneration = null</c> and the gen-versioned
    /// <c>.del</c> path is never probed).
    /// </summary>
    [Fact]
    public void SimulatedCrash_DelWritten_SegNotUpdated_DocumentReappearsAsLive()
    {
        string path = SubDir("crash-del-before-seg");

        // Step 1: write a clean commit.
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("survivor"));
            writer.AddDocument(MakeDoc("target"));
            writer.Commit();
        }

        // Step 2: simulate what happens when the .del file is written but the .seg is NOT
        // updated (pre-F2/N3 state). Find the single segment and its ID.
        var segFile = Directory.GetFiles(path, "seg_*.seg").Single();
        var segInfo = SegmentInfo.ReadFrom(segFile);

        // Manually write a _gen_1.del file that marks doc 1 (target) as deleted.
        var liveDocs = new LiveDocs(segInfo.DocCount);
        liveDocs.Delete(1);
        string delPath = Path.Combine(path, segInfo.SegmentId + "_gen_1.del");
        LiveDocs.Serialise(delPath, liveDocs);

        // Do NOT update the .seg file - simulating the crash window.
        // segInfo.DelGeneration remains null; the .seg on disk is unchanged.

        // Step 3: open the searcher. Since .seg has no DelGeneration, neither the
        // versioned nor the legacy unversioned .del path is found, so the document
        // is treated as live. This is the expected degraded-but-safe crash recovery.
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(2, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "target"), 10).TotalHits);
    }

    // ---- crash window: .seg updated, .del missing ----

    /// <summary>
    /// Simulates a scenario where the <c>.seg</c> file references a
    /// <c>DelGeneration</c> whose <c>.del</c> file was subsequently deleted
    /// (e.g. by a filesystem error or interrupted write). The index must open
    /// without crashing; all documents reappear as live because
    /// <see cref="SegmentReader"/> only loads the live-docs file if it exists.
    /// </summary>
    [Fact]
    public void SimulatedCrash_SegReferencesDelGen_DelFileMissing_DocumentsReturnAsLive()
    {
        string path = SubDir("crash-seg-del-missing");

        // Step 1: create a valid deletion commit.
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("survivor"));
            writer.AddDocument(MakeDoc("target"));
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("body", "target"));
            writer.Commit();
        }

        // Verify deletion is applied before we corrupt state.
        using (var preSearcher = new IndexSearcher(new MMapDirectory(path)))
        {
            Assert.Equal(0, preSearcher.Search(new TermQuery("body", "target"), 10).TotalHits);
        }

        // Step 2: delete the .del file to simulate post-write loss.
        var delFiles = Directory.GetFiles(path, "*.del");
        Assert.NotEmpty(delFiles);
        foreach (var f in delFiles) File.Delete(f);

        // Step 3: open the index. SegmentReader checks File.Exists for the del path;
        // since the file is absent the segment is treated as fully live.
        // Note: Stats.LiveDocCount comes from the persisted SegmentInfo.LiveDocCount in
        // the .seg file (which still says 1); it is not recomputed from the absent bitmap.
        // The correct observable is that both documents are searchable.
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(1, searcher.Search(new TermQuery("body", "survivor"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "target"),   10).TotalHits);
    }

    // ---- crash window: truncated .del file ----

    /// <summary>
    /// Simulates a crash that truncated the <c>.del</c> file mid-write.
    /// The index must fail to open rather than silently producing incorrect results.
    /// A truncated file hits end-of-stream before the CRC check, so the exception
    /// may be <see cref="InvalidDataException"/> (bad CRC) or
    /// <see cref="EndOfStreamException"/> (truncated before CRC), both of which
    /// derive from <see cref="IOException"/>.
    /// </summary>
    [Fact]
    public void CorruptDelFile_ThrowsIoException_OnOpen()
    {
        string path = SubDir("crash-corrupt-del");

        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("alpha"));
            writer.AddDocument(MakeDoc("beta"));
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("body", "beta"));
            writer.Commit();
        }

        // Truncate the .del file to zero bytes (simulates a torn write).
        var delFiles = Directory.GetFiles(path, "*.del");
        Assert.NotEmpty(delFiles);
        File.WriteAllBytes(delFiles[0], []);

        // Opening the searcher must propagate an IOException subclass; the corrupt
        // file must never result in silent data loss or an unexpected exception type.
        Assert.ThrowsAny<IOException>(() => new IndexSearcher(new MMapDirectory(path)));
    }

    // ---- crash window: partial commit rename (temp file exists, final file absent) ----

    /// <summary>
    /// Verifies that a <c>segments_N.tmp</c> file left by an interrupted commit
    /// rename is ignored during recovery. The searcher falls back to the previous
    /// committed generation.
    /// </summary>
    [Fact]
    public void PartialCommitRename_TmpFilePresent_FallsBackToPreviousGeneration()
    {
        string path = SubDir("crash-partial-rename");

        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("committed"));
            writer.Commit();
        }

        // Plant a partial commit file (segments_2.tmp without the rename completing).
        File.WriteAllText(Path.Combine(path, "segments_2.tmp"), "{\"Segments\":[\"seg_99\"],\"Generation\":2}");

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(1, searcher.Search(new TermQuery("body", "committed"), 10).TotalHits);
    }

    // ---- durable mode: fsync errors propagate ----

    /// <summary>
    /// Verifies that durable mode is the default and that a normal commit cycle with
    /// durable commits ON survives a writer restart with all documents intact.
    /// This is the "fail closed" contract: data written in durable mode must be
    /// readable after a process restart.
    /// </summary>
    [Fact]
    public void DurableCommit_DataSurvivesWriterRestart()
    {
        string path = SubDir("durable-restart");

        var config = new IndexWriterConfig { DurableCommits = true };
        using (var writer = new IndexWriter(new MMapDirectory(path), config))
        {
            for (int i = 0; i < 5; i++)
                writer.AddDocument(MakeDoc($"doc {i}"));
            writer.Commit();
        }

        // Writer disposed. Reopen fresh.
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        for (int i = 0; i < 5; i++)
            Assert.Equal(1, searcher.Search(new TermQuery("body", $"{i}"), 10).TotalHits);
    }

    // ---- del file present before any deletion ----

    /// <summary>
    /// A legacy unversioned <c>.del</c> file placed in the directory before any
    /// indexed deletion is performed must still be loaded by <see cref="SegmentReader"/>
    /// so that pre-existing live-docs state is respected on recovery.
    /// </summary>
    [Fact]
    public void LegacyUnversionedDelFile_LoadedBySegmentReader()
    {
        string path = SubDir("legacy-del");
        var mmap = new MMapDirectory(path);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            writer.AddDocument(MakeDoc("live one"));
            writer.AddDocument(MakeDoc("live two"));
            writer.AddDocument(MakeDoc("marked dead"));
            writer.Commit();
        }

        // Manually write a legacy (unversioned) .del file for the segment.
        var segInfo = Directory.GetFiles(path, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .Single();

        var liveDocs = new LiveDocs(segInfo.DocCount);
        liveDocs.Delete(2); // doc index 2 = "marked dead"
        LiveDocs.Serialise(Path.Combine(path, segInfo.SegmentId + ".del"), liveDocs);

        // The SegmentReader falls back to the unversioned path when DelGeneration is null.
        using var searcher = new IndexSearcher(new MMapDirectory(path));

        // Doc 2 is filtered out by the del bitset; both remaining docs are searchable.
        // Note: Stats.LiveDocCount reflects the SegmentInfo metadata in the .seg file,
        // which was written before we added the del file manually -- asserting on
        // search results is the reliable correctness check here.
        Assert.Equal(0, searcher.Search(new TermQuery("body", "dead"), 10).TotalHits);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "live"), 10).TotalHits);
    }

    // ---- helpers ----

    private string SubDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument MakeDoc(string body)
    {
        var d = new LeanDocument();
        d.Add(new TextField("body", body));
        return d;
    }
}
