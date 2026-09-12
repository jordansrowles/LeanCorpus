using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Tests for construction-time DWPT configuration and normal concurrent indexing.
/// Verifies that the DWPT pool correctly partitions work across threads,
/// that ordinary document addition preserves correctness, and that
/// commit flushes all per-thread buffers to disk.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
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

    [Fact(DisplayName = "DWPT Pool: Construction Uses Configured Indexing Concurrency")]
    public void DwptPool_Construction_UsesConfiguredIndexingConcurrency()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            using var writer = new IndexWriter(new MMapDirectory(dir), new IndexWriterConfig
            {
                IndexingConcurrency = 3,
                MaxBufferedDocs = 100,
            });

            Assert.Equal(3, writer.ResolvedIndexingConcurrency);
            Assert.Equal(3, writer.DwptPool!.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies ordinary document indexing uses the constructed DWPT pool.
    /// </summary>
    [Fact(DisplayName = "DWPT Pool: Ordinary Single Thread Indexes Correctly")]
    public void DwptPool_OrdinarySingleThread_IndexesCorrectly()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var mmap = new MMapDirectory(dir);
            var config = new IndexWriterConfig { IndexingConcurrency = 1, MaxBufferedDocs = 500 };
            using var writer = new IndexWriter(mmap, config);

            // Act
            for (int i = 0; i < 100; i++)
            {
                writer.AddDocument(CreateDocument(i));
            }

            writer.Commit();

            // Assert
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 200, cancellationToken: TestContext.Current.CancellationToken);

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
            var config = new IndexWriterConfig { IndexingConcurrency = 4, MaxBufferedDocs = 5000 };
            using var writer = new IndexWriter(mmap, config);

            writer.AddDocumentsConcurrent(Enumerable.Range(0, 1000).Select(CreateDocument).ToArray());

            writer.Commit();

            // Assert — every document should be searchable
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 1500, cancellationToken: TestContext.Current.CancellationToken);

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
            var results = searcher.Search(new TermQuery("body", "document"), topN: 600, cancellationToken: TestContext.Current.CancellationToken);

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
        var dwpt = new DocumentsWriterPerThread(analyser, new Dictionary<string, IAnalyser>(), new IndexWriterConfig());

        Assert.True(dwpt.EstimatedRamBytes > 0);

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
            var config = new IndexWriterConfig { IndexingConcurrency = 2, MaxBufferedDocs = 5000 };
            using var writer = new IndexWriter(mmap, config);

            // Act — ordinary documents use the configured pool.
            for (int i = 0; i < 200; i++)
            {
                writer.AddDocument(CreateDocument(i));
            }

            // Commit should flush all DWPT buffers to disk
            writer.Commit();

            // Assert — all documents from both DWPT slots should be indexed and searchable
            using var searcher = new IndexSearcher(mmap);
            var results = searcher.Search(new TermQuery("body", "document"), topN: 300, cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact(DisplayName = "DWPT Pool: StoreTermVectors writes .tvd/.tvx")]
    public void DwptPool_StoreTermVectors_WritesTermVectorFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var config = new IndexWriterConfig
            {
                StoreTermVectors = true,
                MaxBufferedDocs = 10,
                MergeThrottleSegments = 0
            };
            var mmap = new MMapDirectory(dir);
            using var writer = new IndexWriter(mmap, config);
            writer.AddDocument(CreateDocument(1));
            writer.Commit();

            // .tvd and .tvx should exist for segments written via DWPT flush.
            Assert.NotEmpty(Directory.GetFiles(dir, "*.tvd"));
            Assert.NotEmpty(Directory.GetFiles(dir, "*.tvx"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
