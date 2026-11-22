using static Rowles.LeanCorpus.Tests.Core.Codecs.VectorGraphTestData;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>Construction and traversal tests for the Float32 vector graph (ADR052).</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class VectorGraphTests
{
    private static readonly HnswBuildConfig Default = new() { M = 16, EfConstruction = 100 };

    [Fact(DisplayName = "Build: Every Structural Invariant Holds")]
    public void Build_EveryStructuralInvariantHolds()
    {
        var vectors = NormaliseAll(Clustered(3_000, 32, clusters: 30, spread: 0.15f, seed: 11));
        using var graph = Build(vectors, Default, seed: 7, normalised: true);

        Assert.Equal(vectors.Length, graph.NodeCount);
        Assert.Equal(32, graph.M0);
        Assert.Equal(graph.NodeCount, ReachableFromEntry(graph));
        for (int ordinal = 0; ordinal < graph.NodeCount; ordinal++)
        {
            var neighbours = graph.Neighbours(ordinal, 0).ToArray();
            Assert.InRange(neighbours.Length, 1, graph.M0);
            Assert.DoesNotContain(ordinal, neighbours);
            Assert.Equal(neighbours.Length, neighbours.Distinct().Count());
        }

        for (int level = 1; level <= graph.MaxLevel; level++)
        {
            var members = graph.LevelOrdinals(level).ToArray();
            var below = level == 1 ? Enumerable.Range(0, graph.NodeCount).ToHashSet() : graph.LevelOrdinals(level - 1).ToArray().ToHashSet();
            Assert.All(members, member => Assert.Contains(member, below));
            foreach (int member in members)
            {
                var neighbours = graph.Neighbours(member, level).ToArray();
                Assert.InRange(neighbours.Length, 0, graph.M);
                Assert.All(neighbours, neighbour => Assert.Contains(neighbour, members));
                Assert.DoesNotContain(member, neighbours);
            }
        }

        Assert.Contains(graph.EntryOrdinal, graph.MaxLevel == 0
            ? Enumerable.Range(0, graph.NodeCount).ToArray()
            : graph.LevelOrdinals(graph.MaxLevel).ToArray());
    }

    [Fact(DisplayName = "Build: Level Distribution Follows One Over M")]
    public void Build_LevelDistributionFollowsOneOverM()
    {
        var vectors = Uniform(4_000, 8, seed: 5);
        using var graph = Build(vectors, Default, seed: 99, normalised: false);

        double fraction = graph.LevelOrdinals(1).Length / (double)graph.NodeCount;
        Assert.InRange(fraction, 1.0 / 16 * 0.7, 1.0 / 16 * 1.3);
    }

    [Fact(DisplayName = "Build: Same Seed And Inputs Produce An Identical Graph")]
    public void Build_SameSeedAndInputs_ProduceIdenticalGraph()
    {
        var vectors = NormaliseAll(Clustered(1_500, 24, clusters: 12, spread: 0.2f, seed: 3));
        using var first = Build(vectors, Default, seed: 1234, normalised: true);
        using var second = Build(vectors, Default, seed: 1234, normalised: true);
        using var other = Build(vectors, Default, seed: 4321, normalised: true);

        Assert.Equal(first.EntryOrdinal, second.EntryOrdinal);
        Assert.Equal(first.MaxLevel, second.MaxLevel);
        Assert.Equal(first.CopyLevel0(), second.CopyLevel0());
        for (int level = 1; level <= first.MaxLevel; level++)
            Assert.Equal(first.UpperAdjacency(level).ToArray(), second.UpperAdjacency(level).ToArray());
        Assert.NotEqual(first.CopyLevel0(), other.CopyLevel0());
    }

    [Fact(DisplayName = "Build: Single And Two Node Graphs Are Searchable")]
    public void Build_SingleAndTwoNodeGraphs_AreSearchable()
    {
        var one = new[] { new[] { 1f, 0f } };
        using var single = Build(one, Default, seed: 1, normalised: true);
        Assert.Equal(0, single.Neighbours(0, 0).Length);
        var hits = Search(single, one, new[] { 0f, 1f }, k: 5);
        Assert.Equal([0], hits);

        var two = new[] { new[] { 1f, 0f }, new[] { 0f, 1f } };
        using var pair = Build(two, Default, seed: 1, normalised: true);
        Assert.Equal([1, 0], Search(pair, two, new[] { 0.1f, 0.9f }, k: 2));
    }

    [Fact(DisplayName = "Build: Identical Vectors Stay Connected And Return K")]
    public void Build_IdenticalVectors_StayConnectedAndReturnK()
    {
        var vectors = Enumerable.Range(0, 400).Select(static _ => new[] { 0.6f, 0.8f, 0f }).ToArray();
        using var graph = Build(vectors, Default, seed: 2, normalised: true);

        Assert.Equal(graph.NodeCount, ReachableFromEntry(graph));
        var hits = Search(graph, vectors, new[] { 0.6f, 0.8f, 0f }, k: 50, ef: 64);
        Assert.Equal(50, hits.Length);
        Assert.Equal(50, hits.Distinct().Count());
    }

    [Fact(DisplayName = "Build: Zero Vectors Remain Members With Zero Distance")]
    public void Build_ZeroVectors_RemainMembersWithZeroDistance()
    {
        var vectors = Uniform(200, 6, seed: 8);
        vectors[17] = new float[6];
        vectors[90] = new float[6];
        using var graph = Build(vectors, Default, seed: 4, normalised: false);

        Assert.Equal(200, graph.NodeCount);
        Assert.Equal(0f, graph.InverseNorm(graph.OrdinalOf(17)));
        Assert.Equal(graph.NodeCount, ReachableFromEntry(graph));

        var results = new List<VectorGraphHit>();
        graph.Search(Normalise(vectors[3]), new ArrayVectorSource(vectors), k: 200, ef: 200, eligible: null,
            evaluationBudget: long.MaxValue, results);
        Assert.Equal(0f, results.Single(hit => hit.DocId == 17).Distance);
    }

    [Fact(DisplayName = "Search: Non-Normalised Field Ranks By Cosine")]
    public void Search_NonNormalisedField_RanksByCosine()
    {
        // Wide norm spread: raw dot product would rank large vectors first.
        var unit = Clustered(2_000, 32, clusters: 20, spread: 0.2f, seed: 21);
        var random = new Random(77); // NOSONAR S2245: deterministic test data
        var scaled = unit.Select(v => v.Select(x => x * (float)Math.Pow(10, random.NextDouble() * 4 - 2)).ToArray()).ToArray();
        using var graph = Build(scaled, Default, seed: 9, normalised: false);
        Assert.True(graph.HasInverseNorms);

        double recall = MeanRecall(graph, scaled, Queries(32, 100, seed: 22), k: 10, ef: 64);
        Assert.True(recall >= 0.95, $"Tie-aware recall@10 was {recall:F3}.");
    }

    [Theory(DisplayName = "Search: Clustered Data Reaches Recall With Bounded Visits")]
    [InlineData(64, 5_000)]
    [InlineData(128, 3_000)]
    public void Search_ClusteredData_ReachesRecallWithBoundedVisits(int dimension, int count)
    {
        var vectors = NormaliseAll(Clustered(count, dimension, clusters: 50, spread: 0.08f, seed: dimension));
        using var graph = Build(vectors, Default, seed: 31, normalised: true);
        var queries = Queries(dimension, 100, seed: dimension + 1)
            .Select((q, i) => Normalise(vectors[(i * 37) % count].Zip(q, static (v, n) => v + 0.05f * n).ToArray()))
            .ToArray();

        double recall = 0, visited = 0;
        var source = new ArrayVectorSource(vectors);
        var results = new List<VectorGraphHit>();
        foreach (var query in queries)
        {
            var stats = graph.Search(query, source, k: 10, ef: 64, eligible: null, long.MaxValue, results);
            Assert.False(stats.BudgetExceeded);
            recall += TieAwareRecall(vectors, query, results.Select(static h => h.DocId).ToArray(), 10);
            visited += stats.NodesVisited / (double)graph.NodeCount;
        }

        recall /= queries.Length;
        visited /= queries.Length;
        Assert.True(recall >= 0.95, $"Tie-aware recall@10 was {recall:F3}.");
        Assert.True(visited <= 0.35, $"Mean visited fraction was {visited:F3}.");
    }

    [Fact(DisplayName = "Search: Near-Duplicate Groups Are Judged Tie-Aware")]
    public void Search_NearDuplicateGroups_AreJudgedTieAware()
    {
        // Groups of 16 differ by one ulp-scale step and collapse after normalisation.
        var random = new Random(42); // NOSONAR S2245: deterministic test data
        var vectors = new float[320][];
        for (int group = 0; group < 20; group++)
        {
            var baseVector = Gaussian(random, 64, 1f);
            for (int member = 0; member < 16; member++)
            {
                var vector = (float[])baseVector.Clone();
                if (member != 0)
                    vector[member % 64] += member % 2 == 0 ? -1e-7f : 1e-7f;
                vectors[group * 16 + member] = Normalise(vector);
            }
        }

        // Twenty random group centres in 64 dimensions have no navigable structure, so this case
        // uses the near-exhaustive budget of the plan's NearDuplicate evidence. It checks that
        // collapsed duplicates are interchangeable (tie-aware), not that the graph is efficient here.
        using var graph = Build(vectors, Default, seed: 3, normalised: true);
        double recall = MeanRecall(graph, vectors, Queries(64, 100, seed: 43).Select(Normalise).ToArray(), k: 10, ef: 256);
        Assert.True(recall >= 0.99, $"Tie-aware recall@10 was {recall:F3}.");
    }

    [Fact(DisplayName = "Search: Eligibility Restricts Results But Not Routing")]
    public void Search_Eligibility_RestrictsResultsButNotRouting()
    {
        var vectors = NormaliseAll(Clustered(3_000, 32, clusters: 30, spread: 0.1f, seed: 51));
        using var graph = Build(vectors, Default, seed: 5, normalised: true);
        var eligible = new HashSetBitSet(Enumerable.Range(0, vectors.Length).Where(static i => i % 4 == 0));
        var source = new ArrayVectorSource(vectors);
        var results = new List<VectorGraphHit>();

        double recall = 0;
        var queries = Queries(32, 50, seed: 52).Select(Normalise).ToArray();
        foreach (var query in queries)
        {
            graph.Search(query, source, k: 10, ef: 64, eligible, long.MaxValue, results);
            Assert.All(results, hit => Assert.True(eligible.Contains(hit.DocId)));
            recall += TieAwareRecall(vectors, query, results.Select(static h => h.DocId).ToArray(), 10, eligible.Contains);
        }

        Assert.True(recall / queries.Length >= 0.95, $"Filtered tie-aware recall@10 was {recall / queries.Length:F3}.");
    }

    [Fact(DisplayName = "Search: Budget Stops Traversal And Reports It")]
    public void Search_Budget_StopsTraversalAndReportsIt()
    {
        var vectors = NormaliseAll(Clustered(2_000, 16, clusters: 10, spread: 0.2f, seed: 61));
        using var graph = Build(vectors, Default, seed: 6, normalised: true);
        var none = new HashSetBitSet([]);
        var results = new List<VectorGraphHit>();

        var stats = graph.Search(Normalise(vectors[0]), new ArrayVectorSource(vectors), k: 10, ef: 64, none,
            evaluationBudget: 300, results);

        Assert.True(stats.BudgetExceeded);
        Assert.True(stats.DistanceEvaluations <= 300);
        Assert.Empty(results);
    }

    [Fact(DisplayName = "Search: Results Are Ordered Nearest First Without Duplicates")]
    public void Search_ResultsAreOrderedNearestFirstWithoutDuplicates()
    {
        var vectors = NormaliseAll(Uniform(1_000, 16, seed: 71));
        using var graph = Build(vectors, Default, seed: 7, normalised: true);
        var results = new List<VectorGraphHit>();
        graph.Search(Normalise(vectors[5]), new ArrayVectorSource(vectors), k: 100, ef: 200, null, long.MaxValue, results);

        Assert.Equal(100, results.Count);
        Assert.Equal(results.Count, results.Select(static h => h.DocId).Distinct().Count());
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Distance <= results[i].Distance);
        Assert.Equal(5, results[0].DocId);
    }

    private static int[] Search(VectorGraph graph, float[][] vectors, float[] query, int k, int ef = 64)
    {
        var results = new List<VectorGraphHit>();
        graph.Search(Normalise(query), new ArrayVectorSource(vectors), k, ef, null, long.MaxValue, results);
        return results.Select(static h => h.DocId).ToArray();
    }

    private static double MeanRecall(VectorGraph graph, float[][] vectors, float[][] queries, int k, int ef)
    {
        double total = 0;
        foreach (var query in queries)
            total += TieAwareRecall(vectors, query, Search(graph, vectors, query, k, ef), k);
        return total / queries.Length;
    }

    private static float[][] Queries(int dimension, int count, int seed)
    {
        var random = new Random(seed); // NOSONAR S2245: deterministic test data
        return Enumerable.Range(0, count).Select(_ => Gaussian(random, dimension, 1f)).ToArray();
    }

    private sealed class HashSetBitSet(IEnumerable<int> members) : IBitSet
    {
        private readonly HashSet<int> _members = [.. members];
        public bool Contains(int docId) => _members.Contains(docId);
        public int Cardinality => _members.Count;
    }
}
