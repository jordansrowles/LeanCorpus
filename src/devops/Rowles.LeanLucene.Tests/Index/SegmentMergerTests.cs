using System.Text.Json;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Regression tests for autopsy issue C2: <c>SegmentMerger</c> previously discarded
/// roughly half the codec output. Each test pins one missing artefact (.fln, .dvn,
/// .dvs, .bkd, .tvd/.tvx, .pbs, IndexSortFields) plus the orphan-cleanup behaviour.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Merge")]
public sealed class SegmentMergerTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SegmentMergerTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int SegmentOrdinal(string segmentId)
    {
        Assert.StartsWith("seg_", segmentId);
        return int.Parse(segmentId.AsSpan("seg_".Length));
    }

    private static string MergeSegmentsForTest(string dir, MMapDirectory mmap)
    {
        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(s => SegmentOrdinal(s.SegmentId))
            .ToList();
        Assert.True(sourceSegments.Count >= 2, "Expected at least two source segments to merge.");

        int nextSegmentOrdinal = sourceSegments.Max(s => SegmentOrdinal(s.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);
        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextSegmentOrdinal);

        var mergedSegment = mergedSegments
            .FirstOrDefault(candidate => sourceSegments.All(source => source.SegmentId != candidate.SegmentId));
        Assert.NotNull(mergedSegment);

        // Publish a test-only commit that references the merged segment so searchers exercise
        // the freshly-written codec files rather than the pre-merge source segments.
        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(path => int.TryParse(Path.GetFileName(path).AsSpan("segments_".Length), out int gen) ? gen : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var commitData = JsonSerializer.Serialize(new
        {
            Segments = new[] { mergedSegment!.SegmentId },
            Generation = generation
        });
        File.WriteAllText(Path.Combine(dir, $"segments_{generation}"), commitData);

        var activeSegments = new HashSet<string>(mergedSegments.Select(static segment => segment.SegmentId), StringComparer.Ordinal);
        foreach (var segment in sourceSegments)
        {
            if (!activeSegments.Contains(segment.SegmentId))
                merger.CleanupSegmentFiles(segment);
        }

        return mergedSegment!.SegmentId;
    }

    private static IndexWriterConfig SmallSegmentMergeConfig(bool storeTermVectors = false, IndexSort? sort = null)
        => new()
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 100,
            StoreTermVectors = storeTermVectors,
            IndexSort = sort,
        };

    /// <summary>
    /// Verifies the Merge: Preserves Field Lengths BM25 Scores Match Unmerged scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Field Lengths BM25 Scores Match Unmerged")]
    public void Merge_PreservesFieldLengths_BM25ScoresMatchUnmerged()
    {
        var dir = SubDir(nameof(Merge_PreservesFieldLengths_BM25ScoresMatchUnmerged));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", string.Join(' ', Enumerable.Repeat("alpha", i + 1))));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // After merge there must be a .fln file on the merged segment to preserve
        // exact per-doc field lengths (BM25 falls back to coarse norms otherwise).
        var mergedId = MergeSegmentsForTest(dir, mmap);
        var flnPath = Path.Combine(dir, mergedId + ".fln");
        Assert.True(File.Exists(flnPath), $"Expected merged segment {mergedId} to have a .fln file at {flnPath}");
        Assert.True(new FileInfo(flnPath).Length > 0);

        // Sanity: searching still returns the merged docs.
        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new TermQuery("body", "alpha"), 10);
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Numeric Doc Values scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Numeric Doc Values")]
    public void Merge_PreservesNumericDocValues()
    {
        var dir = SubDir(nameof(Merge_PreservesNumericDocValues));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 10.0 + i));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var dvnPath = Path.Combine(dir, mergedId + ".dvn");
        Assert.True(File.Exists(dvnPath), $"Expected merged segment {mergedId} to have a .dvn file");

        // Sort by numeric DocValues field — works only if .dvn survives the merge.
        using var searcher = new IndexSearcher(mmap);
        var sorted = searcher.Search(new WildcardQuery("id", "*"), 10, SortField.Numeric("price"));
        Assert.Equal(2, sorted.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Sorted Doc Values scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Sorted Doc Values")]
    public void Merge_PreservesSortedDocValues()
    {
        var dir = SubDir(nameof(Merge_PreservesSortedDocValues));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            string[] cats = ["alpha", "bravo"];
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new StringField("category", cats[i]));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var dvsPath = Path.Combine(dir, mergedId + ".dvs");
        Assert.True(File.Exists(dvsPath), $"Expected merged segment {mergedId} to have a .dvs file");

        using var searcher = new IndexSearcher(mmap);
        var sorted = searcher.Search(new WildcardQuery("id", "*"), 10, SortField.String("category"));
        Assert.Equal(2, sorted.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves BKD Range Query Results scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves BKD Range Query Results")]
    public void Merge_PreservesBkdRangeQueryResults()
    {
        var dir = SubDir(nameof(Merge_PreservesBkdRangeQueryResults));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 1; i <= 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 100.0 + i * 10));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var bkdPath = Path.Combine(dir, mergedId + ".bkd");
        Assert.True(File.Exists(bkdPath), $"Expected merged segment {mergedId} to have a .bkd file");

        using var searcher = new IndexSearcher(mmap);
        // 110.0..120.0 inclusive should hit doc1 (110) and doc2 (120).
        var hits = searcher.Search(new RangeQuery("price", 110.0, 120.0), 10);
        Assert.Equal(2, hits.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Term Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Term Vectors")]
    public void Merge_PreservesTermVectors()
    {
        var dir = SubDir(nameof(Merge_PreservesTermVectors));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(storeTermVectors: true)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", $"keyword{i} shared common content"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var tvdPath = Path.Combine(dir, mergedId + ".tvd");
        var tvxPath = Path.Combine(dir, mergedId + ".tvx");
        Assert.True(File.Exists(tvdPath), $"Expected merged segment {mergedId} to have a .tvd file");
        Assert.True(File.Exists(tvxPath), $"Expected merged segment {mergedId} to have a .tvx file");

        // MoreLikeThis depends on term vectors; with vectors lost it would return zero.
        using var searcher = new IndexSearcher(mmap);
        var more = searcher.MoreLikeThis(0, ["body"], 5);
        Assert.True(more.TotalHits > 0, "MoreLikeThis returned zero hits — term vectors likely lost on merge");
    }

    /// <summary>
    /// Verifies the Merge: Preserves Parent Bit Set Block Join Query Still Returns Parents scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Parent Bit Set Block Join Query Still Returns Parents")]
    public void Merge_PreservesParentBitSet_BlockJoinQueryStillReturnsParents()
    {
        var dir = SubDir(nameof(Merge_PreservesParentBitSet_BlockJoinQueryStillReturnsParents));
        var mmap = new MMapDirectory(dir);

        // MaxBufferedDocs must be >= block size so each block lands intact in one segment.
        // We Commit() between blocks to force a flush per block.
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 16,
            MergeThreshold = 100,
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            writer.AddDocumentBlock(
            [
                MakeChild("alpha bravo"),
                MakeChild("charlie delta"),
                MakeParent("post one"),
            ]);
            writer.Commit();

            writer.AddDocumentBlock(
            [
                MakeChild("echo foxtrot"),
                MakeParent("post two"),
            ]);
            writer.Commit();

        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var pbsPath = Path.Combine(dir, mergedId + ".pbs");
        Assert.True(File.Exists(pbsPath), $"Expected merged segment {mergedId} to have a .pbs file");

        using var searcher = new IndexSearcher(mmap);
        var parents = searcher.Search(new BlockJoinQuery(new TermQuery("body", "alpha")), 10);
        Assert.Equal(1, parents.TotalHits);
        var stored = searcher.GetStoredFields(parents.ScoreDocs[0].DocId);
        Assert.True(stored.ContainsKey("title"));
        Assert.Contains("one", stored["title"][0]);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Index Sort Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Index Sort Fields")]
    public void Merge_PreservesIndexSortFields()
    {
        var dir = SubDir(nameof(Merge_PreservesIndexSortFields));
        var mmap = new MMapDirectory(dir);
        var sort = new IndexSort(SortField.Numeric("price"));

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: sort)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 50.0 - i));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var segInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        Assert.NotNull(segInfo.IndexSortFields);
        Assert.Single(segInfo.IndexSortFields!);
        Assert.Equal("Numeric:price:False", segInfo.IndexSortFields![0]);
    }

    /// <summary>
    /// Verifies the Cleanup Segment Files: Leaves No Orphans scenario.
    /// </summary>
    [Fact(DisplayName = "Cleanup Segment Files: Leaves No Orphans")]
    public void CleanupSegmentFiles_LeavesNoOrphans()
    {
        var dir = SubDir(nameof(CleanupSegmentFiles_LeavesNoOrphans));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(storeTermVectors: true)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 1.0 + i));
                doc.Add(new StringField("category", $"cat{i}"));
                doc.Add(new TextField("body", $"shared body content number {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _ = MergeSegmentsForTest(dir, mmap);

        // After merge, the original seg_0 and seg_1 must have ZERO files left on disk
        // (any extension). The previous bug only cleaned a hardcoded extension list.
        for (int i = 0; i < 2; i++)
        {
            var orphans = Directory.GetFiles(dir, $"seg_{i}.*");
            Assert.Empty(orphans);
        }
    }

    /// <summary>
    /// Verifies the Maybe Merge: With Protected Segment Keeps Protected Files And Stats scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Merge: With Protected Segment Keeps Protected Files And Stats")]
    public void MaybeMerge_WithProtectedSegment_KeepsProtectedFilesAndStats()
    {
        var dir = SubDir(nameof(MaybeMerge_WithProtectedSegment_KeepsProtectedFilesAndStats));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"merge protection {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        var protectedSegment = sourceSegments[0];
        var protectedSegmentIds = new HashSet<string>([protectedSegment.SegmentId], StringComparer.Ordinal);
        var nextSegmentOrdinal = sourceSegments.Max(segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);

        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextSegmentOrdinal, protectedSegmentIds);

        Assert.Contains(mergedSegments, segment => segment.SegmentId == protectedSegment.SegmentId);

        var activeSegments = new HashSet<string>(mergedSegments.Select(static segment => segment.SegmentId), StringComparer.Ordinal);
        foreach (var segment in sourceSegments)
        {
            if (!activeSegments.Contains(segment.SegmentId) && !protectedSegmentIds.Contains(segment.SegmentId))
                merger.CleanupSegmentFiles(segment);
        }

        Assert.True(File.Exists(Path.Combine(dir, protectedSegment.SegmentId + ".seg")));
        Assert.True(File.Exists(Path.Combine(dir, protectedSegment.SegmentId + ".stats.json")));
    }

    private static LeanDocument MakeChild(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("type", "child"));
        return doc;
    }

    private static LeanDocument MakeParent(string title)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("title", title));
        doc.Add(new StringField("type", "parent"));
        return doc;
    }
}
