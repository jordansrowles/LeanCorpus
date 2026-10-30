using System.Text.Json;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Phase 4 regression: an HNSW field merged across multiple segments should preserve
/// recall under top-K vector search. Validates the incremental-merge path in
/// <c>SegmentMerger</c> (seed graph from largest contributor, insert remainder).
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class HnswIncrementalMergeTests : IClassFixture<TestDirectoryFixture>
{
    private const int LegacyGraphDimension = 8;
    private const int LegacyGraphPersistedNodeCount = 96;
    private const int LegacyGraphVectorCount = 192;
    private const int LegacyGraphEntryPoint = 48;
    private const string LegacyGraphResourceName =
        "Rowles.LeanCorpus.Tests.Core.Fixtures.Hnsw.hnsw-v1-legacy-missing-entrypoint.hnsw";

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
        Assert.Contains(mergedSegment!.VectorFields, vf => vf.FieldName == "embedding" && vf.HasHnsw);

        // Publish a commit referencing the full active set: merged + any left-over segments.
        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(p => int.TryParse(Path.GetFileName(p).AsSpan("segments_".Length), out int g) ? g : 0)
            .DefaultIfEmpty(0).Max() + 1;
        File.WriteAllText(Path.Combine(dir, $"segments_{generation}"), Rowles.LeanCorpus.Index.CommitFileFormat.Wrap(JsonSerializer.Serialize(new
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
            var top = searcher.Search(new VectorQuery("embedding", allDocs[i], topK: 5), 5, TestContext.Current.CancellationToken);
            if (top.TotalHits > 0 && top.ScoreDocs[0].Score > 0.99f)
                hits++;
        }

        Assert.True(hits >= allDocs.Count * 0.95,
            $"Expected ≥95% top-K self-recall post-merge, got {hits}/{allDocs.Count}.");
    }

    [Fact(DisplayName = "Merge: Remaps legacy HNSW v1 with a removed upper-layer entry point")]
    public void Merge_RemapsLegacyHnswV1AndRepairsTheTopEntryPoint()
    {
        var dir = SubDir(nameof(Merge_RemapsLegacyHnswV1AndRepairsTheTopEntryPoint));
        using var mmap = new MMapDirectory(dir);
        var vectors = BuildLegacyGraphVectors();
        var hnswConfig = new HnswBuildConfig { M = 4, EfConstruction = 12 };

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = LegacyGraphPersistedNodeCount,
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswSeed = 19L,
            HnswBuildConfig = hnswConfig,
        }))
        {
            for (int docId = 0; docId < LegacyGraphPersistedNodeCount; docId++)
                AddVectorDocument(writer, docId, vectors[docId]);
            writer.Commit();

            for (int docId = LegacyGraphPersistedNodeCount; docId < LegacyGraphPersistedNodeCount + 2; docId++)
                AddVectorDocument(writer, docId, vectors[docId]);
            writer.DeleteDocuments(new TermQuery("id", LegacyGraphEntryPoint.ToString()));
            writer.Commit();
        }

        var recovery = Assert.IsType<IndexRecovery.RecoveryResult>(
            IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false));
        var sourceSegments = recovery.SegmentInfos
            .OrderBy(segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        Assert.Equal(2, sourceSegments.Count);
        using (var searcher = new IndexSearcher(mmap))
            Assert.Equal(0, searcher.Search(new TermQuery("id", LegacyGraphEntryPoint.ToString()), 10,
                TestContext.Current.CancellationToken).TotalHits);

        var seedSegment = Assert.Single(sourceSegments, segment => segment.DocCount == LegacyGraphPersistedNodeCount);
        Assert.Equal(LegacyGraphPersistedNodeCount - 1, seedSegment.LiveDocCount);
        string legacyGraphPath = Path.Combine(dir, VectorFilePaths.HnswFile(seedSegment.SegmentId, "embedding"));
        CopyLegacyGraphFixture(legacyGraphPath);

        int nextOrdinal = sourceSegments.Max(segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2, hnswBuildConfig: hnswConfig);
        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextOrdinal);
        var mergedSegment = Assert.IsType<SegmentInfo>(mergedSegments
            .FirstOrDefault(candidate => sourceSegments.All(source => source.SegmentId != candidate.SegmentId)));

        using var reader = new SegmentReader(mmap, mergedSegment);
        var graph = Assert.IsType<HnswGraph>(reader.GetHnswGraph("embedding"));
        Assert.Equal(LegacyGraphPersistedNodeCount + 1, graph.NodeCount);
        Assert.Contains(graph.EntryPoint, graph.GetNodesAtLevel(graph.MaxLevel));
        AssertValidGraphMembership(graph);

        var results = graph.Search(vectors[0], new HnswTraversalOptions { Ef = graph.NodeCount, TopK = 10 });
        Assert.Equal(10, results.Count);
        Assert.Equal(10, results.Select(static result => result.DocId).Distinct().Count());
    }

    private static void AddVectorDocument(IndexWriter writer, int docId, float[] vector)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", docId.ToString()));
        document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
        writer.AddDocument(document);
    }

    private static Dictionary<int, float[]> BuildLegacyGraphVectors()
    {
        var random = new Random(1234);
        return Enumerable.Range(0, LegacyGraphVectorCount)
            .ToDictionary(static docId => docId, _ => SimdVectorOps.Normalise(RandomVector(random)));
    }

    private static float[] RandomVector(Random random)
    {
        var vector = new float[LegacyGraphDimension];
        for (int dimension = 0; dimension < LegacyGraphDimension; dimension++)
            vector[dimension] = (float)(random.NextDouble() * 2d - 1d);
        return vector;
    }

    private static void CopyLegacyGraphFixture(string destinationPath)
    {
        using var fixture = typeof(HnswIncrementalMergeTests).Assembly.GetManifestResourceStream(LegacyGraphResourceName)
            ?? throw new InvalidOperationException($"Embedded legacy fixture '{LegacyGraphResourceName}' was not found.");
        using var destination = File.Create(destinationPath);
        fixture.CopyTo(destination);
    }

    private static void AssertValidGraphMembership(HnswGraph graph)
    {
        var baseMembers = graph.GetNodesAtLevel(0).ToHashSet();
        Assert.Equal(graph.NodeCount, baseMembers.Count);

        for (int level = 0; level < graph.LevelCount; level++)
        {
            var members = graph.GetNodesAtLevel(level).ToHashSet();
            Assert.Subset(baseMembers, members);
            foreach (int docId in members)
                foreach (int neighbour in graph.GetNeighbours(docId, level))
                    Assert.Contains(neighbour, members);
        }
    }
}
