using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Contains unit tests for HNSW Graph.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class HnswGraphTests
{
    /// <summary>
    /// Verifies the Empty Graph: Search Returns No Results scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Graph: Search Returns No Results")]
    public void EmptyGraph_SearchReturnsNoResults()
    {
        var source = MakeSource(new Dictionary<int, float[]>(), dimension: 4);
        var graph = new HnswGraph(source, new HnswBuildConfig { M = 4, EfConstruction = 10 }, seed: 1);
        graph.Freeze();

        var results = graph.Search(new float[] { 1, 0, 0, 0 }, new HnswTraversalOptions { Ef = 10, TopK = 5 });

        Assert.Empty(results);
    }

    /// <summary>
    /// Verifies the Single Node: Becomes Entry Point scenario.
    /// </summary>
    [Fact(DisplayName = "Single Node: Becomes Entry Point")]
    public void SingleNode_BecomesEntryPoint()
    {
        var source = MakeSource(new Dictionary<int, float[]> { [42] = Normalise([1, 2, 3, 4]) }, dimension: 4);
        var graph = new HnswGraph(source, new HnswBuildConfig { M = 4, EfConstruction = 10 }, seed: 1);

        graph.Insert(42);
        graph.Freeze();

        Assert.Equal(1, graph.NodeCount);
        Assert.Equal(42, graph.EntryPoint);
        Assert.True(graph.MaxLevel >= 0);
    }

    /// <summary>
    /// Verifies the Search: Finds Exact Nearest For Orthogonal Set scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Finds Exact Nearest For Orthogonal Set")]
    public void Search_FindsExactNearestForOrthogonalSet()
    {
        var vectors = new Dictionary<int, float[]>
        {
            [0] = Normalise([1, 0, 0, 0]),
            [1] = Normalise([0, 1, 0, 0]),
            [2] = Normalise([0, 0, 1, 0]),
            [3] = Normalise([0, 0, 0, 1]),
        };
        var source = MakeSource(vectors, dimension: 4);
        var graph = HnswGraphBuilder.Build(source, [0, 1, 2, 3], new HnswBuildConfig { M = 4, EfConstruction = 16 }, seed: 7);

        var results = graph.Search(Normalise([0.9f, 0.1f, 0, 0]), new HnswTraversalOptions { Ef = 16, TopK = 1 });

        Assert.Single(results);
        Assert.Equal(0, results[0].DocId);
    }

    /// <summary>
    /// Verifies the Search: Recall Exceeds Threshold On Random Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Recall Exceeds Threshold On Random Vectors")]
    public void Search_RecallExceedsThresholdOnRandomVectors()
    {
        const int n = 500;
        const int dim = 64;
        const int topK = 10;
        var rng = new Random(123);
        var vectors = new Dictionary<int, float[]>(n);
        for (int i = 0; i < n; i++)
            vectors[i] = Normalise(RandomVector(rng, dim));

        var source = MakeSource(vectors, dim);
        var graph = HnswGraphBuilder.Build(source, [.. Enumerable.Range(0, n)],
            new HnswBuildConfig { M = 16, EfConstruction = 100 }, seed: 99);

        int matches = 0;
        const int trials = 25;
        for (int t = 0; t < trials; t++)
        {
            var query = Normalise(RandomVector(rng, dim));
            var bruteTop = BruteForceTopK(vectors, query, topK);
            var hnswTop = graph.Search(query, new HnswTraversalOptions { Ef = 50, TopK = topK })
                .Select(r => r.DocId).ToHashSet();
            matches += bruteTop.Count(id => hnswTop.Contains(id));
        }

        double recall = matches / (double)(trials * topK);
        Assert.True(recall >= 0.90, $"Recall@{topK} was {recall:F3}, expected >= 0.90");
    }

    /// <summary>
    /// Verifies the Build: With Same Seed Produces Identical Graphs scenario.
    /// </summary>
    [Fact(DisplayName = "Build: With Same Seed Produces Identical Graphs")]
    public void Build_WithSameSeed_ProducesIdenticalGraphs()
    {
        const int n = 100;
        const int dim = 16;
        var rng = new Random(555);
        var vectors = new Dictionary<int, float[]>(n);
        for (int i = 0; i < n; i++)
            vectors[i] = Normalise(RandomVector(rng, dim));

        var source1 = MakeSource(vectors, dim);
        var source2 = MakeSource(vectors, dim);
        var config = new HnswBuildConfig { M = 8, EfConstruction = 50 };

        var graphA = HnswGraphBuilder.Build(source1, [.. Enumerable.Range(0, n)], config, seed: 42);
        var graphB = HnswGraphBuilder.Build(source2, [.. Enumerable.Range(0, n)], config, seed: 42);

        Assert.Equal(graphA.NodeCount, graphB.NodeCount);
        Assert.Equal(graphA.EntryPoint, graphB.EntryPoint);
        Assert.Equal(graphA.MaxLevel, graphB.MaxLevel);
        for (int level = 0; level <= graphA.MaxLevel; level++)
        {
            var nodesA = graphA.GetNodesAtLevel(level).OrderBy(x => x).ToArray();
            var nodesB = graphB.GetNodesAtLevel(level).OrderBy(x => x).ToArray();
            Assert.Equal(nodesA, nodesB);
            foreach (var docId in nodesA)
                Assert.Equal(graphA.GetNeighbours(docId, level).ToArray().OrderBy(x => x),
                             graphB.GetNeighbours(docId, level).ToArray().OrderBy(x => x));
        }
    }

    /// <summary>
    /// Verifies the Insert: After Freeze Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Insert: After Freeze Throws")]
    public void Insert_AfterFreeze_Throws()
    {
        var source = MakeSource(new Dictionary<int, float[]> { [0] = Normalise([1, 0, 0, 0]) }, 4);
        var graph = new HnswGraph(source, new HnswBuildConfig { M = 4, EfConstruction = 10 }, seed: 1);
        graph.Insert(0);
        graph.Freeze();

        Assert.Throws<InvalidOperationException>(() => graph.Insert(0));
    }

    /// <summary>
    /// Verifies the Graph Invariant: All Nodes Reachable From Entry Point scenario.
    /// </summary>
    [Fact(DisplayName = "Graph Invariant: All Nodes Reachable From Entry Point")]
    public void GraphInvariant_AllNodesReachableFromEntryPoint()
    {
        const int n = 50;
        const int dim = 8;
        var rng = new Random(321);
        var vectors = new Dictionary<int, float[]>(n);
        for (int i = 0; i < n; i++)
            vectors[i] = Normalise(RandomVector(rng, dim));

        var source = MakeSource(vectors, dim);
        var graph = HnswGraphBuilder.Build(source, [.. Enumerable.Range(0, n)],
            new HnswBuildConfig { M = 8, EfConstruction = 40 }, seed: 11);

        var visited = new HashSet<int> { graph.EntryPoint };
        var queue = new Queue<int>();
        queue.Enqueue(graph.EntryPoint);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            for (int level = 0; level <= graph.MaxLevel; level++)
                foreach (var n2 in graph.GetNeighbours(current, level))
                    if (visited.Add(n2)) queue.Enqueue(n2);
        }

        Assert.Equal(n, visited.Count);
    }

    [Fact(DisplayName = "HNSW new maximum-level entry point is present before and after persistence")]
    public void NewMaximumLevelEntryPoint_IsPresentAcrossLayersAndPersistence()
    {
        const int initialCount = 96;
        const int totalCount = 192;
        const int dimension = 8;
        var random = new Random(1234);
        var vectors = Enumerable.Range(0, totalCount)
            .ToDictionary(static id => id, _ => Normalise(RandomVector(random, dimension)));
        var source = MakeSource(vectors, dimension);
        var config = new HnswBuildConfig { M = 4, EfConstruction = 12 };
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hnsw-entry-" + Guid.NewGuid().ToString("N") + ".hnsw");

        using var graph = new HnswGraph(source, config, seed: 19);
        var observedPromotion = false;
        for (int docId = 0; docId < initialCount; docId++)
        {
            int previousMaxLevel = graph.MaxLevel;
            graph.Insert(docId);
            observedPromotion |= graph.MaxLevel > previousMaxLevel;
            AssertEntryPointPresentAtEveryLayer(graph);
        }

        Assert.True(observedPromotion, "The deterministic fixture must promote the entry point to a new maximum level.");
        graph.Freeze();
        HnswWriter.Write(path, graph, dimension, normalised: true);

        try
        {
            using var reopened = HnswReader.Read(path, source, expectedNormalised: true);
            AssertEntryPointPresentAtEveryLayer(reopened);

            reopened.Thaw();
            for (int docId = initialCount; docId < totalCount; docId++)
            {
                reopened.Insert(docId);
                AssertEntryPointPresentAtEveryLayer(reopened);
            }
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact(DisplayName = "HNSW allow-list search traverses an excluded bridge node")]
    public void AllowListSearch_TraversesExcludedBridgeToEligibleNearestNeighbour()
    {
        var vectors = new Dictionary<int, float[]>
        {
            [0] = Normalise([0.5f, 0.8660254f, 0, 0]),
            [1] = Normalise([0.8f, 0.6f, 0, 0]),
            [2] = Normalise([1, 0, 0, 0]),
        };
        var source = MakeSource(vectors, dimension: 4);
        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [2], []]);
        using var graph = HnswGraph.FromFrozen(source,
            new HnswBuildConfig { M = 4, EfConstruction = 10 }, seed: 1, [level], entryPoint: 0,
            maxLevel: 0, nodeCount: 3);
        var allowedDocuments = new RoaringBitmap();
        allowedDocuments.Add(0);
        allowedDocuments.Add(2);

        var results = graph.Search(Normalise([1, 0, 0, 0]), new HnswTraversalOptions
        {
            Ef = 1,
            TopK = 1,
            AllowList = new RoaringBitmapBitSet(allowedDocuments),
        });

        Assert.Equal(2, Assert.Single(results).DocId);
    }

    [Fact(DisplayName = "HNSW search explores a boundary-tied bridge to a closer BBQ node")]
    public void SearchLayer_ExploresBoundaryTieToCloserBBQNode()
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, -1f },
            [1] = new float[] { 1f, -1f },
            [2] = new float[] { 1f, 1f },
        };
        var source = new BBQMemoryVectorSource(vectors, 2, new float[2]);
        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [2], []]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 3);

        var results = graph.Search([1f, 1f], new HnswTraversalOptions { Ef = 1, TopK = 1 });

        Assert.Equal(2, Assert.Single(results).DocId);
    }

    [Fact(DisplayName = "HNSW allow-list search crosses a boundary-tied excluded BBQ bridge")]
    public void AllowListSearch_ExploresBoundaryTieToEligibleCloserBBQNode()
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, -1f },
            [1] = new float[] { 1f, -1f },
            [2] = new float[] { 1f, 1f },
        };
        var source = new BBQMemoryVectorSource(vectors, 2, new float[2]);
        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [2], []]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 3);
        var allowedDocuments = new RoaringBitmap();
        allowedDocuments.Add(0);
        allowedDocuments.Add(2);

        var results = graph.Search([1f, 1f], new HnswTraversalOptions
        {
            Ef = 1,
            TopK = 1,
            AllowList = new RoaringBitmapBitSet(allowedDocuments),
        });

        Assert.Equal(2, Assert.Single(results).DocId);
    }

    [Fact(DisplayName = "HNSW greedy descent crosses a tie plateau to a closer upper-layer node")]
    public void GreedyDescent_ExploresBoundaryTieToCloserNodeAtUpperLayer()
    {
        var vectors = new Dictionary<int, float[]>
        {
            [0] = Normalise([0f, 1f]),
            [1] = Normalise([0f, 1f]),
            [2] = Normalise([1f, 0f]),
            [3] = Normalise([-1f, 0f]),
        };
        var source = MakeSource(vectors, dimension: 2);
        var baseLevel = new HnswGraph.FrozenLevel([0, 1, 2, 3], [[1, 3], [0], [3], [0, 2]]);
        var upperLevel = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [0, 2], [1]]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [baseLevel, upperLevel],
            entryPoint: 0,
            maxLevel: 1,
            nodeCount: 4);

        var results = graph.Search([1f, 0f], new HnswTraversalOptions { Ef = 1, TopK = 1 });

        Assert.Equal(2, Assert.Single(results).DocId);
    }

    [Fact(DisplayName = "HNSW search restarts from the persisted entry point when greedy descent underfills")]
    public void Search_RestartsFromEntryPoint_WhenGreedyStartCannotFillTopK()
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { -1f, -1f },
            [1] = new float[] { 1f, 1f },
            [2] = new float[] { 1f, 1f },
            [3] = new float[] { 1f, 1f },
        };
        var source = new BBQMemoryVectorSource(vectors, 2, new float[2]);
        var baseLevel = new HnswGraph.FrozenLevel([0, 1, 2, 3], [[2, 3], [2], [], []]);
        var upperLevel = new HnswGraph.FrozenLevel([0, 1], [[1], []]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [baseLevel, upperLevel],
            entryPoint: 0,
            maxLevel: 1,
            nodeCount: 4);

        var results = graph.Search(
            [1f, 1f],
            new HnswTraversalOptions { Ef = 4, TopK = 3 },
            out HnswSearchStats stats);

        Assert.Equal(3, results.Count);
        Assert.Contains(3, results.Select(static result => result.DocId));
        Assert.Equal(5, stats.NodesVisited);
        Assert.Equal(0, stats.ScannedVectors);
        Assert.False(stats.UsedExhaustiveFallback);
    }

    [Fact(DisplayName = "HNSW fills TopK with the graph metric when directed reachability underfills")]
    public void Search_FillsTopKByMetricScan_WhenDirectedComponentUnderfills()
    {
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, 1f },
            [1] = new float[] { 1f, -1f },
            [2] = new float[] { -1f, 1f },
            [3] = new float[] { -1f, -1f },
        };
        var source = new BBQMemoryVectorSource(vectors, 2, new float[2]);
        var level = new HnswGraph.FrozenLevel([0, 1, 2, 3], [[1], [], [], []]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 4);

        var results = graph.Search(
            [1f, 1f],
            new HnswTraversalOptions { Ef = 4, TopK = 3 },
            out HnswSearchStats stats);

        Assert.Equal(new[] { 0, 1, 2 }, results.Select(static result => result.DocId));
        Assert.Equal(2, stats.NodesVisited);
        Assert.Equal(4, stats.ScannedVectors);
        Assert.True(stats.UsedExhaustiveFallback);
        Assert.Equal(HnswExhaustiveFallbackReason.UnderfilledAfterTraversal, stats.FallbackReason);
    }

    [Fact(DisplayName = "HNSW complete graph result does not use exhaustive fallback")]
    public void Search_CompleteGraphDoesNotUseExhaustiveFallback()
    {
        var vectors = new Dictionary<int, float[]>
        {
            [0] = [1f, 0f],
            [1] = [0f, 1f],
            [2] = [-1f, 0f],
        };
        var source = MakeSource(vectors, dimension: 2);
        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1, 2], [0, 2], [0, 1]]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 3);

        var results = graph.Search(
            [1f, 0f],
            new HnswTraversalOptions { Ef = 3, TopK = 2 },
            out HnswSearchStats stats);

        Assert.Equal(new[] { 0, 1 }, results.Select(static result => result.DocId));
        Assert.Equal(3, stats.NodesVisited);
        Assert.Equal(0, stats.ScannedVectors);
        Assert.False(stats.UsedExhaustiveFallback);
        Assert.Equal(HnswExhaustiveFallbackReason.None, stats.FallbackReason);
    }

    [Fact(DisplayName = "HNSW: Maximal efSearch is bounded during post-filter retries")]
    public void Search_MaximalEfSearch_PostFilterVisitsEachNodeOnce()
    {
        var vectors = new Dictionary<int, float[]>
        {
            [0] = Normalise([1f, 0f]),
            [1] = Normalise([0f, 1f]),
            [2] = Normalise([-1f, 0f]),
        };
        var source = MakeSource(vectors, dimension: 2);
        var level = new HnswGraph.FrozenLevel([0, 1, 2], [[1], [2], []]);
        using var graph = HnswGraph.FromFrozen(
            source,
            new HnswBuildConfig { M = 2, EfConstruction = 4 },
            seed: 1,
            [level],
            entryPoint: 0,
            maxLevel: 0,
            nodeCount: 3);
        var onlyLastDocument = new RoaringBitmap();
        onlyLastDocument.Add(2);

        var results = graph.Search(
            [1f, 0f],
            new HnswTraversalOptions
            {
                Ef = int.MaxValue,
                TopK = 2,
                MaxPostFilterRetries = 3,
                PostFilterMask = new RoaringBitmapBitSet(onlyLastDocument),
            },
            out HnswSearchStats stats);

        Assert.Equal(2, Assert.Single(results).DocId);
        Assert.Equal(3, stats.NodesVisited);
    }

    private static void AssertEntryPointPresentAtEveryLayer(HnswGraph graph)
    {
        for (int level = 0; level <= graph.MaxLevel; level++)
            Assert.Contains(graph.EntryPoint, graph.GetNodesAtLevel(level));
    }

    private static InMemoryVectorSource MakeSource(Dictionary<int, float[]> vectors, int dimension)
    {
        var dict = new Dictionary<int, ReadOnlyMemory<float>>(vectors.Count);
        foreach (var (id, vec) in vectors)
            dict[id] = vec.AsMemory();
        return new InMemoryVectorSource(dict, dimension);
    }

    private static float[] RandomVector(Random rng, int dim)
    {
        var v = new float[dim];
        for (int i = 0; i < dim; i++) v[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        return v;
    }

    private static float[] Normalise(float[] v) => SimdVectorOps.Normalise(v);

    private static List<int> BruteForceTopK(Dictionary<int, float[]> vectors, float[] query, int k)
    {
        return vectors
            .Select(kv => (kv.Key, Score: SimdVectorOps.DotProduct(query, kv.Value)))
            .OrderByDescending(x => x.Score)
            .Take(k)
            .Select(x => x.Key)
            .ToList();
    }
}

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BBQDistanceComputerTests
{
    [Fact(DisplayName = "BBQ query distance reads packed bytes in persisted little-endian bit order")]
    public void QueryDistance_UsesPersistedBitOrderForFullWord()
    {
        const int dimension = 64;
        var query = new float[dimension];
        var centroid = new float[dimension];
        var storedBits = new byte[dimension / 8];
        for (int byteIndex = 0; byteIndex < storedBits.Length; byteIndex++)
        {
            byte pattern = (byte)(1 << byteIndex);
            storedBits[byteIndex] = pattern;
            for (int bitIndex = 0; bitIndex < 8; bitIndex++)
                query[(byteIndex * 8) + bitIndex] = (pattern & (1 << bitIndex)) != 0 ? 1f : -1f;
        }

        Assert.Equal(-dimension, BBQDistanceComputer.Distance(query, centroid, storedBits, dimension));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void QueryDistance_MatchesScalarReferenceAtChunkBoundaries(int dim)
    {
        var query = new float[dim];
        var centroid = new float[dim];
        var bits = Enumerable.Range(0, (dim + 7) / 8)
            .Select(static index => (byte)(0xA5 ^ index))
            .ToArray();
        for (int index = 0; index < dim; index++)
        {
            query[index] = ((index % 5) - 2) * 0.25f;
            centroid[index] = ((index % 3) - 1) * 0.125f;
        }

        int matching = 0;
        for (int index = 0; index < dim; index++)
        {
            bool queryBit = query[index] - centroid[index] > 0f;
            bool storedBit = ((bits[index / 8] >> (index % 8)) & 1) != 0;
            if (queryBit == storedBit)
                matching++;
        }

        float expected = -(2f * matching - dim);
        Assert.Equal(expected, BBQDistanceComputer.Distance(query, centroid, bits, dim));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void QueryAndStoredDistance_AgreeForEncodedQuery(int dim)
    {
        var centroid = new float[dim];
        var query = new float[dim];
        var other = new float[dim];
        for (int index = 0; index < dim; index++)
        {
            centroid[index] = ((index % 3) - 1) * 0.125f;
            query[index] = centroid[index] + ((index % 5) - 2) * 0.25f;
            other[index] = centroid[index] + ((index % 7) - 3) * 0.125f;
        }

        var vectors = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = query,
            [1] = other,
        };
        var source = new BBQMemoryVectorSource(vectors, dim, centroid);

        float queryDistance = BBQDistanceComputer.Distance(query, centroid, source.GetRawVector(1), dim);
        float storedDistance = BBQDistanceComputer.Distance(source.GetRawVector(0), source.GetRawVector(1), dim);

        Assert.Equal(storedDistance, queryDistance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    public void StoredDistance_IgnoresUnusedPaddingBits(int dim)
    {
        int packedBytes = (dim + 7) / 8;
        var bitsA = Enumerable.Repeat((byte)0xFF, packedBytes).ToArray();
        var bitsB = Enumerable.Repeat((byte)0xFF, packedBytes).ToArray();
        int usedBitsInLastByte = dim % 8;
        if (usedBitsInLastByte != 0)
            bitsB[^1] = (byte)((1 << usedBitsInLastByte) - 1);

        Assert.Equal(-dim, BBQDistanceComputer.Distance(bitsA, bitsA, dim));
        Assert.Equal(-dim, BBQDistanceComputer.Distance(bitsA, bitsB, dim));
    }

    [Fact(DisplayName = "BBQ stored-vs-stored: identical vectors have distance = -dim")]
    public void StoredVsStored_IdenticalVectors_DistanceNegDim()
    {
        int dim = 128;
        var bits = new byte[(dim + 7) / 8];
        bits.AsSpan().Fill(0xAA);

        float d = BBQDistanceComputer.Distance(bits, bits, dim);
        Assert.Equal(-dim, d);
    }

    [Fact(DisplayName = "BBQ stored-vs-stored: complementary vectors have distance = dim")]
    public void StoredVsStored_ComplementaryVectors_DistanceDim()
    {
        int dim = 64;
        var bitsA = new byte[dim / 8];
        var bitsB = new byte[dim / 8];
        bitsA.AsSpan().Fill(0xFF);
        bitsB.AsSpan().Fill(0x00);

        float d = BBQDistanceComputer.Distance(bitsA, bitsB, dim);
        Assert.Equal(dim, d);
    }

    [Fact(DisplayName = "BBQ stored-vs-stored: half-matching vectors distance = 0")]
    public void StoredVsStored_HalfMatching()
    {
        int dim = 64;
        var bitsA = new byte[dim / 8];
        var bitsB = new byte[dim / 8];
        bitsA.AsSpan().Fill(0xFF);
        bitsB.AsSpan().Fill(0x0F);

        float d = BBQDistanceComputer.Distance(bitsA, bitsB, dim);
        Assert.Equal(0f, d);
    }

    [Fact(DisplayName = "BBQ stored-vs-stored: odd dimension handled correctly")]
    public void StoredVsStored_OddDimension()
    {
        int dim = 65;
        var bits = new byte[(dim + 7) / 8];
        bits.AsSpan().Fill(0xFF);

        float d = BBQDistanceComputer.Distance(bits, bits, dim);
        Assert.Equal(-dim, d);
    }
}
