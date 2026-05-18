using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Tests for DocumentsWriterPerThread (DWPT) pool and lock-free concurrent indexing.
/// Verifies that the DWPT pool correctly partitions work across threads,
/// that lock-free document addition preserves correctness, and that
/// commit flushes all per-thread buffers to disk.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "DWPT")]
public sealed class DwptTests
{
    private static LeanDocument CreateDocument(int i)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", $"document number {i}"));
        doc.Add(new StringField("id", i.ToString()));
        return doc;
    }

    private static LeanDocument CreateBinaryDocument(int i)
    {
        var doc = CreateDocument(i);
        doc.Add(new BinaryField("payload", System.Text.Encoding.UTF8.GetBytes($"payload-{i}")));
        return doc;
    }

    /// <summary>
    /// Verifies the DWPT Pool: Lock Free Single Thread Indexes Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "DWPT Pool: Lock Free Single Thread Indexes Correctly")]
    public void DwptPool_LockFree_SingleThread_IndexesCorrectly()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig { MaxBufferedDocs = 500 };
            using var writer = new IndexWriter(mmap, config);

            writer.InitialiseDwptPool(1);

            // Act
            for (int i = 0; i < 100; i++)
            {
                writer.AddDocumentLockFree(CreateDocument(i));
            }

            writer.Commit();

            // Assert
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 200);

            Assert.Equal(100, results.TotalHits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the DWPT Pool: Multi Thread Indexes Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "DWPT Pool: Multi Thread Indexes Correctly")]
    public void DwptPool_MultiThread_IndexesCorrectly()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig { MaxBufferedDocs = 5000 };
            using var writer = new IndexWriter(mmap, config);

            writer.InitialiseDwptPool(4);

            // Act — add 1,000 documents across multiple threads via lock-free path
            Parallel.For(0, 1000, i =>
            {
                writer.AddDocumentLockFree(CreateDocument(i));
            });

            writer.Commit();

            // Assert — every document should be searchable
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 1500);

            Assert.Equal(1000, results.TotalHits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the DWPT Pool: Concurrent Batch Indexes Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "DWPT Pool: Concurrent Batch Indexes Correctly")]
    public void DwptPool_ConcurrentBatch_IndexesCorrectly()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig { MaxBufferedDocs = 1000 };
            using var writer = new IndexWriter(mmap, config);

            var docs = new List<LeanDocument>(500);
            for (int i = 0; i < 500; i++)
            {
                docs.Add(CreateDocument(i));
            }

            // Act — batch concurrent addition
            writer.AddDocumentsConcurrent(docs);
            writer.Commit();

            // Assert — all 500 documents should be searchable
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 600);

            Assert.Equal(500, results.TotalHits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "DWPT Pool: Concurrent Batch Preserves Binary Doc Values Per Document")]
    public void DwptPool_ConcurrentBatch_PreservesBinaryDocValuesPerDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            using var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 1000 });
            var docs = Enumerable.Range(0, 64).Select(CreateBinaryDocument).ToArray();

            writer.AddDocumentsConcurrent(docs);
            writer.Commit();

            using var searcher = new IndexSearcher(mmap);
            foreach (var reader in searcher.GetSegmentReaders())
            {
                for (int docId = 0; docId < reader.MaxDoc; docId++)
                {
                    var storedId = reader.GetStoredFields(docId)["id"][0];

                    Assert.True(reader.TryGetBinaryDocValues("id", docId, out var idValues));
                    var idValue = Assert.Single(idValues);
                    Assert.Equal(storedId, System.Text.Encoding.UTF8.GetString(idValue));

                    Assert.True(reader.TryGetBinaryDocValues("payload", docId, out var payloadValues));
                    var payloadValue = Assert.Single(payloadValues);
                    Assert.Equal($"payload-{storedId}", System.Text.Encoding.UTF8.GetString(payloadValue));
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "DWPT Pool: Concurrent Batch Uses RAM Threshold Flush")]
    public void DwptPool_ConcurrentBatch_UsesRamThresholdFlush()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            using var writer = new IndexWriter(mmap, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 0.001
            });
            string largeValue = new('x', 4096);
            var docs = Enumerable.Range(0, 16)
                .Select(i =>
                {
                    var doc = CreateDocument(i);
                    doc.Add(new StoredField("blob", largeValue));
                    return doc;
                })
                .ToArray();

            writer.AddDocumentsConcurrent(docs);

            Assert.NotEmpty(Directory.GetFiles(dir, "*.seg"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the DWPT: Estimated Ram Bytes Increases With Documents scenario.
    /// </summary>
    [Fact(DisplayName = "DWPT: Estimated Ram Bytes Increases With Documents")]
    public void Dwpt_EstimatedRamBytes_IncreasesWithDocuments()
    {
        // Arrange — create a DWPT directly (internal class, accessible via InternalsVisibleTo)
        var analyser = new StandardAnalyser();
        var dwpt = new DocumentsWriterPerThread(analyser, new Dictionary<string, IAnalyser>(), storePayloads: false);

        Assert.Equal(0, dwpt.EstimatedRamBytes);

        // Act — add 10 documents to the DWPT
        for (int i = 0; i < 10; i++)
        {
            dwpt.AddDocument(CreateDocument(i));
        }

        // Assert — RAM tracking should reflect buffered data
        Assert.True(dwpt.EstimatedRamBytes > 0,
            $"Expected EstimatedRamBytes > 0 after adding documents, but was {dwpt.EstimatedRamBytes}.");
        Assert.Equal(10, dwpt.DocCount);
    }

    /// <summary>
    /// Verifies the DWPT Pool: Commit Flushes All Buffers scenario.
    /// </summary>
    [Fact(DisplayName = "DWPT Pool: Commit Flushes All Buffers")]
    public void DwptPool_CommitFlushesAllBuffers()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig { MaxBufferedDocs = 5000 };
            using var writer = new IndexWriter(mmap, config);

            writer.InitialiseDwptPool(2);

            // Act — add documents via lock-free path, distributed across 2 DWPT slots
            for (int i = 0; i < 200; i++)
            {
                writer.AddDocumentLockFree(CreateDocument(i));
            }

            // Commit should flush all DWPT buffers to disk
            writer.Commit();

            // Assert — all documents from both DWPT slots should be indexed and searchable
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 300);

            Assert.Equal(200, results.TotalHits);

            // Verify segment files were written
            var segFiles = Directory.GetFiles(dir, "*.seg");
            Assert.NotEmpty(segFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
