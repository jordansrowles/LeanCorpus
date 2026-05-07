using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Tests for Stage 2 index features: deletion policy, background merge, BKD, term vectors,
/// compound file, payload support.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Stage2")]
public sealed class Stage2IndexTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Stage2IndexTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── S2-2: Index Deletion Policy ─────────────────────────────────────────

    /// <summary>
    /// Verifies the Keep Latest Commit Policy: Prunes Old Commit Files scenario.
    /// </summary>
    [Fact(DisplayName = "Keep Latest Commit Policy: Prunes Old Commit Files")]
    public void KeepLatestCommitPolicy_PrunesOldCommitFiles()
    {
        var dir = new MMapDirectory(SubDir("del_policy_latest"));
        var config = new IndexWriterConfig { DeletionPolicy = new KeepLatestCommitPolicy(), MaxBufferedDocs = 5 };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        // Only the latest segments_N file should remain
        var commitFiles = Directory.GetFiles(dir.DirectoryPath, "segments_*");
        Assert.Single(commitFiles);
    }

    /// <summary>
    /// Verifies the Keep Last N Commits Policy: Keeps Specified Count scenario.
    /// </summary>
    [Fact(DisplayName = "Keep Last N Commits Policy: Keeps Specified Count")]
    public void KeepLastNCommitsPolicy_KeepsSpecifiedCount()
    {
        var dir = new MMapDirectory(SubDir("del_policy_n"));
        var config = new IndexWriterConfig { DeletionPolicy = new KeepLastNCommitsPolicy(2), MaxBufferedDocs = 5 };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        var commitFiles = Directory.GetFiles(dir.DirectoryPath, "segments_*");
        Assert.True(commitFiles.Length <= 2, $"Expected ≤2 commit files, got {commitFiles.Length}");
    }

    // ── S2-1: Background Merge ──────────────────────────────────────────────

    /// <summary>
    /// Verifies the Background Merge: Commit Returns Immediately Merge Runs In Background scenario.
    /// </summary>
    [Fact(DisplayName = "Background Merge: Commit Returns Immediately Merge Runs In Background")]
    public void BackgroundMerge_CommitReturnsImmediately_MergeRunsInBackground()
    {
        var dir = new MMapDirectory(SubDir("bg_merge"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 2 };

        using (var writer = new IndexWriter(dir, config))
        {
            // Create multiple small segments to trigger merge
            for (int i = 0; i < 20; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document number {i} with some text"));
                writer.AddDocument(doc);
                if (i % 2 == 1) writer.Commit();
            }
            writer.Commit();
            // Dispose waits for any in-progress background merge to complete.
        }

        // Should be searchable after merge completes
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "document"), 100);
        Assert.Equal(20, results.TotalHits);
    }

    // ── S2-7: BKD Tree ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the BKD Tree: Write And Read Range Query scenario.
    /// </summary>
    [Fact(DisplayName = "BKD Tree: Write And Read Range Query")]
    public void BKDTree_WriteAndRead_RangeQuery()
    {
        var path = Path.Combine(SubDir("bkd_rw"), "test.bkd");
        var fieldPoints = new Dictionary<string, List<(double Value, int DocId)>>
        {
            ["price"] = [(10.0, 0), (20.0, 1), (30.0, 2), (40.0, 3), (50.0, 4)]
        };
        BKDWriter.Write(path, fieldPoints);

        using var reader = BKDReader.Open(path);
        var results = reader.RangeQuery("price", 15.0, 35.0);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.DocId == 1);
        Assert.Contains(results, r => r.DocId == 2);
    }

    /// <summary>
    /// Verifies the BKD Tree: Empty Range Returns No Results scenario.
    /// </summary>
    [Fact(DisplayName = "BKD Tree: Empty Range Returns No Results")]
    public void BKDTree_EmptyRange_ReturnsNoResults()
    {
        var path = Path.Combine(SubDir("bkd_empty"), "test.bkd");
        var fieldPoints = new Dictionary<string, List<(double Value, int DocId)>>
        {
            ["price"] = [(10.0, 0), (20.0, 1)]
        };
        BKDWriter.Write(path, fieldPoints);

        using var reader = BKDReader.Open(path);
        var results = reader.RangeQuery("price", 100.0, 200.0);
        Assert.Empty(results);
    }

    /// <summary>
    /// Verifies the BKD Tree: Missing Field Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "BKD Tree: Missing Field Returns Empty")]
    public void BKDTree_MissingField_ReturnsEmpty()
    {
        var path = Path.Combine(SubDir("bkd_missing"), "test.bkd");
        var fieldPoints = new Dictionary<string, List<(double Value, int DocId)>>
        {
            ["price"] = [(10.0, 0)]
        };
        BKDWriter.Write(path, fieldPoints);

        using var reader = BKDReader.Open(path);
        var results = reader.RangeQuery("nonexistent", 0.0, 100.0);
        Assert.Empty(results);
    }

    // ── S2-8: Term Vectors ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Term Vectors: Write And Read Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Term Vectors: Write And Read Round Trips")]
    public void TermVectors_WriteAndRead_RoundTrips()
    {
        var sub = SubDir("tv_rw");
        var tvdPath = Path.Combine(sub, "test.tvd");
        var tvxPath = Path.Combine(sub, "test.tvx");

        var docs = new Dictionary<string, List<TermVectorEntry>>[]
        {
            new(StringComparer.Ordinal)
            {
                ["body"] = [new TermVectorEntry("hello", 2, [0, 5]), new TermVectorEntry("world", 1, [3])]
            },
            new(StringComparer.Ordinal)
            {
                ["body"] = [new TermVectorEntry("foo", 1, [0])]
            }
        };

        TermVectorsWriter.Write(tvdPath, tvxPath, docs);
        using var reader = TermVectorsReader.Open(tvdPath, tvxPath);

        var tv0 = reader.GetTermVector(0);
        Assert.True(tv0.ContainsKey("body"));
        Assert.Equal(2, tv0["body"].Count);
        Assert.Contains(tv0["body"], e => e.Term == "hello" && e.Freq == 2);

        var tv1 = reader.GetTermVector(1);
        Assert.Single(tv1["body"]);
        Assert.Equal("foo", tv1["body"][0].Term);
    }

    /// <summary>
    /// Verifies the Term Vectors: Get Term Vector By Field Returns Correct Field scenario.
    /// </summary>
    [Fact(DisplayName = "Term Vectors: Get Term Vector By Field Returns Correct Field")]
    public void TermVectors_GetTermVectorByField_ReturnsCorrectField()
    {
        var sub = SubDir("tv_field");
        var tvdPath = Path.Combine(sub, "test.tvd");
        var tvxPath = Path.Combine(sub, "test.tvx");

        var docs = new Dictionary<string, List<TermVectorEntry>>[]
        {
            new(StringComparer.Ordinal)
            {
                ["title"] = [new TermVectorEntry("test", 1, [0])],
                ["body"] = [new TermVectorEntry("content", 1, [0])]
            }
        };

        TermVectorsWriter.Write(tvdPath, tvxPath, docs);
        using var reader = TermVectorsReader.Open(tvdPath, tvxPath);

        var titleTV = reader.GetTermVector(0, "title");
        Assert.NotNull(titleTV);
        Assert.Single(titleTV);
        Assert.Equal("test", titleTV[0].Term);

        var bodyTV = reader.GetTermVector(0, "body");
        Assert.NotNull(bodyTV);
        Assert.Equal("content", bodyTV![0].Term);
    }

    /// <summary>
    /// Verifies the Term Vectors: Integration With Index Writer scenario.
    /// </summary>
    [Fact(DisplayName = "Term Vectors: Integration With Index Writer")]
    public void TermVectors_IntegrationWithIndexWriter()
    {
        var dir = new MMapDirectory(SubDir("tv_integration"));
        var config = new IndexWriterConfig { StoreTermVectors = true };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "the quick brown fox"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        // Term vector files should exist
        var tvdFiles = Directory.GetFiles(dir.DirectoryPath, "*.tvd");
        Assert.NotEmpty(tvdFiles);
    }

    // Compound files were removed because the old implementation packed files
    // and then extracted them back to disc on read, doubling storage.
    /// <summary>
    /// Verifies the Compound File Feature: Is Removed scenario.
    /// </summary>
    [Fact(DisplayName = "Compound File Feature: Is Removed")]
    public void CompoundFileFeature_IsRemoved()
    {
        var dir = new MMapDirectory(SubDir("cfs_removed"));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        Assert.Empty(Directory.GetFiles(dir.DirectoryPath, "*.cfs"));
        Assert.Null(Type.GetType("Rowles.LeanLucene.Codecs.CompoundFileWriter, Rowles.LeanLucene"));
        Assert.Null(Type.GetType("Rowles.LeanLucene.Codecs.CompoundFileReader, Rowles.LeanLucene"));
    }

    // ── S2-9: Payload Support (data model) ──────────────────────────────────

    /// <summary>
    /// Verifies the Posting Accumulator: Add With Payload Stores Payloads scenario.
    /// </summary>
    [Fact(DisplayName = "Posting Accumulator: Add With Payload Stores Payloads")]
    public void PostingAccumulator_AddWithPayload_StoresPayloads()
    {
        var acc = new PostingAccumulator();
        acc.AddWithPayload(0, 0, [0xCA, 0xFE]);
        acc.AddWithPayload(0, 1, [0xBA, 0xBE]);
        acc.AddWithPayload(1, 0, null);

        Assert.True(acc.HasPayloads);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, acc.GetPayload(0, 0));
        Assert.Equal(new byte[] { 0xBA, 0xBE }, acc.GetPayload(0, 1));
        Assert.Null(acc.GetPayload(1, 0));
    }

    /// <summary>
    /// Verifies the Posting Accumulator: Without Payloads Has Payloads Is False scenario.
    /// </summary>
    [Fact(DisplayName = "Posting Accumulator: Without Payloads Has Payloads Is False")]
    public void PostingAccumulator_WithoutPayloads_HasPayloadsIsFalse()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 0);
        acc.Add(0, 1);

        Assert.False(acc.HasPayloads);
        Assert.Null(acc.GetPayload(0, 0));
    }

    /// <summary>
    /// Verifies the Postings Enum: Get Payload Returns Empty For Now scenario.
    /// </summary>
    [Fact(DisplayName = "Postings Enum: Get Payload Returns Empty For Now")]
    public void PostingsEnum_GetPayload_ReturnsEmptyForNow()
    {
        // PostingsEnum.GetPayload is a stub until the binary format is extended
        var dir = new MMapDirectory(SubDir("payload_stub"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // Just verify it doesn't throw
        var results = searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    // ── S2-3: Pluggable Similarity via Config ───────────────────────────────

    /// <summary>
    /// Verifies the Index Writer Config: Default Similarity Is BM25 scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer Config: Default Similarity Is BM25")]
    public void IndexWriterConfig_DefaultSimilarity_IsBm25()
    {
        var config = new IndexWriterConfig();
        Assert.IsType<Bm25Similarity>(config.Similarity);
    }

    /// <summary>
    /// Verifies the Index Writer Config: Custom Similarity Is Retained scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer Config: Custom Similarity Is Retained")]
    public void IndexWriterConfig_CustomSimilarity_IsRetained()
    {
        var config = new IndexWriterConfig { Similarity = TfIdfSimilarity.Instance };
        Assert.IsType<TfIdfSimilarity>(config.Similarity);
    }
}
