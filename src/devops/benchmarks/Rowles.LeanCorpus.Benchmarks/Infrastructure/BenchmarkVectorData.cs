using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record BenchmarkVectorDataset(
    VectorRecord[] Records,
    VectorQueryCase[] Queries,
    ulong QuerySeed,
    DataForgeDatasetIdentity Identity)
{
    public VectorQueryCase Query => Queries[0];
}

internal static class BenchmarkVectorData
{
    private static readonly ConcurrentDictionary<DatasetCacheKey, Lazy<BenchmarkVectorDataset>> Cache = new();
    private static readonly JsonSerializerOptions QualityJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void ValidateSupportedQuantisation(VectorQuantisation quantisation)
    {
        if (quantisation != VectorQuantisation.None)
            throw new NotSupportedException(
                $"Vector benchmark creation supports Float32 exact search only; {quantisation} writes are retired.");
    }

    public static BenchmarkVectorDataset Get(int count, int dimension)
        => Get(count, dimension, VectorDistribution.Uniform, 42, 42, 1);

    public static BenchmarkVectorDataset Get(
        int count,
        int dimension,
        VectorDistribution distribution,
        ulong seed,
        int queryCount)
        => Get(count, dimension, distribution, seed, seed, queryCount);

    public static BenchmarkVectorDataset Get(
        int count,
        int dimension,
        VectorDistribution distribution,
        ulong seed,
        ulong querySeed,
        int queryCount)
    {
        if (count < 1 || dimension < 2 || queryCount is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(count));
        var key = new DatasetCacheKey(count, dimension, distribution, seed, querySeed, queryCount);
        var dataset = Cache.GetOrAdd(key, static key =>
            new Lazy<BenchmarkVectorDataset>(() => Generate(key),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        BenchmarkDatasetSidecars.Write(dataset.Identity);
        return dataset;
    }

    public static void WriteVectorQuality(
        BenchmarkVectorDataset dataset,
        string benchmark,
        string parameterKey,
        object parameters,
        IReadOnlyList<VectorQualityMeasurement> measurements)
        => WriteVectorQuality(dataset, dataset.Query, dataset.QuerySeed, benchmark, parameterKey, parameters, measurements);

    public static void WriteVectorQuality(
        BenchmarkVectorDataset dataset,
        VectorQueryCase query,
        ulong querySeed,
        string benchmark,
        string parameterKey,
        object parameters,
        IReadOnlyList<VectorQualityMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmark);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterKey);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(measurements);
        BenchmarkVectorQuality.ValidateFixture();
        var root = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("LEANCORPUS_ARTIFACT_DIR must be set before recording vector quality.");
        var directory = Path.Combine(Path.GetFullPath(root), "vector-recall");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"{dataset.Identity.GetShortKey()}-{benchmark}-{parameterKey}-{query.Id}-q{querySeed}.json");
        double minimumRecall = BenchmarkVectorQuality.MinimumAcceptedRecall;
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fixtureId = "bval-09-vector-recall-ground-truth",
            benchmark,
            benchmarkParameters = parameters,
            minimumAcceptedRecall = minimumRecall,
            qualityPassed = measurements.All(measurement => measurement.RecallAtK + 1e-12 >= minimumRecall),
            profileId = dataset.Identity.ProfileId,
            profileVersion = dataset.Identity.ProfileVersion,
            seed = dataset.Identity.Seed,
            querySeed,
            recordCount = dataset.Identity.RecordCount,
            datasetParameters = dataset.Identity.Parameters,
            contentSha256 = dataset.Identity.ContentSha256,
            queryId = query.Id,
            metric = "cosine",
            exactGroundTruth = "VectorGroundTruth over original unquantised source records",
            measurements
        }, QualityJsonOptions);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static void WriteVectorQualityGroup(
        BenchmarkVectorDataset dataset,
        string benchmark,
        string parameterKey,
        object parameters,
        IReadOnlyList<VectorQueryQualityResult> queryResults,
        IReadOnlyList<VectorExactFloat32ContractResult>? exactFloat32Contracts = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmark);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterKey);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(queryResults);
        VectorHeldOutQualitySummary qualitySummary = BenchmarkVectorQuality.EvaluateHeldOutGroup(
            queryResults, $"{benchmark} {parameterKey} held-out quality");
        if (exactFloat32Contracts is not null)
        {
            var expected = queryResults.SelectMany(query => query.Measurements
                .Select(measurement => (query.QueryId, measurement.TopK))).Order().ToArray();
            var actual = exactFloat32Contracts.Select(result => (result.QueryId, result.TopK)).Order().ToArray();
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException("Exact Float32 evidence must cover each query and K exactly once.");
        }
        var root = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("LEANCORPUS_ARTIFACT_DIR must be set before recording vector quality.");
        var directory = Path.Combine(Path.GetFullPath(root), "vector-recall");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,
            $"{dataset.Identity.GetShortKey()}-{benchmark}-{parameterKey}-heldout-q{dataset.QuerySeed}.json");
        var means = new[] { 1, 10, 100 }.ToDictionary(
            static topK => $"recallAt{topK}",
            topK => queryResults.Average(result =>
                result.Measurements.Single(measurement => measurement.TopK == topK).RecallAtK));
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            fixtureId = "bval-09-vector-recall-ground-truth",
            benchmark,
            benchmarkParameters = parameters,
            querySeed = dataset.QuerySeed,
            queryCount = queryResults.Count,
            qualityBasis = exactFloat32Contracts is null ? "literal-source-recall" : "exact-float32-score-and-boundary-ties",
            qualitySummary = exactFloat32Contracts is null ? (object)qualitySummary : new
            {
                passed = exactFloat32Contracts.All(result => result.Passed),
                scoreTolerance = BenchmarkExactFloat32Quality.ScoreTolerance,
                boundaryTieTolerance = BenchmarkExactFloat32Quality.BoundaryTieTolerance,
                results = exactFloat32Contracts,
            },
            literalRecallSummary = qualitySummary,
            meanRecalls = means,
            qualityPassed = exactFloat32Contracts?.All(result => result.Passed) ?? qualitySummary.Passed,
            profileId = dataset.Identity.ProfileId,
            profileVersion = dataset.Identity.ProfileVersion,
            seed = dataset.Identity.Seed,
            recordCount = dataset.Identity.RecordCount,
            datasetParameters = dataset.Identity.Parameters,
            contentSha256 = dataset.Identity.ContentSha256,
            metric = "cosine",
            exactGroundTruth = "VectorGroundTruth over original unquantised source records",
            queryResults,
        }, QualityJsonOptions);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static BenchmarkVectorDataset Generate(DatasetCacheKey key)
    {
        var profile = new LeanCorpusVectorProfile();
        var parameters = new Dictionary<string, string>
        {
            ["dimension"] = key.Dimension.ToString(CultureInfo.InvariantCulture),
            ["vectorDistribution"] = key.Distribution.ToString(),
            ["clusterCount"] = Math.Min(8, key.Count).ToString(CultureInfo.InvariantCulture),
            ["queryCount"] = key.QueryCount.ToString(CultureInfo.InvariantCulture)
        };
        var options = new DataForgeGenerationOptions(key.Seed, key.Count, parameters);
        var queryOptions = new DataForgeGenerationOptions(key.QuerySeed, key.Count, parameters);
        var records = profile.Generate(options).ToArray();
        using var writer = new CanonicalJsonWriter(Stream.Null);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }
        var identity = new DataForgeDatasetIdentity(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            profile.Descriptor.ProfileId,
            profile.Descriptor.ProfileVersion,
            DatasetId: null,
            DatasetVersion: null,
            Seed: options.Seed,
            RecordCount: records.Length,
            Parameters: options.Parameters,
            Dependencies: profile.Dependencies.ToArray(),
            ContentSha256: Convert.ToHexString(writer.GetSha256()).ToLowerInvariant());
        var queries = new VectorQueryCase[key.QueryCount];
        for (var queryIndex = 0; queryIndex < queries.Length; queryIndex++)
            queries[queryIndex] = profile.GenerateQuery(queryOptions, queryIndex);
        return new BenchmarkVectorDataset(records, queries, key.QuerySeed, identity);
    }

    private readonly record struct DatasetCacheKey(
        int Count,
        int Dimension,
        VectorDistribution Distribution,
        ulong Seed,
        ulong QuerySeed,
        int QueryCount);
}
