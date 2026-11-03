using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class VectorQualityGateTests
{
    [Fact]
    public void HeldOutGate_RequiresExactlyFourQueries()
    {
        VectorQueryQualityResult[] results = Enumerable.Range(0, 3)
            .Select(static index => CreateQueryResult(index, 1, 10, 100))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            BenchmarkVectorQuality.RequireHeldOutGroup(results, "held-out test"));
    }

    [Fact]
    public void HeldOutGate_EnforcesPerQueryMinimumRecallAtTen()
    {
        VectorQueryQualityResult[] results =
        [
            CreateQueryResult(0, 1, 10, 100),
            CreateQueryResult(1, 1, 10, 100),
            CreateQueryResult(2, 1, 10, 100),
            CreateQueryResult(3, 1, 7, 100),
        ];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkVectorQuality.RequireHeldOutGroup(results, "held-out test"));

        Assert.Contains("minimum recall@10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeldOutGate_EnforcesMeanRecallAtTen()
    {
        VectorQueryQualityResult[] results = Enumerable.Range(0, 4)
            .Select(static index => CreateQueryResult(index, 1, 8, 100))
            .ToArray();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkVectorQuality.RequireHeldOutGroup(results, "held-out test"));

        Assert.Contains("mean recall@10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeldOutGate_EnforcesMeanRecallAtOneAndOneHundred()
    {
        VectorQueryQualityResult[] badTopOne =
        [
            CreateQueryResult(0, 1, 10, 100),
            CreateQueryResult(1, 1, 10, 100),
            CreateQueryResult(2, 1, 10, 100),
            CreateQueryResult(3, 0, 10, 100),
        ];
        VectorQueryQualityResult[] badTopHundred =
        [
            CreateQueryResult(0, 1, 10, 100),
            CreateQueryResult(1, 1, 10, 100),
            CreateQueryResult(2, 1, 10, 100),
            CreateQueryResult(3, 1, 10, 50),
        ];

        Assert.Contains("mean recall@1", Assert.Throws<InvalidOperationException>(() =>
            BenchmarkVectorQuality.RequireHeldOutGroup(badTopOne, "held-out test")).Message, StringComparison.Ordinal);
        Assert.Contains("mean recall@100", Assert.Throws<InvalidOperationException>(() =>
            BenchmarkVectorQuality.RequireHeldOutGroup(badTopHundred, "held-out test")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HeldOutGate_AcceptsTheDeclaredGroupBoundaries()
    {
        VectorQueryQualityResult[] results =
        [
            CreateQueryResult(0, 1, 10, 90),
            CreateQueryResult(1, 1, 8, 90),
            CreateQueryResult(2, 1, 10, 90),
            CreateQueryResult(3, 1, 9, 90),
        ];

        BenchmarkVectorQuality.RequireHeldOutGroup(results, "held-out test");
    }

    private static VectorQueryQualityResult CreateQueryResult(int queryIndex, int topOneHits, int topTenHits, int topHundredHits)
        => new($"vector-query-{queryIndex:D4}",
        [
            CreateMeasurement(1, topOneHits),
            CreateMeasurement(10, topTenHits),
            CreateMeasurement(100, topHundredHits),
        ]);

    private static VectorQualityMeasurement CreateMeasurement(int topK, int hits)
        => new(topK, topK, hits, (double)hits / topK, null, null, null, null, null, null);
}
