using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus;
using Rowles.LeanCorpus.Benchmarks;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class VectorBenchmarkMatrixTests
{
    [Fact]
    public void VectorQuantisationMatrix_CoversSupportedDimensionsAndRetiredCodecIsAbsent()
    {
        Assert.Equal([32, 64, 128, 384, 768], ReadParameterValues<int>(nameof(VectorQuantisationBenchmarks.Dimension)));
        Assert.Equal(
            [VectorQuantisation.None, VectorQuantisation.Int8],
            ReadParameterValues<VectorQuantisation>(nameof(VectorQuantisationBenchmarks.Quantisation)));
        Assert.Equal([10_000, 100_000], VectorQuantisationBenchmarks.DocCounts);
    }

    [Fact]
    public void HnswMatrix_CoversSupportedRepresentationsDimensionsAndScaleCounts()
    {
        Assert.Equal([32, 64, 128, 384, 768], ReadParameterValues<int>(typeof(HnswSearchBenchmarks), "Dimension"));
        Assert.Equal(
            [VectorQuantisation.None, VectorQuantisation.Int8],
            ReadParameterValues<VectorQuantisation>(typeof(HnswSearchBenchmarks), "Quantisation"));
        Assert.Equal(
            [VectorDistribution.Uniform, VectorDistribution.Clustered, VectorDistribution.QuantisationHostile],
            ReadParameterValues<VectorDistribution>(typeof(HnswSearchBenchmarks), "Distribution"));
        Assert.Equal([42, 2025], ReadParameterValues<int>(typeof(HnswSearchBenchmarks), "QuerySeed"));
        Assert.Equal([10_000, 100_000], HnswSearchBenchmarks.DocCounts);
    }

    [Fact]
    public void HnswGraphBuild_MatchesFrozenQualificationManifest()
    {
        var config = HnswSearchBenchmarks.CreateGraphBuildConfig();

        Assert.Equal(16, config.M);
        Assert.Equal(32, config.M0);
        Assert.Equal(100, config.EfConstruction);
        Assert.Equal(1L, HnswSearchBenchmarks.GraphSeed);
    }

    [Fact]
    public void HnswFixtureBuild_RecordsOnePrimarySegmentForTheEntireCorpus()
    {
        MethodInfo? buildIndex = typeof(HnswSearchBenchmarks).GetMethod(
            "BuildIndex",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(buildIndex);

        const int documentCount = 10_001;
        const int dimension = 32;
        float[][] vectors = new float[documentCount][];
        for (int document = 0; document < vectors.Length; document++)
        {
            vectors[document] = new float[dimension];
            for (int component = 0; component < dimension; component++)
                vectors[document][component] = MathF.Sin((document + 1) * (component + 1) * 0.013f);
        }

        string indexPath = Path.Combine(Path.GetTempPath(), "lc-hnsw-single-segment-" + Guid.NewGuid().ToString("N"));
        try
        {
            object? evidence = buildIndex.Invoke(null, [indexPath, vectors, false, VectorQuantisation.None]);
            Assert.NotNull(evidence);

            Type evidenceType = evidence.GetType();
            int? segmentCount = evidenceType.GetProperty("SegmentCount")?.GetValue(evidence) as int?;
            int? segmentDocumentCount = evidenceType.GetProperty("SegmentDocumentCount")?.GetValue(evidence) as int?;

            Assert.Equal(1, segmentCount);
            Assert.Equal(documentCount, segmentDocumentCount);

            SegmentInfo segment = Assert.IsType<SegmentInfo>(evidenceType.GetProperty("Segment")?.GetValue(evidence));
            using var directory = new MMapDirectory(indexPath);
            using var reader = new SegmentReader(directory, segment);
            for (int document = 0; document < vectors.Length; document++)
            {
                float[] actual = Assert.IsType<float[]>(reader.GetVector("emb", document));
                double dot = 0;
                double expectedLengthSquared = 0;
                double actualLengthSquared = 0;
                for (int component = 0; component < dimension; component++)
                {
                    dot += vectors[document][component] * actual[component];
                    expectedLengthSquared += vectors[document][component] * vectors[document][component];
                    actualLengthSquared += actual[component] * actual[component];
                }

                double cosine = dot / Math.Sqrt(expectedLengthSquared * actualLengthSquared);
                Assert.True(cosine > 0.999999, $"Segment document {document} is not in source ordinal order.");
            }
        }
        finally
        {
            if (Directory.Exists(indexPath))
                Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void HnswScaleQueries_UseFrozenDefaultEfBudgetsWithoutAnOverrideParameter()
    {
        LeanCorpusDefaults.Reset();
        Assert.Null(typeof(HnswSearchBenchmarks).GetProperty("EfSearch"));
        HnswSearchBudgetMeasurement[] budgets = HnswSearchBenchmarks.CreateQualificationBudgets(10_000);

        Assert.Equal(
        [
            new HnswSearchBudgetMeasurement(1, 64, 64, 1),
            new HnswSearchBudgetMeasurement(10, 64, 64, 10),
            new HnswSearchBudgetMeasurement(100, 400, 400, 100),
        ],
        budgets);
    }

    [Fact]
    public void HnswDocumentCountOverride_ResolvesBothFrozenScaleCounts()
    {
        string? original = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        try
        {
            Environment.SetEnvironmentVariable("BENCH_DOC_COUNT", "10000");
            Assert.Equal([10_000], HnswSearchBenchmarks.DocCounts);
            Environment.SetEnvironmentVariable("BENCH_DOC_COUNT", "100000");
            Assert.Equal([100_000], HnswSearchBenchmarks.DocCounts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENCH_DOC_COUNT", original);
        }
    }

    [Fact]
    public void HnswSetupEvidence_UsesRepresentationNeutralExactCount()
    {
        var measurement = new HnswGraphQualityMeasurement("vector-query-0000", 10, 10, 9, 0.9);
        var json = JsonSerializer.SerializeToElement(measurement, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Equal(10, json.GetProperty("exactRepresentationCount").GetInt32());
        Assert.False(json.TryGetProperty("exactInt8Count", out _));
    }

    [Fact]
    public void ExplicitBbqBenchmarkRepresentation_IsRejected()
    {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            BenchmarkVectorData.ValidateSupportedQuantisation(VectorQuantisation.BBQ));

        Assert.Contains("Float32 and Int8", exception.Message, StringComparison.Ordinal);
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

    private static T[] ReadParameterValues<T>(string propertyName)
        => ReadParameterValues<T>(typeof(VectorQuantisationBenchmarks), propertyName);

    private static T[] ReadParameterValues<T>(Type benchmarkType, string propertyName)
    {
        PropertyInfo property = benchmarkType.GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Benchmark property '{propertyName}' was not found.");
        CustomAttributeData attribute = CustomAttributeData.GetCustomAttributes(property)
            .Single(item => item.AttributeType == typeof(ParamsAttribute));
        CustomAttributeTypedArgument valuesArgument = Assert.Single(attribute.ConstructorArguments);
        var values = Assert.IsAssignableFrom<IReadOnlyCollection<CustomAttributeTypedArgument>>(valuesArgument.Value);
        return values.Select(static value => ConvertParameterValue<T>(value)).ToArray();
    }

    private static T ConvertParameterValue<T>(CustomAttributeTypedArgument value)
    {
        object rawValue = value.Value ?? throw new InvalidOperationException("Benchmark parameter value cannot be null.");
        object converted = value.ArgumentType.IsEnum
            ? Enum.ToObject(value.ArgumentType, rawValue)
            : Convert.ChangeType(rawValue, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        return (T)converted;
    }
}
