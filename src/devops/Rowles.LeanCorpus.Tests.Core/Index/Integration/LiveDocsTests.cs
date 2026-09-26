using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Contains unit tests for Live Docs.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class LiveDocsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public LiveDocsTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Verifies the Live Docs: Delete Document Marked In Bitset scenario.
    /// </summary>
    [Fact(DisplayName = "Live Docs: Delete Document Marked In Bitset")]
    public void LiveDocs_DeleteDocument_MarkedInBitset()
    {
        var liveDocs = new LiveDocs(3);
        liveDocs.Delete(1);

        Assert.True(liveDocs.IsLive(0));
        Assert.False(liveDocs.IsLive(1));
        Assert.True(liveDocs.IsLive(2));
    }

    [Fact(DisplayName = "Live Docs: Delete Mutations Reject Out Of Range Document IDs")]
    public void LiveDocs_DeleteMutations_RejectOutOfRangeDocumentIds()
    {
        var liveDocs = new LiveDocs(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => liveDocs.Delete(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => liveDocs.Delete(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => liveDocs.SoftDelete(-1, 1234));
        Assert.Throws<ArgumentOutOfRangeException>(() => liveDocs.SoftDelete(3, 1234));
        Assert.Equal(3, liveDocs.LiveCount);
    }

    [Fact(DisplayName = "Live Docs: Deserialise Rejects Out Of Range Deleted IDs")]
    public void LiveDocs_Deserialise_RejectsOutOfRangeDeletedIds()
    {
        string path = Path.Combine(_fixture.Path, $"livedocs_out_of_range_{Guid.NewGuid():N}.del");
        var deletedDocs = new RoaringBitmap();
        deletedDocs.Add(3);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            deletedDocs.Serialise(writer);
            writer.Write(0);
        }

        Assert.Throws<InvalidDataException>(() => LiveDocs.Deserialise(path, maxDoc: 3));
    }

    [Fact(DisplayName = "Live Docs: Deserialise Rejects Truncated Soft Delete Timestamps")]
    public void LiveDocs_Deserialise_RejectsTruncatedSoftDeleteTimestamps()
    {
        string path = Path.Combine(_fixture.Path, $"livedocs_truncated_soft_deletes_{Guid.NewGuid():N}.del");
        var deletedDocs = new RoaringBitmap();
        deletedDocs.Add(1);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            deletedDocs.Serialise(writer);
            writer.Write(1); // One timestamp is declared, but no record follows.
        }

        Assert.Throws<InvalidDataException>(() => LiveDocs.Deserialise(path, maxDoc: 3));
    }

    [Fact(DisplayName = "Live Docs: Deserialise Rejects Soft Delete Timestamp For Live Document")]
    public void LiveDocs_Deserialise_RejectsSoftDeleteTimestampForLiveDocument()
    {
        string path = Path.Combine(_fixture.Path, $"livedocs_orphan_soft_delete_{Guid.NewGuid():N}.del");
        var deletedDocs = new RoaringBitmap();
        deletedDocs.Add(1);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            deletedDocs.Serialise(writer);
            writer.Write(1);
            writer.Write(2); // Document 2 is live in the bitmap.
            writer.Write(1234L);
        }

        Assert.Throws<InvalidDataException>(() => LiveDocs.Deserialise(path, maxDoc: 3));
    }

    [Fact(DisplayName = "Live Docs: Reader Rejects Soft Delete Timestamp Metadata Mismatch")]
    public void LiveDocs_ReaderRejectsSoftDeleteTimestampMetadataMismatch()
    {
        string directoryPath = Path.Combine(_fixture.Path, $"livedocs_timestamp_metadata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { SoftDeletesEnabled = true }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "timestamp mismatch"));
            writer.AddDocument(document);
            writer.Commit();
            writer.SoftDeleteDocuments(new TermQuery("body", "mismatch"));
            writer.Commit();
        }

        string segmentPath = Directory.GetFiles(directoryPath, "seg_*.seg").Single();
        var info = SegmentInfo.ReadFrom(segmentPath);
        Assert.NotNull(info.EarliestSoftDeleteTimestamp);
        info.EarliestSoftDeleteTimestamp = null;
        info.WriteTo(segmentPath);

        Assert.Throws<InvalidDataException>(() => new IndexSearcher(new MMapDirectory(directoryPath)));
    }

    /// <summary>
    /// Verifies the Live Docs: Deleted Doc Not Returned By Search scenario.
    /// </summary>
    [Fact(DisplayName = "Live Docs: Deleted Doc Not Returned By Search")]
    public void LiveDocs_DeletedDoc_NotReturnedBySearch()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "livedocs_search");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "alpha content here"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "beta content here"));
        writer.AddDocument(doc2);

        writer.DeleteDocuments(new TermQuery("body", "alpha"));
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "alpha"), 10, TestContext.Current.CancellationToken);
        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Delete And Reopen: Deleted Document Remains Deleted Non Deleted Document Found scenario.
    /// </summary>
    [Fact(DisplayName = "Delete And Reopen: Deleted Document Remains Deleted Non Deleted Document Found")]
    public void DeleteAndReopen_DeletedDocumentRemainsDeleted_NonDeletedDocumentFound()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "delete_reopen");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);

        // Index several documents with unique IDs
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("id", "doc1"));
            doc1.Add(new TextField("body", "first document content"));
            writer.AddDocument(doc1);

            var doc2 = new LeanDocument();
            doc2.Add(new TextField("id", "doc2"));
            doc2.Add(new TextField("body", "second document content"));
            writer.AddDocument(doc2);

            var doc3 = new LeanDocument();
            doc3.Add(new TextField("id", "doc3"));
            doc3.Add(new TextField("body", "third document content"));
            writer.AddDocument(doc3);

            writer.Commit();

            // Delete one document by term
            writer.DeleteDocuments(new TermQuery("id", "doc2"));
            writer.Commit();
        }

        // Open a NEW IndexWriter on the same directory
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            using var searcher = new IndexSearcher(dir);

            // Search for deleted document — assert 0 results
            var deletedResults = searcher.Search(new TermQuery("id", "doc2"), 10, TestContext.Current.CancellationToken);
            Assert.Equal(0, deletedResults.TotalHits);

            // Search for non-deleted documents — assert they're found
            var doc1Results = searcher.Search(new TermQuery("id", "doc1"), 10, TestContext.Current.CancellationToken);
            Assert.Equal(1, doc1Results.TotalHits);

            var doc3Results = searcher.Search(new TermQuery("id", "doc3"), 10, TestContext.Current.CancellationToken);
            Assert.Equal(1, doc3Results.TotalHits);
        }
    }

    /// <summary>
    /// Verifies the Delete Documents: While Merge Eligible Does Not Resurrect Deleted Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Delete Documents: While Merge Eligible Does Not Resurrect Deleted Docs")]
    public void DeleteDocuments_WhileMergeEligible_DoesNotResurrectDeletedDocs()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "delete_merge_eligible");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 2,
        }))
        {
            for (int i = 0; i < 6; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i % 2 == 0 ? "victim" : "survivor"));
                doc.Add(new TextField("body", i % 2 == 0 ? "delete target" : "keep target"));
                writer.AddDocument(doc);
                writer.Commit();
            }

            writer.DeleteDocuments(new TermQuery("id", "victim"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(0, searcher.Search(new TermQuery("id", "victim"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(3, searcher.Search(new TermQuery("id", "survivor"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    /// <summary>
    /// Verifies the Update Document: After Immediate Replacement Flush Replaces Exactly Once scenario.
    /// </summary>
    [Fact(DisplayName = "Update Document: After Immediate Replacement Flush Replaces Exactly Once")]
    public void UpdateDocument_AfterImmediateReplacementFlush_ReplacesExactlyOnce()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "update_merge_eligible");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 100,
        }))
        {
            for (int i = 0; i < 4; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", $"doc-{i}"));
                doc.Add(new TextField("body", $"old body {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }

            var replacement = new LeanDocument();
            replacement.Add(new StringField("id", "doc-1"));
            replacement.Add(new TextField("body", "new replacement body"));
            writer.UpdateDocument("id", "doc-1", replacement);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(0, searcher.Search(new TermQuery("body", "old"), 10, TestContext.Current.CancellationToken).ScoreDocs
            .Count(hit => searcher.GetStoredFields(hit.DocId)["id"][0] == "doc-1"));
        Assert.Equal(1, searcher.Search(new TermQuery("body", "replacement"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(4, searcher.Stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies the Delete Documents: Multiple Terms Across Segments Applies To All Live Segments scenario.
    /// </summary>
    [Fact(DisplayName = "Delete Documents: Multiple Terms Across Segments Applies To All Live Segments")]
    public void DeleteDocuments_MultipleTermsAcrossSegments_AppliesToAllLiveSegments()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "delete_multi_terms");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 1 }))
        {
            foreach (var id in new[] { "alpha", "bravo", "charlie", "delta" })
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", id));
                doc.Add(new TextField("body", $"document {id}"));
                writer.AddDocument(doc);
                writer.Commit();
            }

            writer.DeleteDocuments(new TermQuery("id", "alpha"));
            writer.DeleteDocuments(new TermQuery("id", "charlie"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(0, searcher.Search(new TermQuery("id", "alpha"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "charlie"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "bravo"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "delta"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    /// <summary>
    /// Verifies that soft deletes are applied to committed segments during
    /// commit and that the list is cleared; the single pre-flush application
    /// in CommitCore suffices (no second pass is needed).
    /// </summary>
    [Fact(DisplayName = "Soft Delete: Commit Applies Deletes Once And Clears Pending List")]
    public void SoftDelete_CommitAppliesDeletesOnce_AndClearsPendingList()
    {
        var subDir = System.IO.Path.Combine(_fixture.Path, "sd_commit_once");
        System.IO.Directory.CreateDirectory(subDir);

        var dir = new MMapDirectory(subDir);
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 2,
            SoftDeletesEnabled = true,
            SoftDeleteRetentionSeconds = 3600
        };

        using (var writer = new IndexWriter(dir, config))
        {
            // Index 6 documents across multiple segments (flush every 2)
            for (int i = 1; i <= 6; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", $"doc-{i}"));
                doc.Add(new TextField("body", $"content number {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();

            // Soft-delete three of them
            writer.SoftDeleteDocuments(new TermQuery("id", "doc-1"));
            writer.SoftDeleteDocuments(new TermQuery("id", "doc-3"));
            writer.SoftDeleteDocuments(new TermQuery("id", "doc-5"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        Assert.Equal(3, searcher.Stats.LiveDocCount);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "doc-1"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "doc-3"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "doc-5"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "doc-2"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "doc-4"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "doc-6"), 10, TestContext.Current.CancellationToken).TotalHits);
    }
}
