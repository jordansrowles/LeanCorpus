using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class VectorBenchmarkMatrixTests
{
    [Fact]
    public void RetiredVectorSearchSuitesAreNotExposedByTheBenchmarkHost()
    {
        Assert.DoesNotContain(BenchmarkSuiteRegistry.Descriptors, static suite => suite.Id is "hnsw" or "vq");
        Assert.Throws<ArgumentException>(() => BenchmarkSuiteRegistry.ParseSuiteId("hnsw"));
        Assert.Throws<ArgumentException>(() => BenchmarkSuiteRegistry.ParseSuiteId("vq"));
        Assert.DoesNotContain("HnswSearchBenchmarks", BenchmarkSuiteRegistry.CreateManifestJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("VectorQuantisationBenchmarks", BenchmarkSuiteRegistry.CreateManifestJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExactFloat32Baseline_RejectsAnUnrelatedNeighbour()
    {
        VectorRecord[] records =
        [
            new(0, "a", 0, "category", "public", [1f, 0f]),
            new(1, "b", 0, "category", "public", [0f, 1f]),
        ];
        var result = BenchmarkExactFloat32Quality.Evaluate(records,
            new VectorQueryCase("query", 0, [1f, 0f]), [new ScoreDoc(1, 0f)], topK: 1);

        Assert.False(result.Passed);
        Assert.Throws<InvalidOperationException>(() => BenchmarkExactFloat32Quality.Require([result]));
    }

    [Theory]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    public void RetiredQuantisedBenchmarkRepresentationsAreRejected(VectorQuantisation representation)
    {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            BenchmarkVectorData.ValidateSupportedQuantisation(representation));

        Assert.Contains("Float32 exact", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VectorDatasetCacheKey_IncludesDistributionSeedAndQueryCount()
    {
        BenchmarkVectorDataset clustered42 = BenchmarkVectorData.Get(300, 32, VectorDistribution.Clustered, 42, 4);
        BenchmarkVectorDataset clustered2025 = BenchmarkVectorData.Get(300, 32, VectorDistribution.Clustered, 2025, 4);
        BenchmarkVectorDataset clusteredQuery2025 = BenchmarkVectorData.Get(300, 32, VectorDistribution.Clustered, 42, 2025, 4);
        BenchmarkVectorDataset uniform42 = BenchmarkVectorData.Get(300, 32, VectorDistribution.Uniform, 42, 4);
        BenchmarkVectorDataset clustered42TwoQueries = BenchmarkVectorData.Get(300, 32, VectorDistribution.Clustered, 42, 2);

        Assert.NotSame(clustered42, clustered2025);
        Assert.NotSame(clustered42, clusteredQuery2025);
        Assert.NotSame(clustered42, uniform42);
        Assert.NotSame(clustered42, clustered42TwoQueries);
        Assert.Equal(42UL, clustered42.Identity.Seed);
        Assert.Equal(2025UL, clustered2025.Identity.Seed);
        Assert.Equal("4", clustered42.Identity.Parameters["queryCount"]);
        Assert.Equal("2", clustered42TwoQueries.Identity.Parameters["queryCount"]);
        Assert.Equal(2025UL, clusteredQuery2025.QuerySeed);
        Assert.Equal(clustered42.Identity.ContentSha256, clusteredQuery2025.Identity.ContentSha256);
        Assert.Equal(4, clustered42.Queries.Length);
        Assert.Equal(["vector-query-0000", "vector-query-0001", "vector-query-0002", "vector-query-0003"],
            clustered42.Queries.Select(static query => query.Id));
        Assert.NotEqual(clustered42.Queries[0].Vector, clustered2025.Queries[0].Vector);
        Assert.NotEqual(clustered42.Queries[0].Vector, clusteredQuery2025.Queries[0].Vector);
        Assert.NotEqual(clustered42.Records[0].Vector, uniform42.Records[0].Vector);
    }

    [Fact]
    public void DefaultUniformDataset_PreservesTheProfileVersionOneContentHash()
    {
        BenchmarkVectorDataset dataset = BenchmarkVectorData.Get(500, 32);

        Assert.Equal("7a35c00028745efcda39a34d3472789aca8ffb9b2a42165a8ecfb37ea7f660cc",
            dataset.Identity.ContentSha256);
        Assert.Equal(42UL, dataset.Identity.Seed);
        Assert.Equal(42UL, dataset.QuerySeed);
        Assert.Single(dataset.Queries);
    }
}
