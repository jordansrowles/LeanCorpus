using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public enum VectorDistribution
{
    Uniform,
    Clustered,
    DenseNeighbourhood,
    NearDuplicate,
    QuantisationFriendly,
    QuantisationHostile
}

public sealed record VectorRecord(
    long Ordinal,
    string Id,
    int ClusterId,
    string Category,
    string AccessGroup,
    float[] Vector);

public sealed record VectorQueryCase(string Id, int TargetClusterId, float[] Vector);

public sealed record VectorGroundTruthResult(
    string QueryId,
    int TopK,
    string Metric,
    long[] NeighbourOrdinals,
    double[] CosineValues);

public static class VectorGroundTruth
{
    public static VectorGroundTruthResult Compute(
        IEnumerable<VectorRecord> records,
        VectorQueryCase query,
        int topK,
        Func<long, bool>? allowedOrdinal = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(query);
        if (topK < 1)
            throw new ArgumentOutOfRangeException(nameof(topK));
        if (query.Vector.Length == 0)
            throw new ArgumentException("The query vector must have at least one component.", nameof(query));

        var scores = new List<(long Ordinal, double Cosine)>();
        foreach (var record in records)
        {
            if (allowedOrdinal is not null && !allowedOrdinal(record.Ordinal))
                continue;
            if (record.Vector.Length != query.Vector.Length)
                throw new ArgumentException("Record and query dimensions differ.", nameof(records));

            double dot = 0, recordNorm = 0, queryNorm = 0;
            for (var dimension = 0; dimension < query.Vector.Length; dimension++)
            {
                var left = (double)record.Vector[dimension];
                var right = (double)query.Vector[dimension];
                dot += left * right;
                recordNorm += left * left;
                queryNorm += right * right;
            }
            if (recordNorm == 0 || queryNorm == 0)
                throw new InvalidDataException("Cosine ground truth requires non-zero vectors.");
            scores.Add((record.Ordinal, dot / Math.Sqrt(recordNorm * queryNorm)));
        }

        var ranked = scores.OrderByDescending(static item => item.Cosine)
            .ThenBy(static item => item.Ordinal)
            .Take(topK).ToArray();
        return new VectorGroundTruthResult(
            query.Id, topK, "cosine",
            ranked.Select(static item => item.Ordinal).ToArray(),
            ranked.Select(static item => item.Cosine).ToArray());
    }
}

