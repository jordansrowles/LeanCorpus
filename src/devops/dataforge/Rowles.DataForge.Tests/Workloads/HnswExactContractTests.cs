using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.DataForge.Tests.Workloads;

[CollectionDefinition("Vector benchmark process state", DisableParallelization = true)]
public sealed class VectorBenchmarkProcessStateCollection;

[Collection("Vector benchmark process state")]
public sealed class ExactFloat32ContractTests
{
    [Fact]
    public void ExactBoundaryTie_AllowsAnEligibleNeighbourWithinTheFixedTolerance()
    {
        VectorRecord[] records =
        [
            new(0, "a", 0, "category", "public", [1f, 0f]),
            new(1, "b", 0, "category", "public", [1f, 0.0001f]),
        ];
        var result = BenchmarkExactFloat32Quality.Evaluate(records,
            new VectorQueryCase("query", 0, [1f, 0f]), [new ScoreDoc(1, 1f)], topK: 1);

        Assert.True(result.Passed);
        Assert.Equal(0, result.RequiredHigherRankCount);
        Assert.Equal(1, result.ReturnedBoundaryTieCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("underfilled")]
    [InlineData("out-of-range")]
    [InlineData("non-finite")]
    [InlineData("score-error")]
    [InlineData("order")]
    public void ExactContract_RejectsInvalidResults(string scenario)
    {
        VectorRecord[] records =
        [
            new(0, "a", 0, "category", "public", [1f, 0f]),
            new(1, "b", 0, "category", "public", [1f, 1f]),
            new(2, "c", 0, "category", "public", [0f, 1f]),
        ];
        float boundary = (float)(1 / Math.Sqrt(2));
        ScoreDoc[] returned = scenario switch
        {
            "missing" => [new(1, boundary), new(2, 0)],
            "duplicate" => [new(0, 1), new(0, 1)],
            "underfilled" => [new(0, 1)],
            "out-of-range" => [new(0, 1), new(99, 0)],
            "non-finite" => [new(0, float.NaN), new(1, boundary)],
            "score-error" => [new(0, 0.9f), new(1, boundary)],
            "order" => [new(1, boundary), new(0, 1)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var result = BenchmarkExactFloat32Quality.Evaluate(records,
            new VectorQueryCase("query", 0, [1f, 0f]), returned, topK: 2);

        Assert.False(result.Passed);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign-query")]
    public void ExactEvidence_RejectsIncompleteOrMismatchedQueryAndKCoverage(string scenario)
    {
        var dataset = BenchmarkVectorData.Get(300, 64, VectorDistribution.Uniform, 42, 42, 4);
        var contracts = new List<VectorExactFloat32ContractResult>();
        var queryResults = dataset.Queries.Select(query => new VectorQueryQualityResult(query.Id,
            BenchmarkVectorQuality.Measure(dataset, query, k =>
            {
                var truth = VectorGroundTruth.Compute(dataset.Records, query, k);
                ScoreDoc[] returned = truth.NeighbourOrdinals.Zip(truth.CosineValues)
                    .Select(pair => new ScoreDoc(checked((int)pair.First), (float)pair.Second)).ToArray();
                contracts.Add(BenchmarkExactFloat32Quality.Evaluate(dataset.Records, query, returned, k));
                return returned;
            }, quantised: false))).ToArray();
        IReadOnlyList<VectorExactFloat32ContractResult> incomplete = scenario switch
        {
            "empty" => [],
            "missing" => contracts.Skip(1).ToArray(),
            "duplicate" => contracts.Take(11).Append(contracts[0]).ToArray(),
            "foreign-query" => contracts.Skip(1).Append(contracts[0] with { QueryId = "unknown" }).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        Assert.Throws<InvalidOperationException>(() => BenchmarkVectorData.WriteVectorQualityGroup(
            dataset, "exact-evidence", scenario, new { }, queryResults, incomplete));
    }
}
