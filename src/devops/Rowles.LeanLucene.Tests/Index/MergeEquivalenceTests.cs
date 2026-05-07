using System.Text.Json;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Verifies that every query feature produces identical results whether the index
/// consists of many small segments or those same segments merged into one.
/// Complements <see cref="MergeCorrectnessTests"/> which focuses on deletions
/// and <see cref="SegmentMergerTests"/> which focuses on codec file survival.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Merge")]
[Trait("Category", "Equivalence")]
public sealed class MergeEquivalenceTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public MergeEquivalenceTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    // ---- term query ----

    /// <summary>
    /// Verifies the Term Query: Unmerged Vs Merged Same Hit Count scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: Unmerged Vs Merged Same Hit Count")]
    public void TermQuery_UnmergedVsMerged_SameHitCount()
    {
        const int docCount = 24;
        int unmerged = CountViaQuery(
            BuildTextDocs(docCount),
            new TermQuery("body", "fox"),
            SubDir("tq-unmerged"), merged: false);

        int merged = CountViaQuery(
            BuildTextDocs(docCount),
            new TermQuery("body", "fox"),
            SubDir("tq-merged"), merged: true);

        Assert.Equal(unmerged, merged);
    }

    // ---- numeric range query ----

    /// <summary>
    /// Verifies the Numeric Range Query: Unmerged Vs Merged Same Hit Count scenario.
    /// </summary>
    [Fact(DisplayName = "Numeric Range Query: Unmerged Vs Merged Same Hit Count")]
    public void NumericRangeQuery_UnmergedVsMerged_SameHitCount()
    {
        const int docCount = 30;
        var query = new RangeQuery("price", 5, 14);

        int unmerged = CountViaQuery(BuildNumericDocs(docCount), query, SubDir("nrq-unmerged"), merged: false);
        int merged   = CountViaQuery(BuildNumericDocs(docCount), query, SubDir("nrq-merged"),   merged: true);

        Assert.Equal(unmerged, merged);
    }

    // ---- boolean query ----

    /// <summary>
    /// Verifies the Boolean And Query: Unmerged Vs Merged Same Hit Count scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean And Query: Unmerged Vs Merged Same Hit Count")]
    public void BooleanAndQuery_UnmergedVsMerged_SameHitCount()
    {
        const int docCount = 20;
        var query = new BooleanQuery.Builder()
            .Add(new TermQuery("tag", "alpha"), Occur.Must)
            .Add(new TermQuery("tag", "beta"),  Occur.Must)
            .Build();

        int unmerged = CountViaQuery(BuildTagDocs(docCount), query, SubDir("bq-unmerged"), merged: false);
        int merged   = CountViaQuery(BuildTagDocs(docCount), query, SubDir("bq-merged"),   merged: true);

        Assert.Equal(unmerged, merged);
    }

    // ---- facet correctness after merge ----

    /// <summary>
    /// Verifies the Facets: Unmerged Vs Merged Same Bucket Counts scenario.
    /// </summary>
    [Fact(DisplayName = "Facets: Unmerged Vs Merged Same Bucket Counts")]
    public void Facets_UnmergedVsMerged_SameBucketCounts()
    {
        const int docCount = 18;
        var docs = BuildCategoryDocs(docCount);

        var (_, bucketsUnmerged) = FacetsViaQuery(docs, "category", SubDir("fac-unmerged"), merged: false);
        var (_, bucketsMerged)   = FacetsViaQuery(docs, "category", SubDir("fac-merged"),   merged: true);

        var uSorted = bucketsUnmerged.OrderBy(b => b.Value).Select(b => $"{b.Value}:{b.Count}").ToArray();
        var mSorted = bucketsMerged .OrderBy(b => b.Value).Select(b => $"{b.Value}:{b.Count}").ToArray();

        Assert.Equal(uSorted, mSorted);
    }

    // ---- aggregation correctness after merge ----

    /// <summary>
    /// Verifies the Aggregations: Unmerged Vs Merged Same Sum And Count scenario.
    /// </summary>
    [Fact(DisplayName = "Aggregations: Unmerged Vs Merged Same Sum And Count")]
    public void Aggregations_UnmergedVsMerged_SameSumAndCount()
    {
        const int docCount = 20;
        var docs = BuildNumericBodyDocs(docCount);

        var (sumU, cntU) = AggregationViaQuery(docs, SubDir("agg-unmerged"), merged: false);
        var (sumM, cntM) = AggregationViaQuery(docs, SubDir("agg-merged"),   merged: true);

        Assert.Equal(sumU, sumM);
        Assert.Equal(cntU, cntM);
    }

    // ---- collapse after merge ----

    /// <summary>
    /// Verifies the Collapse: Unmerged Vs Merged Same Group Count scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Unmerged Vs Merged Same Group Count")]
    public void Collapse_UnmergedVsMerged_SameGroupCount()
    {
        const int docCount = 15;
        var docs = BuildCategoryDocs(docCount);

        int unmerged = CollapseViaQuery(docs, "category", SubDir("col-unmerged"), merged: false);
        int merged   = CollapseViaQuery(docs, "category", SubDir("col-merged"),   merged: true);

        Assert.Equal(unmerged, merged);
    }

    // ---- deletions survive merge ----

    /// <summary>
    /// Verifies the Deleted Documents: Are Still Excluded After Merge scenario.
    /// </summary>
    [Fact(DisplayName = "Deleted Documents: Are Still Excluded After Merge")]
    public void DeletedDocuments_AreStillExcludedAfterMerge()
    {
        var dir = SubDir("del-merge");
        var mmap = new MMapDirectory(dir);

        // Write 6 single-doc segments and delete half.
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 1, MergeThreshold = int.MaxValue }))
        {
            for (int i = 0; i < 6; i++)
            {
                var d = new LeanDocument();
                d.Add(new TextField("body", i < 3 ? "keeper" : "victim"));
                writer.AddDocument(d);
                writer.Commit();
            }
            writer.DeleteDocuments(new TermQuery("body", "victim"));
            writer.Commit();
        }

        // Merge deterministically.
        var segFiles = Directory.GetFiles(dir, "seg_*.seg");
        var sourceSegments = segFiles
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(s => int.Parse(s.SegmentId.AsSpan("seg_".Length)))
            .ToList();

        int nextOrdinal = sourceSegments.Max(s => int.Parse(s.SegmentId.AsSpan("seg_".Length))) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: sourceSegments.Count);
        var result = merger.MaybeMerge(sourceSegments, ref nextOrdinal);

        var mergedSeg = result.First(
            candidate => sourceSegments.All(src => src.SegmentId != candidate.SegmentId));

        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(f => int.TryParse(Path.GetFileName(f).AsSpan("segments_".Length), out int g) ? g : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        File.WriteAllText(
            Path.Combine(dir, $"segments_{generation}"),
            JsonSerializer.Serialize(new { Segments = new[] { mergedSeg.SegmentId }, Generation = generation }));

        var activeIds = new HashSet<string>(result.Select(s => s.SegmentId), StringComparer.Ordinal);
        foreach (var seg in sourceSegments.Where(s => !activeIds.Contains(s.SegmentId)))
            merger.CleanupSegmentFiles(seg);

        using var searcher = new IndexSearcher(new MMapDirectory(dir));
        Assert.Equal(3, searcher.Search(new TermQuery("body", "keeper"), 20).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "victim"), 20).TotalHits);
    }

    // ---- helpers ----

    private static int CountViaQuery(List<LeanDocument> docs, Query query, string path, bool merged)
    {
        IndexDocuments(docs, path, merged);
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        return searcher.Search(query, 200).TotalHits;
    }

    private static (double sum, long count) AggregationViaQuery(List<LeanDocument> docs, string path, bool merged)
    {
        IndexDocuments(docs, path, merged);
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var (_, aggs) = searcher.SearchWithAggregations(
            new TermQuery("body", "item"), 200,
            new AggregationRequest("price_sum", "price"));
        var agg = aggs[0];
        return (agg.Sum, agg.Count);
    }

    private static (int total, IReadOnlyList<FacetBucket> buckets) FacetsViaQuery(
        List<LeanDocument> docs, string field, string path, bool merged)
    {
        IndexDocuments(docs, path, merged);
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var (results, facets) = searcher.SearchWithFacets(new TermQuery("body", "item"), 200, field);
        return (results.TotalHits, facets.FirstOrDefault()?.Buckets ?? []);
    }

    private static int CollapseViaQuery(List<LeanDocument> docs, string field, string path, bool merged)
    {
        IndexDocuments(docs, path, merged);
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        return searcher.SearchWithCollapse(new TermQuery("body", "item"), 200, new CollapseField(field)).TotalHits;
    }

    /// <summary>
    /// Indexes <paramref name="docs"/> into <paramref name="path"/>. When
    /// <paramref name="merged"/> is <see langword="true"/>, each document is
    /// flushed as a separate segment and then <see cref="SegmentMerger"/> is
    /// called directly so the merge is synchronous and deterministic.
    /// </summary>
    private static void IndexDocuments(List<LeanDocument> docs, string path, bool merged)
    {
        var mmap = new MMapDirectory(path);

        if (!merged)
        {
            using var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 500 });
            foreach (var doc in docs)
                writer.AddDocument(doc);
            writer.Commit();
            return;
        }

        // Write each document into its own segment.
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 1, MergeThreshold = int.MaxValue }))
        {
            foreach (var doc in docs)
            {
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        // Merge all segments synchronously via SegmentMerger.
        var segFiles = Directory.GetFiles(path, "seg_*.seg");
        if (segFiles.Length < 2) return;

        var sourceSegments = segFiles
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(s => int.Parse(s.SegmentId.AsSpan("seg_".Length)))
            .ToList();

        int nextOrdinal = sourceSegments.Max(s => int.Parse(s.SegmentId.AsSpan("seg_".Length))) + 1;
        // Use the segment count as the threshold so all segments are merged into one in a single pass.
        var merger = new SegmentMerger(mmap, mergeThreshold: sourceSegments.Count);
        var result = merger.MaybeMerge(sourceSegments, ref nextOrdinal);

        var mergedSeg = result.FirstOrDefault(
            candidate => sourceSegments.All(src => src.SegmentId != candidate.SegmentId));
        if (mergedSeg is null) return;

        int generation = Directory.GetFiles(path, "segments_*")
            .Select(f => int.TryParse(Path.GetFileName(f).AsSpan("segments_".Length), out int g) ? g : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        File.WriteAllText(
            Path.Combine(path, $"segments_{generation}"),
            JsonSerializer.Serialize(new { Segments = new[] { mergedSeg.SegmentId }, Generation = generation }));

        var activeIds = new HashSet<string>(result.Select(s => s.SegmentId), StringComparer.Ordinal);
        foreach (var seg in sourceSegments.Where(s => !activeIds.Contains(s.SegmentId)))
            merger.CleanupSegmentFiles(seg);
    }

    // ---- document builders ----

    private static List<LeanDocument> BuildTextDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", i % 3 == 0 ? "the quick brown fox" : "lazy dog content"));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildNumericDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new NumericField("price", i));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildNumericBodyDocs(int count)
    {
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", "item"));
            d.Add(new NumericField("price", i));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildTagDocs(int count)
    {
        var cats = new[] { "alpha", "beta", "alpha beta", "gamma" };
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("tag", cats[i % cats.Length]));
            docs.Add(d);
        }
        return docs;
    }

    private static List<LeanDocument> BuildCategoryDocs(int count)
    {
        var cats = new[] { "alpha", "beta", "gamma" };
        var docs = new List<LeanDocument>(count);
        for (int i = 0; i < count; i++)
        {
            var d = new LeanDocument();
            d.Add(new TextField("body", "item"));
            d.Add(new StringField("category", cats[i % cats.Length]));
            d.Add(new NumericField("price", i));
            docs.Add(d);
        }
        return docs;
    }
}
