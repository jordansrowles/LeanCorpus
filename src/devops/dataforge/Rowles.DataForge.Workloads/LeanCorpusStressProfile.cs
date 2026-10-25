using System.Globalization;
using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public enum StressMode
{
    Search,
    Vector,
    Hybrid
}

public sealed record StressRecord(object Payload);

public sealed class LeanCorpusStressProfile : IDataForgeGeneratedProfile<StressRecord>
{
    private const string ProfileId = "leancorpus-stress";
    private readonly LeanCorpusSearchProfile _search = new();
    private readonly LeanCorpusVectorProfile _vector = new();
    private readonly LeanCorpusHybridProfile _hybrid = new();

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        ProfileId, 1, "Generated", 42, 100_000,
        "Bounded deterministic search, vector and hybrid stress workloads.");

    public IDataForgeCanonicalRecordWriter<StressRecord> CanonicalRecordWriter { get; } = new RecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies => _search.Dependencies;

    public IEnumerable<StressRecord> Generate(DataForgeGenerationOptions options)
    {
        var parameters = Parse(options);
        switch (parameters.Mode)
        {
            case StressMode.Search:
                foreach (var record in _search.Generate(new DataForgeGenerationOptions(options.Seed, options.RecordCount)))
                    yield return new StressRecord(TransformSearch(record, options.Seed, parameters));
                break;
            case StressMode.Vector:
                var vectorOptions = new DataForgeGenerationOptions(options.Seed, options.RecordCount,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["dimension"] = Format(parameters.Dimension),
                        ["vectorDistribution"] = parameters.Distribution.ToString(),
                        ["clusterCount"] = Format(parameters.ClusterCount),
                        ["queryCount"] = Format(parameters.QueryCount)
                    });
                foreach (var record in _vector.GenerateStress(vectorOptions))
                    yield return new StressRecord(record);
                break;
            case StressMode.Hybrid:
                var hybridOptions = new DataForgeGenerationOptions(options.Seed, options.RecordCount,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["dimension"] = Format(parameters.Dimension) });
                foreach (var record in _hybrid.Generate(hybridOptions))
                    yield return new StressRecord(TransformHybrid(record, options.Seed, parameters));
                break;
            default:
                throw new InvalidOperationException("Unknown stress mode.");
        }
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options)
    {
        var parameters = Parse(options);
        var summaries = new List<DataForgeSummary> { new("mode", ModeName(parameters.Mode)) };
        if (parameters.Mode == StressMode.Search)
        {
            summaries.Add(new("veryLongShareBasisPoints", Format(parameters.VeryLongShareBasisPoints)));
            summaries.Add(new("longShareBasisPoints", Format(parameters.LongShareBasisPoints)));
            summaries.Add(new("categoryCardinality", Format(parameters.CategoryCardinality)));
            summaries.Add(new("regionCardinality", Format(parameters.RegionCardinality)));
            summaries.Add(new("rareAnchorBasisPoints", Format(parameters.RareAnchorBasisPoints)));
        }
        else if (parameters.Mode == StressMode.Vector)
        {
            summaries.Add(new("dimension", Format(parameters.Dimension)));
            summaries.Add(new("vectorDistribution", parameters.Distribution.ToString()));
            summaries.Add(new("clusterCount", Format(parameters.ClusterCount)));
            summaries.Add(new("queryCount", Format(parameters.QueryCount)));
        }
        else
        {
            summaries.Add(new("dimension", Format(parameters.Dimension)));
            summaries.Add(new("categoryCardinality", Format(parameters.CategoryCardinality)));
            summaries.Add(new("narrowFilterBasisPoints", Format(parameters.NarrowFilterBasisPoints)));
        }
        return summaries;
    }

    private static SearchRecord TransformSearch(SearchRecord record, ulong seed, Parameters parameters)
    {
        var context = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)record.Ordinal);
        var category = $"category-{context.Random("stress/category").NextInt32(parameters.CategoryCardinality):D4}";
        var region = $"region-{context.Random("stress/region").NextInt32(parameters.RegionCardinality):D3}";
        var roll = context.Random("stress/length").NextInt32(10_000);
        var targetTokens = roll < parameters.VeryLongShareBasisPoints ? 1_800
            : roll < parameters.VeryLongShareBasisPoints + parameters.LongShareBasisPoints ? 700
            : 499;
        var words = record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(static word => !string.Equals(word, "quasarneedle", StringComparison.OrdinalIgnoreCase))
            .Take(targetTokens).ToArray();
        var body = new StringBuilder(record.Body.Length + Math.Max(0, targetTokens - words.Length) * 11);
        foreach (var word in words)
        {
            if (body.Length != 0)
                body.Append(' ');
            body.Append(word);
        }
        if (targetTokens != 499)
        {
            for (var index = words.Length; index < targetTokens; index++)
                body.Append(" stressfill");
        }
        if (context.Random("stress/rare-anchor").NextInt32(10_000) < parameters.RareAnchorBasisPoints)
            body.Append(" quasarneedle");
        return record with { Body = body.ToString(), Category = category, Region = region };
    }

    private static HybridRecord TransformHybrid(HybridRecord record, ulong seed, Parameters parameters)
    {
        var context = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)record.Ordinal);
        var category = $"hybrid-category-{context.Random("stress/category").NextInt32(parameters.CategoryCardinality):D4}";
        var narrow = context.Random("stress/narrow-filter").NextInt32(10_000) < parameters.NarrowFilterBasisPoints;
        return record with { Category = category, AccessGroup = narrow ? "stress-narrow" : record.AccessGroup };
    }

    private static Parameters Parse(DataForgeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var modeText = options.Parameters.GetValueOrDefault("mode")
            ?? throw new ArgumentException("Stress mode is required: search, vector or hybrid.", nameof(options));
        var mode = modeText switch
        {
            "search" => StressMode.Search,
            "vector" => StressMode.Vector,
            "hybrid" => StressMode.Hybrid,
            _ => throw new ArgumentException($"Unknown stress mode '{modeText}'.", nameof(options))
        };
        var allowed = mode switch
        {
            StressMode.Search => new[] { "mode", "veryLongShareBasisPoints", "longShareBasisPoints", "categoryCardinality", "regionCardinality", "rareAnchorBasisPoints" },
            StressMode.Vector => new[] { "mode", "dimension", "vectorDistribution", "clusterCount", "queryCount" },
            _ => new[] { "mode", "dimension", "categoryCardinality", "narrowFilterBasisPoints" }
        };
        foreach (var key in options.Parameters.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown {modeText} stress parameter '{key}'.", nameof(options));

        var dimension = mode switch
        {
            StressMode.Vector => ReadInt("dimension", 128, 2, 4_096),
            StressMode.Hybrid => ReadInt("dimension", 64, 2, 4_096),
            _ => 0
        };
        var clusterCount = mode == StressMode.Vector
            ? ReadInt("clusterCount", Math.Min(8, options.RecordCount), 1, Math.Min(1_024, options.RecordCount)) : 0;
        var queryCount = mode == StressMode.Vector ? ReadInt("queryCount", 10, 1, 10_000) : 0;
        var distributionText = options.Parameters.GetValueOrDefault("vectorDistribution") ?? "DenseNeighbourhood";
        if (mode == StressMode.Vector &&
            (!Enum.TryParse<VectorDistribution>(distributionText, ignoreCase: false, out var parsedDistribution) ||
             !Enum.IsDefined(parsedDistribution)))
            throw new ArgumentException($"Unknown vector distribution '{distributionText}'.", nameof(options));
        var distribution = mode == StressMode.Vector
            ? Enum.Parse<VectorDistribution>(distributionText, ignoreCase: false)
            : VectorDistribution.DenseNeighbourhood;
        var veryLongShare = mode == StressMode.Search ? ReadInt("veryLongShareBasisPoints", 500, 0, 10_000) : 0;
        var longShare = mode == StressMode.Search ? ReadInt("longShareBasisPoints", 2_000, 0, 10_000) : 0;
        if (veryLongShare + longShare > 10_000)
            throw new ArgumentException("Stress long and very-long shares must total at most 10,000 basis points.", nameof(options));
        var categoryCardinality = mode switch
        {
            StressMode.Search => ReadInt("categoryCardinality", 2_048, 1, 100_000),
            StressMode.Hybrid => ReadInt("categoryCardinality", 4_096, 1, 100_000),
            _ => 0
        };
        var regionCardinality = mode == StressMode.Search ? ReadInt("regionCardinality", 256, 1, 100_000) : 0;
        var rareAnchor = mode == StressMode.Search ? ReadInt("rareAnchorBasisPoints", 2, 0, 10_000) : 0;
        var narrowFilter = mode == StressMode.Hybrid ? ReadInt("narrowFilterBasisPoints", 5, 0, 10_000) : 0;
        return new Parameters(mode, dimension, distribution, clusterCount, queryCount,
            veryLongShare, longShare, categoryCardinality, regionCardinality, rareAnchor, narrowFilter);

        int ReadInt(string key, int fallback, int minimum, int maximum)
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

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string ModeName(StressMode mode) => mode.ToString().ToLowerInvariant();

    private sealed record Parameters(
        StressMode Mode, int Dimension, VectorDistribution Distribution, int ClusterCount, int QueryCount,
        int VeryLongShareBasisPoints, int LongShareBasisPoints, int CategoryCardinality,
        int RegionCardinality, int RareAnchorBasisPoints, int NarrowFilterBasisPoints);

    private sealed class RecordWriter : IDataForgeCanonicalRecordWriter<StressRecord>
    {
        private readonly LeanCorpusSearchProfile _search = new();
        private readonly LeanCorpusVectorProfile _vector = new();
        private readonly LeanCorpusHybridProfile _hybrid = new();

        public void Write(CanonicalJsonWriter writer, StressRecord record)
        {
            switch (record.Payload)
            {
                case SearchRecord search:
                    _search.CanonicalRecordWriter.Write(writer, search);
                    break;
                case VectorRecord vector:
                    _vector.CanonicalRecordWriter.Write(writer, vector);
                    break;
                case HybridRecord hybrid:
                    _hybrid.CanonicalRecordWriter.Write(writer, hybrid);
                    break;
                default:
                    throw new ArgumentException("Unknown stress record payload.", nameof(record));
            }
        }
    }
}
