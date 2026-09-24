using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares eligible best-first nearest execution with the exact general sort
/// over the same committed point index, and records packed traversal counters.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class SpatialNearestBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    public static IEnumerable<string> SpatialKinds
        => GetStringParameterValues("LEANCORPUS_SPATIAL_NEAREST_KINDS", "Geo", "XY");

    [ParamsSource(nameof(SpatialKinds))]
    public string SpatialKind { get; set; } = "Geo";

    public static IEnumerable<string> Distributions
        => GetStringParameterValues("LEANCORPUS_SPATIAL_NEAREST_DISTRIBUTIONS", "Uniform", "Clustered", "MultiValue");

    [ParamsSource(nameof(Distributions))]
    public string Distribution { get; set; } = "Uniform";

    public static IEnumerable<string> FilterSelectivities
        => GetStringParameterValues("LEANCORPUS_SPATIAL_NEAREST_FILTERS", "MatchAll", "50%", "10%", "1%", "Empty");

    [ParamsSource(nameof(FilterSelectivities))]
    public string FilterSelectivity { get; set; } = "MatchAll";

    public static IEnumerable<int> TopNValues
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("LEANCORPUS_SPATIAL_NEAREST_TOP_NS");
            if (string.IsNullOrWhiteSpace(configured))
                return [10, 100, 1000];

            int[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int topN)
                    && topN > 0
                        ? topN
                        : throw new InvalidOperationException(
                            $"Invalid Top-N value '{value}' in LEANCORPUS_SPATIAL_NEAREST_TOP_NS."))
                .Distinct()
                .ToArray();
            return values.Length == 0
                ? throw new InvalidOperationException("LEANCORPUS_SPATIAL_NEAREST_TOP_NS must contain a positive integer.")
                : values;
        }
    }

    [ParamsSource(nameof(TopNValues))]
    public int TopN { get; set; }

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<string, string?> _latestActivityTags = new(StringComparer.Ordinal);
    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _searcher;
    private Query _query = new MatchAllDocsQuery();
    private ActivityListener? _activityListener;
    private long _indexedPointValues;

    [GlobalSetup]
    public void Setup()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "Rowles.LeanCorpus",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = CaptureBestFirstActivity
        };
        ActivitySource.AddActivityListener(_activityListener);

        _indexPath = BenchmarkHelpers.CreateTempDirectory("lc-spatial-nearest");
        BuildIndex();
        _directory = new MMapDirectory(_indexPath);
        _searcher = new IndexSearcher(_directory);
        _query = CreateQuery();

        SortField sort = CreateSort();
        TopDocs nearest = _searcher.Search(_query, TopN, sort);
        TopDocs exhaustive = _searcher.Search(_query, TopN, [sort, SortField.DocId]);
        if (nearest.TotalHits != exhaustive.TotalHits
            || !nearest.ScoreDocs.Select(static hit => hit.DocId)
                .SequenceEqual(exhaustive.ScoreDocs.Select(static hit => hit.DocId)))
            throw new InvalidOperationException("Best-first nearest results differ from the exhaustive distance sort.");

        WriteObserverEvidence(nearest.TotalHits);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _searcher = null;
        _directory?.Dispose();
        _directory = null;
        _activityListener?.Dispose();
        _activityListener = null;
        if (!string.IsNullOrWhiteSpace(_indexPath))
            BenchmarkHelpers.DeleteDirectory(_indexPath);
        _indexPath = string.Empty;
    }

    [Benchmark(Baseline = true, Description = "Spatial nearest exhaustive exact sort")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SpatialNearest_Exhaustive()
        => RunExhaustive();

    [Benchmark(Description = "Spatial nearest best-first Packed BKD Top-N")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SpatialNearest_BestFirst()
    {
        TopDocs result = _searcher!.Search(_query, TopN, CreateSort());
        return result.ScoreDocs.Length == 0 ? -1 : result.ScoreDocs[^1].DocId;
    }

    private int RunExhaustive()
    {
        SortField sort = CreateSort();
        TopDocs result = _searcher!.Search(_query, TopN, [sort, SortField.DocId]);
        return result.ScoreDocs.Length == 0 ? -1 : result.ScoreDocs[^1].DocId;
    }

    private void BuildIndex()
    {
        var random = new Random(0x0700_2032);
        using var directory = new MMapDirectory(_indexPath);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { BKDMaxLeafSize = 64 });
        for (int documentId = 0; documentId < DocumentCount; documentId++)
        {
            var document = new LeanDocument();
            if (documentId % 2 == 0)
                document.Add(new StringField("spatial_eligibility", "half", stored: false, boost: 1,
                    storeDocValues: false, indexOptions: FieldIndexOptions.DocsOnly));
            if (documentId % 10 == 0)
                document.Add(new StringField("spatial_eligibility", "tenth", stored: false, boost: 1,
                    storeDocValues: false, indexOptions: FieldIndexOptions.DocsOnly));
            if (documentId % 100 == 0)
                document.Add(new StringField("spatial_eligibility", "one", stored: false, boost: 1,
                    storeDocValues: false, indexOptions: FieldIndexOptions.DocsOnly));

            if (SpatialKind == "Geo")
            {
                for (int point = 0; point < GetPointCount(documentId); point++)
                {
                    (double latitude, double longitude) = CreateGeoPoint(random);
                    document.Add(new GeoPointField("location", latitude, longitude));
                    _indexedPointValues++;
                }
            }
            else
            {
                for (int point = 0; point < GetPointCount(documentId); point++)
                {
                    (float x, float y) = CreateXyPoint(random);
                    document.Add(new XYPointField("position", x, y));
                    _indexedPointValues++;
                }
            }

            writer.AddDocument(document);
        }

        writer.Commit();
        writer.ForceMerge(1);
        writer.Commit();
    }

    private (double Latitude, double Longitude) CreateGeoPoint(Random random)
        => Distribution == "Clustered"
            ? (random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1)
            : (random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180);

    private (float X, float Y) CreateXyPoint(Random random)
    {
        double extent = Distribution == "Clustered" ? 1_000 : 1_000_000;
        return ((float)(random.NextDouble() * 2 * extent - extent),
            (float)(random.NextDouble() * 2 * extent - extent));
    }

    private int GetPointCount(int documentId)
        => Distribution == "MultiValue"
            ? (documentId % 10 == 0 ? 3 : 2)
            : (documentId % 17 == 0 ? 2 : 1);

    private static IEnumerable<string> GetStringParameterValues(string environmentVariable, params string[] defaults)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return defaults;

        string[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
            throw new InvalidOperationException($"{environmentVariable} must contain one or more values.");
        string? invalid = values.FirstOrDefault(value => !defaults.Contains(value, StringComparer.Ordinal));
        return invalid is null
            ? values
            : throw new InvalidOperationException($"Unknown value '{invalid}' in {environmentVariable}.");
    }

    private Query CreateQuery()
    {
        if (FilterSelectivity == "MatchAll")
            return new MatchAllDocsQuery();

        string term = FilterSelectivity switch
        {
            "50%" => "half",
            "10%" => "tenth",
            "1%" => "one",
            "Empty" => "empty",
            _ => throw new InvalidOperationException($"Unknown spatial filter selectivity '{FilterSelectivity}'.")
        };
        return new ConstantScoreQuery(new TermQuery("spatial_eligibility", term));
    }

    private SortField CreateSort()
        => SpatialKind == "Geo"
            ? SortField.GeoDistance("location", new GeoPoint(0, 0))
            : SortField.XYDistance("position", new XYPoint(0, 0));

    private void CaptureBestFirstActivity(Activity activity)
    {
        if (activity.OperationName != "leancorpus.search.packed_bkd.best_first")
            return;

        _latestActivityTags.Clear();
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
            _latestActivityTags[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
    }

    private void WriteObserverEvidence(int totalHits)
    {
        string? directory = Environment.GetEnvironmentVariable("LEANCORPUS_SPATIAL_NEAREST_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        System.IO.Directory.CreateDirectory(directory);
        string fileName = string.Join("_", new[]
        {
            "nearest",
            SpatialKind.ToLowerInvariant(),
            Distribution.ToLowerInvariant(),
            FilterSelectivity.Replace("%", "percent", StringComparison.Ordinal).ToLowerInvariant(),
            DocumentCount.ToString(CultureInfo.InvariantCulture),
            TopN.ToString(CultureInfo.InvariantCulture)
        }) + ".json";
        var evidence = new ObserverEvidence(
            DocumentCount,
            TopN,
            SpatialKind,
            Distribution,
            _indexedPointValues,
            totalHits,
            _searcher!.GetSegmentReaders().Count,
            FilterSelectivity,
            new Dictionary<string, string?>(_latestActivityTags, StringComparer.Ordinal));
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    private sealed record ObserverEvidence(
        int DocumentCount,
        int TopN,
        string SpatialKind,
        string Distribution,
        long IndexedPointValues,
        int TotalHits,
        int SegmentCount,
        string FilterSelectivity,
        Dictionary<string, string?> TraversalCounters);
}
