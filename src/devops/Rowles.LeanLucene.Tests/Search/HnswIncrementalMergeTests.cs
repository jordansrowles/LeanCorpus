using System.Text.Json;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Phase 4 regression: an HNSW field merged across multiple segments should preserve
/// recall under top-K vector search. Validates the incremental-merge path in
/// <c>SegmentMerger</c> (seed graph from largest contributor, insert remainder).
/// </summary>
[Trait("Category", "Phase4")]
[Trait("Category", "Merge")]
public sealed class HnswIncrementalMergeTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HnswIncrementalMergeTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int SegmentOrdinal(string segmentId) => int.Parse(segmentId.AsSpan("seg_".Length));

    /// <summary>
    /// Verifies the Merge: Preserves HNSW Search Across Segments scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves HNSW Search Across Segments")]
    public void Merge_PreservesHnswSearchAcrossSegments()
    {
        var dir = SubDir("hnsw_merge");
        var mmap = new MMapDirectory(dir);

        var rnd = new Random(11);
        var allDocs = new List<float[]>();

        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswSeed = 7L,
            MaxBufferedDocs = 60,
        };

        using (var writer = new IndexWriter(mmap, cfg))
        {
            for (int i = 0; i < 180; i++)
            {
                var v = new float[16];
                double norm = 0;
                for (int d = 0; d < v.Length; d++)
                {
                    v[d] = (float)(rnd.NextDouble() * 2 - 1);
                    norm += v[d] * v[d];
                }
                norm = Math.Sqrt(norm);
                for (int d = 0; d < v.Length; d++) v[d] = (float)(v[d] / norm);
                allDocs.Add(v);
                var doc = new LeanDocument();
                doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>(v)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // Run the merger explicitly and publish a commit pointing at the merged segment.
        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(s => SegmentOrdinal(s.SegmentId))
            .ToList();
        Assert.True(sourceSegments.Count >= 2, "Test requires at least two source segments.");

        int next = sourceSegments.Max(s => SegmentOrdinal(s.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);
        var mergedSegments = merger.MaybeMerge(sourceSegments, ref next);

        var mergedSegment = mergedSegments
            .FirstOrDefault(c => sourceSegments.All(s => s.SegmentId != c.SegmentId));
        Assert.NotNull(mergedSegment);
        Assert.True(mergedSegment!.VectorFields.Any(vf => vf.FieldName == "embedding" && vf.HasHnsw),
            "Merged segment must retain HNSW for the embedding field.");

        // Publish a commit referencing the full active set: merged + any left-over segments.
        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(p => int.TryParse(Path.GetFileName(p).AsSpan("segments_".Length), out int g) ? g : 0)
            .DefaultIfEmpty(0).Max() + 1;
        File.WriteAllText(Path.Combine(dir, $"segments_{generation}"), Rowles.LeanLucene.Index.CommitFileFormat.Wrap(JsonSerializer.Serialize(new
        {
            Segments = mergedSegments.Select(s => s.SegmentId).ToArray(),
            Generation = generation,
        })));

        // Top-1 self-recall: each indexed vector should retrieve a near-perfect cosine match
        // (≈1.0) somewhere in the top-K. Validates the merged graph still finds the original.
        using var searcher = new IndexSearcher(mmap);
        int hits = 0;
        for (int i = 0; i < allDocs.Count; i++)
        {
            var top = searcher.Search(new VectorQuery("embedding", allDocs[i], topK: 5), 5);
            if (top.TotalHits > 0 && top.ScoreDocs[0].Score > 0.99f)
                hits++;
        }

        Assert.True(hits >= allDocs.Count * 0.95,
            $"Expected ≥95% top-K self-recall post-merge, got {hits}/{allDocs.Count}.");
    }
}
