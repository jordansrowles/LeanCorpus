using static Rowles.LeanCorpus.Tests.Core.Codecs.VectorGraphTestData;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Numerical stability and the reachability postcondition of the Float32 vector graph (ADR052):
/// graph distances must rank every finite input as the exact scorer does, and a published graph
/// must reach every node from its entry point.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class VectorGraphRobustnessTests
{
    private static readonly HnswBuildConfig Default = new() { M = 16, EfConstruction = 100 };

    [Fact(DisplayName = "Numerics: Inverse Norms Mark Zero, Conditioned And Scaled Vectors")]
    public void Numerics_InverseNormsMarkZeroConditionedAndScaledVectors()
    {
        Assert.Equal(0f, VectorGraph.InverseNormOf([0f, 0f]));
        Assert.Equal(0.2f, VectorGraph.InverseNormOf([3f, 4f]));
        Assert.Equal(VectorGraph.ScaledCosine, VectorGraph.InverseNormOf([1e20f, 0f]));
        Assert.Equal(VectorGraph.ScaledCosine, VectorGraph.InverseNormOf([1e-40f, 0f]));
        Assert.Equal(VectorGraph.ScaledCosine, VectorGraph.InverseNormOf([float.MaxValue, float.MaxValue]));
        Assert.Equal(VectorGraph.ScaledCosine, VectorGraph.InverseNormOf([float.Epsilon, 0f]));
        Assert.True(VectorGraph.IsValidInverseNorm(VectorGraph.InverseNormOf([1e15f, 0f])));
        Assert.True(VectorGraph.IsValidInverseNorm(VectorGraph.InverseNormOf([1e-15f, 0f])));
    }

    [Theory(DisplayName = "Numerics: Node Distances Equal The Exact Cosine At Extreme Magnitudes")]
    [InlineData(1e20f, 1e20f)]
    [InlineData(1e-40f, 1e-40f)]
    [InlineData(1e-40f, 1f)]
    [InlineData(1e20f, 1e-40f)]
    [InlineData(3e38f, 1e-30f)]
    [InlineData(1e15f, 1e-15f)]
    public void Numerics_NodeDistancesEqualExactCosine(float scaleA, float scaleB)
    {
        float[] a = [scaleA, 0f, scaleA];
        float[] b = [scaleB, scaleB, 0f];
        float distance = VectorGraph.NodeDistance(a, VectorGraph.InverseNormOf(a), b, VectorGraph.InverseNormOf(b));
        Assert.Equal(-Cosine(a, b), distance, 1e-6);
        float self = VectorGraph.NodeDistance(a, VectorGraph.InverseNormOf(a), a, VectorGraph.InverseNormOf(a));
        Assert.Equal(-1.0, self, 1e-6);
    }

    [Fact(DisplayName = "Numerics: Zero Vectors Have Zero Distance To Any Vector")]
    public void Numerics_ZeroVectorsHaveZeroDistance()
    {
        float[] zero = [0f, 0f];
        foreach (float[] other in new[] { new[] { 1e20f, 0f }, new[] { 1e-40f, 0f }, new[] { 1f, 1f }, zero })
            Assert.Equal(0f, VectorGraph.NodeDistance(zero, 0f, other, VectorGraph.InverseNormOf(other)));
    }

    [Fact(DisplayName = "Numerics: Query Distances Equal The Exact Cosine For Every Node")]
    public void Numerics_QueryDistancesEqualExactCosineForEveryNode()
    {
        float[][] vectors =
        [
            [1e20f, 0f], [1e-40f, 0f], [0f, 1e-40f], [1e-40f, 1e-40f], [3e38f, -3e38f],
            [0f, 0f], [1f, 0f], [0f, 1f], [1e-30f, 1e30f], [-1e20f, 1e-40f],
        ];
        using var graph = Build(vectors, Default, seed: 3, normalised: false);
        Assert.Equal(VectorGraph.ScaledCosine, graph.InverseNorm(0));
        Assert.Equal(VectorGraph.ScaledCosine, graph.InverseNorm(1));
        Assert.Equal(0f, graph.InverseNorm(5));

        foreach (float[] query in new[] { new[] { 1f, 0f }, new[] { 0.6f, 0.8f }, new[] { -1f, 0f } })
        {
            var results = new List<VectorGraphHit>();
            graph.Search(query, new ArrayVectorSource(vectors), k: vectors.Length, ef: vectors.Length, eligible: null,
                evaluationBudget: long.MaxValue, results);
            Assert.Equal(vectors.Length, results.Count);
            Assert.All(results, hit => Assert.Equal(-Cosine(query, vectors[hit.DocId]), hit.Distance, 1e-6));
        }
    }

    [Fact(DisplayName = "Numerics: Mixed Magnitudes Rank By Cosine With Recall")]
    public void Numerics_MixedMagnitudesRankByCosineWithRecall()
    {
        // Directions are clustered; one magnitude per vector spans 1e-35 to 1e35, so most nodes need
        // the scaled cosine and every pair mixes magnitudes.
        var random = new Random(41); // NOSONAR S2245: deterministic test data
        var vectors = Clustered(2_000, 16, clusters: 20, spread: 0.15f, seed: 40)
            .Select(v =>
            {
                float scale = (float)Math.Pow(10, random.NextDouble() * 70 - 35);
                return v.Select(x => x * scale).ToArray();
            })
            .ToArray();
        Assert.True(vectors.Count(v => VectorGraph.InverseNormOf(v) == VectorGraph.ScaledCosine) > vectors.Length / 2); // NOSONAR S1244: exact marker
        vectors[11] = new float[16];
        using var graph = Build(vectors, Default, seed: 5, normalised: false);
        Assert.Equal(graph.NodeCount, graph.CountReachableFromEntry());

        double recall = 0;
        var queries = Enumerable.Range(0, 50).Select(i => Normalise(vectors[i * 37 % vectors.Length])).ToArray();
        foreach (var query in queries)
        {
            var results = new List<VectorGraphHit>();
            graph.Search(query, new ArrayVectorSource(vectors), k: 10, ef: 64, null, long.MaxValue, results);
            recall += TieAwareRecall(vectors, query, results.Select(static h => h.DocId).ToArray(), 10);
        }
        Assert.True(recall / queries.Length >= 0.95, $"Tie-aware recall@10 was {recall / queries.Length:F3}.");
    }

    [Theory(DisplayName = "Reachability: Seeded Adversarial Topologies Stay Fully Reachable")]
    [InlineData("far-clusters", 2)]
    [InlineData("far-clusters", 16)]
    [InlineData("duplicates", 2)]
    [InlineData("duplicates", 8)]
    [InlineData("saturated", 4)]
    [InlineData("mixed", 3)]
    public void Reachability_AdversarialTopologiesStayFullyReachable(string topology, int m)
    {
        var config = new HnswBuildConfig { M = m, EfConstruction = 16 };
        for (int seed = 0; seed < 12; seed++)
        {
            var vectors = Topology(topology, seed);
            using var graph = TryBuild(vectors, config, seed: seed, normalised: false);
            Assert.NotNull(graph);
            Assert.Equal(graph.NodeCount, graph.CountReachableFromEntry());
            Assert.Equal(graph.NodeCount, ReachableFromEntry(graph));
        }
    }

    [Fact(DisplayName = "Reachability: An Unrepaired Disconnected Graph Is Refused And Repair Connects It")]
    public void Reachability_UnrepairedDisconnectedGraphIsRefused()
    {
        // Without repair, tight far clusters at a tiny degree leave nodes stranded on some seed.
        var config = new HnswBuildConfig { M = 2, EfConstruction = 4 };
        int refused = 0;
        for (int seed = 0; seed < 40 && refused == 0; seed++)
        {
            var vectors = Topology("far-clusters", seed);
            using var unrepaired = TryBuild(vectors, config, seed, normalised: false, repairPasses: 0);
            if (unrepaired is not null)
                continue;
            refused++;
            using var repaired = TryBuild(vectors, config, seed, normalised: false);
            Assert.NotNull(repaired);
            Assert.Equal(repaired.NodeCount, repaired.CountReachableFromEntry());
        }

        Assert.True(refused > 0, "No seed produced a disconnected unrepaired graph; the fixture no longer exercises refusal.");
    }

    [Fact(DisplayName = "Budget: A Zero Budget Evaluates Nothing, Even On A Single Node")]
    public void Budget_ZeroBudgetEvaluatesNothing()
    {
        float[][] one = [[1f, 0f]];
        using var single = Build(one, Default, seed: 1, normalised: true);
        var results = new List<VectorGraphHit>();

        var zero = single.Search([1f, 0f], new ArrayVectorSource(one), k: 1, ef: 1, null, evaluationBudget: 0, results);
        Assert.True(zero.BudgetExceeded);
        Assert.Equal(0, zero.DistanceEvaluations);
        Assert.Empty(results);

        var once = single.Search([1f, 0f], new ArrayVectorSource(one), k: 1, ef: 1, null, evaluationBudget: 1, results);
        Assert.False(once.BudgetExceeded);
        Assert.Equal(1, once.DistanceEvaluations);
        Assert.Single(results);
    }

    [Fact(DisplayName = "Budget: Every Evaluation, Descent Included, Counts Against The Budget")]
    public void Budget_EveryEvaluationCountsAgainstTheBudget()
    {
        var vectors = NormaliseAll(Clustered(3_000, 16, clusters: 12, spread: 0.2f, seed: 81));
        using var graph = Build(vectors, new HnswBuildConfig { M = 4, EfConstruction = 40 }, seed: 8, normalised: true);
        Assert.True(graph.MaxLevel >= 3, "The fixture needs several upper levels.");
        var source = new ArrayVectorSource(vectors);
        float[] query = Normalise(vectors[17]);
        var results = new List<VectorGraphHit>();
        var unlimited = graph.Search(query, source, k: 10, ef: 32, null, long.MaxValue, results);
        Assert.False(unlimited.BudgetExceeded);

        // Small budgets end inside the upper-level descent; the rest end at level zero.
        for (long budget = 0; budget <= unlimited.DistanceEvaluations + 2; budget++)
        {
            var stats = graph.Search(query, source, k: 10, ef: 32, null, budget, results);
            Assert.True(stats.DistanceEvaluations <= budget, $"Budget {budget} allowed {stats.DistanceEvaluations} evaluations.");
            Assert.Equal(budget < unlimited.DistanceEvaluations, stats.BudgetExceeded);
            if (stats.BudgetExceeded && stats.LayersDescended < graph.MaxLevel)
                Assert.Empty(results);
            if (!stats.BudgetExceeded)
                Assert.Equal(unlimited.DistanceEvaluations, stats.DistanceEvaluations);
        }
    }

    private static float[][] Topology(string topology, int seed)
    {
        var random = new Random(1_000 + seed); // NOSONAR S2245: deterministic test data
        switch (topology)
        {
            case "far-clusters":
                // Many tight clusters on near-orthogonal directions: neighbours stay in-cluster.
                return Clustered(600, 24, clusters: 24, spread: 0.002f, seed: seed);
            case "duplicates":
                // Few distinct vectors, each repeated many times: pruning keeps one of each.
                var distinct = Clustered(6, 8, clusters: 6, spread: 0.5f, seed: seed);
                return Enumerable.Range(0, 500).Select(i => (float[])distinct[i % distinct.Length].Clone()).ToArray();
            case "saturated":
                // A dense core every node prefers, plus outliers that compete for its slots.
                var core = Clustered(450, 8, clusters: 1, spread: 0.01f, seed: seed);
                var outliers = Uniform(150, 8, seed);
                return [.. core, .. outliers];
            default:
                var mixed = Clustered(500, 12, clusters: 10, spread: 0.01f, seed: seed);
                for (int i = 0; i < mixed.Length; i += 9)
                    mixed[i] = (float[])mixed[0].Clone();
                for (int i = 4; i < mixed.Length; i += 13)
                {
                    float scale = (float)Math.Pow(10, random.NextDouble() * 60 - 30);
                    mixed[i] = mixed[i].Select(x => x * scale).ToArray();
                }
                mixed[7] = new float[12];
                return mixed;
        }
    }
}