public sealed class LeanCorpusVectorProfile : IDataForgeGeneratedProfile<VectorRecord>
{
    private const int Grid = 8_388_608;
    private const string ProfileId = "leancorpus-vector";
    private static readonly string[] AccessGroups =
        ["public", "team-a", "team-b", .. Enumerable.Range(0, 64).Select(static value => $"tenant-{value:D2}")];
    private static readonly float[] FriendlyValues = [-1f, -0.5f, 0f, 0.5f, 1f];
    private static readonly int[] HostileJitter = [-3, -2, -1, 1, 2, 3];

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        ProfileId, 1, "Generated", 42, 1_000,
        "Deterministic synthetic vectors for search and quantisation workloads.");

    public IDataForgeCanonicalRecordWriter<VectorRecord> CanonicalRecordWriter { get; } = new RecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; } = [];

    public IEnumerable<VectorRecord> Generate(DataForgeGenerationOptions options) => GenerateCore(options, allowStressCount: false);

    internal IEnumerable<VectorRecord> GenerateStress(DataForgeGenerationOptions options) => GenerateCore(options, allowStressCount: true);

    private IEnumerable<VectorRecord> GenerateCore(DataForgeGenerationOptions options, bool allowStressCount)
    {
        var parameters = Parse(options, allowStressCount);
        var centroids = parameters.Distribution is VectorDistribution.Clustered or VectorDistribution.DenseNeighbourhood
            ? BuildCentroids(options.Seed, parameters.EffectiveClusterCount, parameters.Dimension)
            : null;
        for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
            yield return GenerateRecord(options.Seed, ordinal, parameters, centroids);
    }

    public VectorQueryCase GenerateQuery(DataForgeGenerationOptions options, int queryIndex)
    {
        var parameters = Parse(options);
        if ((uint)queryIndex >= (uint)parameters.QueryCount)
            throw new ArgumentOutOfRangeException(nameof(queryIndex));

        var targetCluster = queryIndex % parameters.EffectiveClusterCount;
        var vector = new float[parameters.Dimension];
        for (var dimension = 0; dimension < vector.Length; dimension++)
        {
            var random = new DataForgePrng(DataForgeSeedDerivation.Derive(
                options.Seed, ProfileId, 1, (ulong)queryIndex,
                $"vector/query/{queryIndex}/component/{dimension}"));
            var raw = parameters.Distribution is VectorDistribution.Clustered or VectorDistribution.DenseNeighbourhood
                ? Math.Clamp(CentroidRaw(options.Seed, targetCluster, dimension) +
                    random.NextInt32(-524_288, 524_289), -Grid, Grid - 1)
                : random.NextInt32(-Grid, Grid);
            vector[dimension] = raw / (float)Grid;
        }
        EnsureNonZero(vector);
        return new VectorQueryCase($"vector-query-{queryIndex:D4}", targetCluster, vector);
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options)
    {
        var parameters = Parse(options);
        return
        [
            new("dimension", parameters.Dimension.ToString(CultureInfo.InvariantCulture)),
            new("vectorDistribution", parameters.Distribution.ToString()),
            new("clusterCount", parameters.ClusterCount.ToString(CultureInfo.InvariantCulture)),
            new("queryCount", parameters.QueryCount.ToString(CultureInfo.InvariantCulture))
        ];
    }

    private static VectorRecord GenerateRecord(ulong seed, int ordinal, Parameters parameters, int[][]? centroids)
    {
        var context = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)ordinal);
        var cluster = context.Random("vector/cluster-assignment").NextInt32(parameters.EffectiveClusterCount);
        var category = $"vector-category-{context.Random("filter/category").NextInt32(32):D2}";
        var accessGroup = AccessGroups[context.Random("filter/access-group").NextInt32(AccessGroups.Length)];
        var vector = new float[parameters.Dimension];
        var member = ordinal % 16;
        var group = ordinal / 16;
        for (var dimension = 0; dimension < vector.Length; dimension++)
        {
            var random = parameters.Distribution == VectorDistribution.NearDuplicate
                ? new DataForgePrng(DataForgeSeedDerivation.Derive(seed, ProfileId, 1, (ulong)group,
                    $"vector/base/component/{dimension}"))
                : context.Random($"vector/component/{dimension}");
            var raw = parameters.Distribution switch
            {
                VectorDistribution.Uniform or VectorDistribution.NearDuplicate => random.NextInt32(-Grid, Grid),
                VectorDistribution.Clustered => Math.Clamp(
                    centroids![cluster][dimension] + random.NextInt32(-524_288, 524_289), -Grid, Grid - 1),
                VectorDistribution.DenseNeighbourhood => Math.Clamp(
                    centroids![cluster][dimension] + random.NextInt32(-32_768, 32_769), -Grid, Grid - 1),
                VectorDistribution.QuantisationFriendly =>
                    (int)(FriendlyValues[random.NextInt32(FriendlyValues.Length)] * Grid),
                VectorDistribution.QuantisationHostile => Math.Clamp(
                    random.NextInt32(-128, 128) * 65_536 + HostileJitter[random.NextInt32(HostileJitter.Length)],
                    -Grid, Grid - 1),
                _ => throw new ArgumentOutOfRangeException(nameof(parameters))
            };
            if (parameters.Distribution == VectorDistribution.NearDuplicate && member != 0 &&
                dimension == member % parameters.Dimension)
                raw = Math.Clamp(raw + (member % 2 == 1 ? 1 : -1), -Grid, Grid - 1);
            vector[dimension] = raw / (float)Grid;
        }
        EnsureNonZero(vector);
        return new VectorRecord(ordinal, $"vector-{ordinal:D8}", cluster, category, accessGroup, vector);
    }

    private static int CentroidRaw(ulong seed, int cluster, int dimension)
    {
        var random = new DataForgePrng(DataForgeSeedDerivation.Derive(
            seed, ProfileId, 1, ulong.MaxValue - (ulong)cluster, "vector/centroid"));
        for (var index = 0; index < dimension; index++)
            random.NextInt32(-4_194_304, 4_194_305);
        return random.NextInt32(-4_194_304, 4_194_305);
    }

    private static int[][] BuildCentroids(ulong seed, int count, int dimension)
    {
        var centroids = new int[count][];
        for (var cluster = 0; cluster < count; cluster++)
        {
            var random = new DataForgePrng(DataForgeSeedDerivation.Derive(
                seed, ProfileId, 1, ulong.MaxValue - (ulong)cluster, "vector/centroid"));
            var components = new int[dimension];
            for (var index = 0; index < dimension; index++)
                components[index] = random.NextInt32(-4_194_304, 4_194_305);
            centroids[cluster] = components;
        }
        return centroids;
    }

    private static void EnsureNonZero(float[] vector)
    {
        if (vector.All(static value => value == 0))
            vector[0] = 1f / Grid;
    }

    private static Parameters Parse(DataForgeGenerationOptions options, bool allowStressCount = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!allowStressCount && options.RecordCount > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Vector count must not exceed 1,000,000.");
        foreach (var key in options.Parameters.Keys)
            if (key is not ("dimension" or "vectorDistribution" or "clusterCount" or "queryCount"))
                throw new ArgumentException($"Unknown vector parameter '{key}'.", nameof(options));

        var dimension = ParseInt("dimension", 64, 2, 4_096);
        var clusterCount = ParseInt("clusterCount", Math.Min(8, options.RecordCount), 1, Math.Min(1_024, options.RecordCount));
        var queryCount = ParseInt("queryCount", 1, 1, 10_000);
        var distributionText = options.Parameters.GetValueOrDefault("vectorDistribution") ?? "Uniform";
        if (!Enum.TryParse<VectorDistribution>(distributionText, ignoreCase: false, out var distribution) ||
            !Enum.IsDefined(distribution))
            throw new ArgumentException($"Unknown vector distribution '{distributionText}'.", nameof(options));
        return new Parameters(dimension, distribution, clusterCount, queryCount,
            distribution == VectorDistribution.DenseNeighbourhood ? Math.Min(clusterCount, 8) : clusterCount);

        int ParseInt(string key, int fallback, int minimum, int maximum)
        {
            var text = options.Parameters.GetValueOrDefault(key);
            if (text is null)
                return fallback;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(nameof(options), $"{key} must be between {minimum} and {maximum}.");
            return value;
        }
    }

    private sealed record Parameters(int Dimension, VectorDistribution Distribution, int ClusterCount,
        int QueryCount, int EffectiveClusterCount);

    private sealed class RecordWriter : IDataForgeCanonicalRecordWriter<VectorRecord>
    {
        public void Write(CanonicalJsonWriter writer, VectorRecord record)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ordinal");
            writer.WriteInt64Value(record.Ordinal);
            writer.WritePropertyName("id");
            writer.WriteStringValue(record.Id);
            writer.WritePropertyName("clusterId");
            writer.WriteInt32Value(record.ClusterId);
            writer.WritePropertyName("category");
            writer.WriteStringValue(record.Category);
            writer.WritePropertyName("accessGroup");
            writer.WriteStringValue(record.AccessGroup);
            writer.WritePropertyName("vectorBitsBase64");
            writer.WriteSingleBitsBase64Value(record.Vector);
            writer.WriteEndObject();
        }
    }
}
