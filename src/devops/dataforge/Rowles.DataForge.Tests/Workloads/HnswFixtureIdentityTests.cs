using System.Reflection;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class HnswFixtureIdentityTests
{
    [Fact]
    public void FixtureMarker_RejectsPreviousCoreBuildAndMissingMarker()
    {
        var benchmark = new HnswSearchBenchmarks { DocCount = 300, Dimension = 64 };
        var dataset = BenchmarkVectorData.Get(300, 64);
        var benchmarkId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var oldCoreId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var newCoreId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var original = benchmark.CreateFixtureIdentityValue(dataset, benchmarkId, oldCoreId);
        var current = benchmark.CreateFixtureIdentityValue(dataset, benchmarkId, newCoreId);
        var temporaryDirectory = Directory.CreateTempSubdirectory("lc-fixture-marker-");
        try
        {
            var hnsw = Directory.CreateDirectory(Path.Combine(temporaryDirectory.FullName, "hnsw")).FullName;
            var flat = Directory.CreateDirectory(Path.Combine(temporaryDirectory.FullName, "flat")).FullName;
            var marker = Path.Combine(temporaryDirectory.FullName, "fixture.identity");
            var method = typeof(HnswSearchBenchmarks).GetMethod("IsFixtureReady",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            File.WriteAllText(marker, original);
            Assert.False((bool)method.Invoke(null, [marker, current, hnsw, flat])!);
            File.WriteAllText(marker, current);
            Assert.True((bool)method.Invoke(null, [marker, current, hnsw, flat])!);
            File.Delete(marker);
            Assert.False((bool)method.Invoke(null, [marker, current, hnsw, flat])!);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void FixtureIdentity_ChangesWhenOnlyCoreBuildChanges()
    {
        var benchmark = new HnswSearchBenchmarks { DocCount = 300, Dimension = 64 };
        var dataset = BenchmarkVectorData.Get(300, 64);
        var benchmarkId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var oldCoreId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var newCoreId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var original = benchmark.CreateFixtureIdentityValue(dataset, benchmarkId, oldCoreId);

        Assert.Equal(original, benchmark.CreateFixtureIdentityValue(dataset, benchmarkId, oldCoreId));
        Assert.NotEqual(original, benchmark.CreateFixtureIdentityValue(dataset, benchmarkId, newCoreId));
        Assert.NotEqual(original, benchmark.CreateFixtureIdentityValue(dataset, newCoreId, oldCoreId));
    }

    [Fact]
    public void FixtureIdentity_IncludesCoreImplementation()
    {
        var benchmark = new HnswSearchBenchmarks { DocCount = 300, Dimension = 64 };
        var method = typeof(HnswSearchBenchmarks).GetMethod("CreateFixtureIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fixture = method.Invoke(benchmark, [BenchmarkVectorData.Get(300, 64)])!;
        var identity = (string)fixture.GetType().GetProperty("Value")!.GetValue(fixture)!;

        Assert.Contains(typeof(IndexWriter).Assembly.ManifestModule.ModuleVersionId.ToString("N"), identity);
        Assert.StartsWith("hnsw-search-fixture-v2|", identity);
    }
}
