using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public enum HybridScenario
{
    VectorOnlyAligned,
    FilteredVectorBroad,
    FilteredVectorMedium,
    FilteredVectorNarrow,
    RrfAligned,
    RrfConflict
}

public enum HybridCorrelation
{
    Aligned,
    Neutral,
    Conflict
}

public sealed record HybridRecord(
    long Ordinal,
    string Id,
    string Title,
    string Body,
    string Topic,
    string Category,
    string AccessGroup,
    float[] Vector);

public sealed class LeanCorpusHybridProfile : IDataForgeGeneratedProfile<HybridRecord>
{
    private const int Grid = 8_388_608;
    private const string ProfileId = "leancorpus-hybrid";
    private static readonly string[] TopicValues =
        ["government", "market", "technology", "science", "health", "travel", "sports", "culture"];
    private static readonly string[] FillerWords =
        ["report", "analysis", "regional", "document", "reference", "signal", "archive", "research"];

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        ProfileId, 1, "Generated", 42, 20_000,
        "Correlated text, vector and keyword records for hybrid retrieval benchmarks.");

    public IDataForgeCanonicalRecordWriter<HybridRecord> CanonicalRecordWriter { get; } = new RecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; } = [];

    public static IReadOnlyList<string> Topics => TopicValues;

    public IEnumerable<HybridRecord> Generate(DataForgeGenerationOptions options)
    {
        var dimension = ParseDimension(options);
        var centroids = BuildCentroids(options.Seed, dimension);
        for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
            yield return GenerateRecord(options.Seed, ordinal, dimension, centroids);
    }

    public float[] GenerateTopicQuery(ulong seed, int topicIndex, int dimension)
    {
        if ((uint)topicIndex >= (uint)TopicValues.Length)
            throw new ArgumentOutOfRangeException(nameof(topicIndex));
        if (dimension is < 2 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(dimension));
        var centroid = BuildCentroids(seed, dimension)[topicIndex];
        var vector = new float[dimension];
        for (var index = 0; index < dimension; index++)
        {
            var random = new DataForgePrng(DataForgeSeedDerivation.Derive(
                seed, ProfileId, 1, (ulong)topicIndex, $"hybrid/query/component/{index}"));
            var raw = Math.Clamp(centroid[index] + random.NextInt32(-65_536, 65_537), -Grid, Grid - 1);
            vector[index] = raw / (float)Grid;
        }
        EnsureNonZero(vector);
        return vector;
    }

    public HybridCorrelation Classify(ulong seed, long ordinal)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        var roll = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)ordinal)
            .Random("hybrid/correlation").NextInt32(10_000);
        return roll < 7_000 ? HybridCorrelation.Aligned
            : roll < 9_000 ? HybridCorrelation.Neutral
            : HybridCorrelation.Conflict;
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) =>
    [
        new("dimension", ParseDimension(options).ToString(CultureInfo.InvariantCulture)),
        new("topicCount", TopicValues.Length.ToString(CultureInfo.InvariantCulture)),
        new("recordCount", options.RecordCount.ToString(CultureInfo.InvariantCulture))
    ];

    private HybridRecord GenerateRecord(ulong seed, int ordinal, int dimension, int[][] centroids)
    {
        var context = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)ordinal);
        var topicIndex = context.Random("hybrid/topic").NextInt32(TopicValues.Length);
        var topic = TopicValues[topicIndex];
        var correlation = Classify(seed, ordinal);
        var textTopic = correlation switch
        {
            HybridCorrelation.Aligned => topic,
            HybridCorrelation.Neutral => null,
            HybridCorrelation.Conflict => TopicValues[(topicIndex + 1 +
                context.Random("hybrid/conflict-topic").NextInt32(TopicValues.Length - 1)) % TopicValues.Length],
            _ => throw new InvalidOperationException("Unknown hybrid correlation class.")
        };

        var filler = context.Random("hybrid/text");
        var body = string.Join(' ', Enumerable.Range(0, 24)
            .Select(_ => FillerWords[filler.NextInt32(FillerWords.Length)]));
        if (textTopic is not null)
            body = string.Concat(textTopic, " ", body);

        var category = $"hybrid-category-{context.Random("hybrid/category").NextInt32(32):D2}";
        var accessRoll = context.Random("hybrid/access-group").NextInt32(10_000);
        var accessGroup = accessRoll switch
        {
            < 5_000 => "public",
            < 7_000 => "team-a",
            < 9_000 => "team-b",
            _ => $"tenant-{context.Random("hybrid/tenant").NextInt32(100):D2}"
        };

        var vector = new float[dimension];
        for (var index = 0; index < dimension; index++)
        {
            var noise = context.Random($"hybrid/vector/component/{index}").NextInt32(-524_288, 524_289);
            var raw = Math.Clamp(centroids[topicIndex][index] + noise, -Grid, Grid - 1);
            vector[index] = raw / (float)Grid;
        }
        EnsureNonZero(vector);

        return new HybridRecord(ordinal, $"hybrid-{ordinal:D8}",
            $"Hybrid reference {ordinal:D8}", body, topic, category, accessGroup, vector);
    }

    private static int ParseDimension(DataForgeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var key in options.Parameters.Keys)
            if (!string.Equals(key, "dimension", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown hybrid parameter '{key}'.", nameof(options));
        var text = options.Parameters.GetValueOrDefault("dimension");
        if (text is null)
            return 64;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var dimension) ||
            dimension is < 2 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(options), "Hybrid dimension must be between 2 and 4,096.");
        return dimension;
    }

    private static int[][] BuildCentroids(ulong seed, int dimension)
    {
        var centroids = new int[TopicValues.Length][];
        for (var topic = 0; topic < centroids.Length; topic++)
        {
            var random = new DataForgePrng(DataForgeSeedDerivation.Derive(
                seed, ProfileId, 1, ulong.MaxValue - (ulong)topic, "hybrid/centroid"));
            var vector = new int[dimension];
            for (var index = 0; index < dimension; index++)
                vector[index] = random.NextInt32(-4_194_304, 4_194_305);
            centroids[topic] = vector;
        }
        return centroids;
    }

    private static void EnsureNonZero(float[] vector)
    {
        if (vector.All(static value => value == 0f))
            vector[0] = 1f / Grid;
    }

    private sealed class RecordWriter : IDataForgeCanonicalRecordWriter<HybridRecord>
    {
        public void Write(CanonicalJsonWriter writer, HybridRecord record)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ordinal");
            writer.WriteInt64Value(record.Ordinal);
            writer.WritePropertyName("id");
            writer.WriteStringValue(record.Id);
            writer.WritePropertyName("title");
            writer.WriteStringValue(record.Title);
            writer.WritePropertyName("body");
            writer.WriteStringValue(record.Body);
            writer.WritePropertyName("topic");
            writer.WriteStringValue(record.Topic);
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
