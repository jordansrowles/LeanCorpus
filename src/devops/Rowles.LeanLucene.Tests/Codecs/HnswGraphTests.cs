using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for HNSW Graph.
/// </summary>
[Trait("Category", "Hnsw")]
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

        var results = graph.Search(new float[] { 1, 0, 0, 0 }, new HnswSearchOptions { Ef = 10, TopK = 5 });

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

        var results = graph.Search(Normalise([0.9f, 0.1f, 0, 0]), new HnswSearchOptions { Ef = 16, TopK = 1 });

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
            var hnswTop = graph.Search(query, new HnswSearchOptions { Ef = 50, TopK = topK })
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
                Assert.Equal(graphA.GetNeighbours(docId, level).OrderBy(x => x),
                             graphB.GetNeighbours(docId, level).OrderBy(x => x));
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
