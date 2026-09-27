using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Tests for <see cref="IndexStats"/> persistence: write at commit time, load at searcher construction.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexStatsPersistenceTests : IDisposable
{
    private readonly string _dir;

    public IndexStatsPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ll_stats_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    /// <summary>
    /// Verifies the Commit: Writes Stats File scenario.
    /// </summary>
    [Fact(DisplayName = "Commit: Writes Stats File")]
    public void Commit_WritesStatsFile()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("hello world"));
            writer.Commit();
        }

        var statsPath = IndexStats.GetStatsPath(_dir, 1);
        Assert.True(File.Exists(statsPath), "stats_1.json should exist after commit");
    }

    /// <summary>
    /// Verifies the Persisted Stats: Match Recomputed Stats scenario.
    /// </summary>
    [Fact(DisplayName = "Persisted Stats: Match Recomputed Stats")]
    public void PersistedStats_MatchRecomputedStats()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("the quick brown fox"));
            writer.AddDocument(CreateDoc("jumped over the lazy dog"));
            writer.AddDocument(CreateDoc("hello"));
            writer.Commit();
        }

        var persisted = IndexStats.TryLoadFrom(IndexStats.GetStatsPath(_dir, 1));
        Assert.NotNull(persisted);

        using var searcher = new IndexSearcher(dir);
        var computed = searcher.Stats;

        Assert.Equal(computed.TotalDocCount, persisted.TotalDocCount);
        Assert.Equal(computed.LiveDocCount, persisted.LiveDocCount);
    }

    /// <summary>
    /// Verifies the Persisted Stats: After Deletion Updates Live Doc Count scenario.
    /// </summary>
    [Fact(DisplayName = "Persisted Stats: After Deletion Updates Live Doc Count")]
    public void PersistedStats_AfterDeletion_UpdatesLiveDocCount()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("alpha"));
            writer.AddDocument(CreateDoc("beta"));
            writer.AddDocument(CreateDoc("gamma"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "alpha"));
            writer.Commit();
        }

        var stats = IndexStats.TryLoadFrom(IndexStats.GetStatsPath(_dir, 2));
        Assert.NotNull(stats);
        Assert.Equal(3, stats.TotalDocCount);
        Assert.Equal(2, stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies sparse field statistics use the exact live document set for persisted and recomputed stats.
    /// </summary>
    [Fact(DisplayName = "Persisted Stats: Sparse Fields Respect Arbitrary Live Documents")]
    public void PersistedStats_SparseFieldsRespectArbitraryLiveDocuments()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 100,
            MergeThreshold = 100,
        }))
        {
            writer.AddDocument(CreateSparseDoc(bodyText: "alpha beta"));
            writer.AddDocument(CreateSparseDoc(titleText: "middle"));
            writer.AddDocument(CreateSparseDoc(bodyText: "gamma"));
            writer.AddDocument(CreateSparseDoc(titleText: "omega psi"));
            writer.Commit();

            writer.DeleteDocuments(new TermQuery("body", "alpha"));
            writer.DeleteDocuments(new TermQuery("title", "omega"));
            writer.Commit();
        }

        var statsPath = IndexStats.GetStatsPath(_dir, 2);
        var persisted = IndexStats.TryLoadFrom(statsPath);
        Assert.NotNull(persisted);
        AssertSparseLiveStats(persisted);

        File.Delete(statsPath);
        using var searcher = new IndexSearcher(dir);
        AssertSparseLiveStats(searcher.Stats);
    }

    /// <summary>
    /// Verifies a corrupt field-length frame fails structurally and a repaired frame can be read by the same reader.
    /// </summary>
    [Fact(DisplayName = "Segment Reader: Corrupt Field Lengths Can Be Retried")]
    public void SegmentReader_CorruptFieldLengthsCanBeRetried()
    {
        var dir = new MMapDirectory(_dir);
        SegmentInfo segmentInfo;
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("alpha beta"));
            writer.Commit();
            segmentInfo = writer.GetNrtSegments()[0];
        }

        string path = Path.Combine(_dir, segmentInfo.SegmentId + ".fln");
        byte[] validBytes = File.ReadAllBytes(path);
        byte[] corruptBytes = (byte[])validBytes.Clone();
        corruptBytes[^1] ^= 0x01;
        File.WriteAllBytes(path, corruptBytes);

        using var reader = new SegmentReader(dir, segmentInfo);
        var exception = Assert.Throws<CodecFileException>(
            () => reader.TryGetFieldLengths("body", out _));
        Assert.Equal(CodecFileErrorCode.ChecksumMismatch, exception.ErrorCode);

        File.WriteAllBytes(path, validBytes);
        Assert.True(reader.TryGetFieldLengths("body", out var lengths));
        Assert.Equal([2], lengths);
    }

    [Fact(DisplayName = "Statistics: Unversioned Sidecars Are Recomputed")]
    public void Statistics_UnversionedSidecarsAreRejected()
    {
        string indexStatsPath = IndexStats.GetStatsPath(_dir, 1);
        File.WriteAllText(indexStatsPath,
            "{\"totalDocCount\":1,\"liveDocCount\":1,\"avgFieldLengths\":{\"body\":1},\"fieldDocCounts\":{\"body\":1},\"fieldLengthSums\":{\"body\":1}}");
        Assert.Null(IndexStats.TryLoadFrom(indexStatsPath));

        string segmentStatsPath = SegmentStats.GetStatsPath(_dir, "seg_0");
        File.WriteAllText(segmentStatsPath,
            "{\"totalDocCount\":1,\"liveDocCount\":1,\"fieldLengthSums\":{\"body\":1},\"fieldDocCounts\":{\"body\":1}}");
        Assert.Null(SegmentStats.TryLoadFrom(segmentStatsPath));
    }

    [Fact(DisplayName = "Statistics: Legacy Segments Derive Field Presence From Postings")]
    public void Statistics_LegacySegmentsDeriveFieldPresenceFromPostings()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = false,
        }))
        {
            writer.AddDocument(CreateSparseDoc(bodyText: "alpha beta"));
            writer.AddDocument(CreateSparseDoc(titleText: "middle"));
            writer.Commit();
        }

        File.Delete(Path.Combine(_dir, "seg_0.fln"));
        File.Delete(IndexStats.GetStatsPath(_dir, 1));

        SegmentInfo legacySegment = SegmentInfo.ReadFrom(Path.Combine(_dir, "seg_0.seg"));
        using var legacyReader = new SegmentReader(dir, legacySegment);
        int expectedBodyLength = legacyReader.GetFieldLength(0, "body");
        int expectedTitleLength = legacyReader.GetFieldLength(1, "title");

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(2, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Stats.GetFieldDocCount("body"));
        Assert.Equal(expectedBodyLength, searcher.Stats.GetFieldLengthSum("body"));
        Assert.Equal(1, searcher.Stats.GetFieldDocCount("title"));
        Assert.Equal(expectedTitleLength, searcher.Stats.GetFieldLengthSum("title"));
    }

    [Fact(DisplayName = "Merge Stats: Retained Soft Deletes Keep Exact Field Presence")]
    public void MergeStats_RetainedSoftDeletesKeepExactFieldPresence()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 2,
            MergeThreshold = 100,
            MergePolicy = NoMergePolicy.Instance,
            SoftDeletesEnabled = true,
            SoftDeleteRetentionSeconds = 3600,
        }))
        {
            writer.AddDocument(CreateSparseDoc(bodyText: "alpha beta"));
            writer.AddDocument(CreateSparseDoc(titleText: "middle"));
            writer.Commit();

            writer.AddDocument(CreateSparseDoc(bodyText: "gamma"));
            writer.AddDocument(CreateSparseDoc(titleText: "omega psi"));
            writer.Commit();

            Assert.Equal(2, writer.GetNrtSegments().Count);
            writer.SoftDeleteDocuments(new TermQuery("body", "alpha"));
            writer.SoftDeleteDocuments(new TermQuery("title", "omega"));
            writer.Commit();

            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        SegmentInfo mergedSegment = Assert.Single(Directory.GetFiles(_dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom));
        Assert.Equal(4, mergedSegment.DocCount);
        Assert.Equal(2, mergedSegment.LiveDocCount);

        SegmentStats mergedStats = Assert.IsType<SegmentStats>(
            SegmentStats.TryLoadFrom(SegmentStats.GetStatsPath(_dir, mergedSegment.SegmentId)));
        Assert.Equal(1, mergedStats.FieldDocCounts["body"]);
        Assert.Equal(1, mergedStats.FieldLengthSums["body"]);
        Assert.Equal(1, mergedStats.FieldDocCounts["title"]);
        Assert.Equal(1, mergedStats.FieldLengthSums["title"]);

        using var searcher = new IndexSearcher(dir);
        IndexStats commitStats = Assert.IsType<IndexStats>(
            IndexStats.TryLoadFrom(IndexStats.GetStatsPath(_dir, searcher.CommitGeneration)));
        AssertSparseLiveStats(commitStats);
    }

    /// <summary>
    /// Verifies the Commit Stats: Uses Per Segment Stats Without Opening Old Segment Data scenario.
    /// </summary>
    [Fact(DisplayName = "Commit Stats: Uses Per Segment Stats Without Opening Old Segment Data")]
    public void CommitStats_UsesPerSegmentStatsWithoutOpeningOldSegmentData()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 100,
            MergeThreshold = 100,
        });

        writer.AddDocument(CreateDoc("alpha"));
        writer.Commit();

        var firstSegmentStatsPath = SegmentStats.GetStatsPath(_dir, "seg_0");
        Assert.True(File.Exists(firstSegmentStatsPath));

        File.Delete(Path.Combine(_dir, "seg_0.dic"));
        File.Delete(Path.Combine(_dir, "seg_0.pos"));

        writer.AddDocument(CreateDoc("beta"));
        writer.Commit();

        var stats = IndexStats.TryLoadFrom(IndexStats.GetStatsPath(_dir, 2));
        Assert.NotNull(stats);
        Assert.Equal(2, stats.TotalDocCount);
        Assert.Equal(2, stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies the Missing Stats File: Falls Back To Recomputation scenario.
    /// </summary>
    [Fact(DisplayName = "Missing Stats File: Falls Back To Recomputation")]
    public void MissingStatsFile_FallsBackToRecomputation()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("test document"));
            writer.Commit();
        }

        File.Delete(IndexStats.GetStatsPath(_dir, 1));

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Stats.TotalDocCount);
        Assert.Equal(1, searcher.Stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies the Corrupt Stats File: Falls Back To Recomputation scenario.
    /// </summary>
    [Fact(DisplayName = "Corrupt Stats File: Falls Back To Recomputation")]
    public void CorruptStatsFile_FallsBackToRecomputation()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDoc("test"));
            writer.Commit();
        }

        File.WriteAllText(IndexStats.GetStatsPath(_dir, 1), "NOT VALID JSON{{{");

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Stats.TotalDocCount);
    }

    /// <summary>
    /// Verifies the Stats File: Round-trip Preserves Field Data scenario.
    /// </summary>
    [Fact(DisplayName = "Stats File: Round-trip Preserves Field Data")]
    public void StatsFile_RoundTrip_PreservesFieldData()
    {
        var avgLengths = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["title"] = 5.2f,
            ["body"] = 142.7f,
        };
        var docCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["title"] = 9800,
            ["body"] = 9500,
        };
        var original = new IndexStats(10000, 9800, avgLengths, docCounts,
            new Dictionary<string, long>(StringComparer.Ordinal) { ["title"] = 50000, ["body"] = 1350000 });

        var path = Path.Combine(_dir, "test_stats.json");
        original.WriteTo(path);

        var loaded = IndexStats.TryLoadFrom(path);
        Assert.NotNull(loaded);
        Assert.Equal(10000, loaded.TotalDocCount);
        Assert.Equal(9800, loaded.LiveDocCount);
        Assert.Equal(5.2f, loaded.GetAvgFieldLength("title"));
        Assert.Equal(142.7f, loaded.GetAvgFieldLength("body"));
        Assert.Equal(9800, loaded.GetFieldDocCount("title"));
        Assert.Equal(9500, loaded.GetFieldDocCount("body"));
    }

    /// <summary>
    /// Verifies the Old Stats Files: Pruned By Deletion Policy scenario.
    /// </summary>
    [Fact(DisplayName = "Old Stats Files: Pruned By Deletion Policy")]
    public void OldStatsFiles_PrunedByDeletionPolicy()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(CreateDoc("first"));
        writer.Commit();

        writer.AddDocument(CreateDoc("second"));
        writer.Commit();

        Assert.False(File.Exists(IndexStats.GetStatsPath(_dir, 1)),
            "stats_1.json should be deleted by KeepLatestCommitPolicy");
        Assert.True(File.Exists(IndexStats.GetStatsPath(_dir, 2)),
            "stats_2.json should remain");
    }

    /// <summary>
    /// Verifies the Empty Index: Returns Empty Stats scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Index: Returns Empty Stats")]
    public void EmptyIndex_ReturnsEmptyStats()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.Commit();
        }

        var stats = IndexStats.TryLoadFrom(IndexStats.GetStatsPath(_dir, 1));
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalDocCount);
        Assert.Equal(0, stats.LiveDocCount);
    }

    private static LeanDocument CreateDoc(string bodyText)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", bodyText));
        return doc;
    }

    private static LeanDocument CreateSparseDoc(string? bodyText = null, string? titleText = null)
    {
        var doc = new LeanDocument();
        if (bodyText is not null)
            doc.Add(new TextField("body", bodyText));
        if (titleText is not null)
            doc.Add(new TextField("title", titleText));
        return doc;
    }

    private static void AssertSparseLiveStats(IndexStats stats)
    {
        Assert.Equal(4, stats.TotalDocCount);
        Assert.Equal(2, stats.LiveDocCount);
        Assert.Equal(1, stats.GetFieldDocCount("body"));
        Assert.Equal(1, stats.GetFieldLengthSum("body"));
        Assert.Equal(1.0f, stats.GetAvgFieldLength("body"));
        Assert.Equal(1, stats.GetFieldDocCount("title"));
        Assert.Equal(1, stats.GetFieldLengthSum("title"));
        Assert.Equal(1.0f, stats.GetAvgFieldLength("title"));
    }
}
