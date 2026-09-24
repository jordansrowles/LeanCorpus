using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class ShapeIndexingBenchmarks
{
    public static IEnumerable<int> VertexCounts
        => ShapeBenchmarkParameters.ReadIntegers("LEANCORPUS_SHAPE_VERTEX_COUNTS", [10, 100, 1_000, 10_000]);

    [ParamsSource(nameof(VertexCounts))]
    public int VertexCount { get; set; }

    public static IEnumerable<string> ShapeKinds
        => ShapeBenchmarkParameters.ReadStrings("LEANCORPUS_SHAPE_KINDS", ["Convex", "Concave", "OneHole", "ManyHoles"]);

    [ParamsSource(nameof(ShapeKinds))]
    public string ShapeKind { get; set; } = "Convex";

    private IXYGeometry _geometry = new XYPoint(0, 0);
    private string _path = string.Empty;
    private MMapDirectory? _directory;
    private IndexWriter? _writer;
    private long _packedBytes;
    private long _primitiveCount;
    private long _elapsedTimestampTicks;
    private int _actualInputVertexCount;

    [GlobalSetup]
    public void Setup()
    {
        _geometry = ShapeGeometryFactory.CreateIndexedGeometry(ShapeKind, VertexCount);
        _actualInputVertexCount = ShapeGeometryFactory.CountVertices(_geometry);
    }

    [IterationSetup]
    public void SetupIndex()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("lc-shape-index");
        _directory = new MMapDirectory(_path);
        _writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 64,
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = false,
        });
    }

    [Benchmark(Description = "XY shape field add and flush")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long IndexShapeAndFlush()
    {
        var document = new LeanDocument();
        document.Add(new XYShapeField("shape", _geometry));
        long start = Stopwatch.GetTimestamp();
        _writer!.AddDocument(document);
        _writer.Commit();
        _elapsedTimestampTicks = Stopwatch.GetTimestamp() - start;
        return _elapsedTimestampTicks;
    }

    [IterationCleanup]
    public void CleanupIndex()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            _directory?.Dispose();
            _directory = null;
            string? packedPath = Directory.GetFiles(_path, "*.pbkd").SingleOrDefault();
            if (packedPath is null)
                throw new InvalidDataException("Shape indexing benchmark did not produce a loose Packed BKD file.");
            _packedBytes = new FileInfo(packedPath).Length;
            using var packedReader = PackedBkdReader.Open(packedPath);
            _primitiveCount = packedReader.GetFieldMetadata("shape").PointCount;
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
            _directory?.Dispose();
            _directory = null;
            if (!string.IsNullOrWhiteSpace(_path))
                BenchmarkHelpers.DeleteDirectory(_path);
            _path = string.Empty;
        }
    }

    [GlobalCleanup]
    public void WriteEvidence()
    {
        string? output = Environment.GetEnvironmentVariable("LEANCORPUS_SHAPE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        double elapsedSeconds = _elapsedTimestampTicks / (double)Stopwatch.Frequency;
        var evidence = new
        {
            coordinateSystem = "XY",
            shapeKind = ShapeKind,
            requestedVertexCount = VertexCount,
            actualInputVertexCount = _actualInputVertexCount,
            primitiveCount = _primitiveCount,
            rawPrimitiveValueBytesPerDocument = _primitiveCount * 28L,
            packedBytesPerDocument = _packedBytes,
            elapsedMillisecondsObserved = elapsedSeconds * 1_000,
            documentsPerSecondObserved = elapsedSeconds <= 0 ? 0 : 1 / elapsedSeconds,
            ramAccounting = new
            {
                shapePreparationEstimateBytes = _primitiveCount * 96L,
                shapePreparationEstimateReleasedAfterAppend = true,
                packedBkdBufferCapacityIncludedInDwptEstimate = true,
                allocatedBytesPerOperation = "BenchmarkDotNet MemoryDiagnoser",
            },
            allocations = "BenchmarkDotNet MemoryDiagnoser report",
            elapsedTime = "BenchmarkDotNet report",
            flushCost = "Included in IndexShapeAndFlush",
        };
        string name = $"shape-index-{ShapeKind.ToLowerInvariant()}-{VertexCount.ToString(CultureInfo.InvariantCulture)}.json";
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class ShapeRelationBenchmarks
{
    public static IEnumerable<SpatialRelation> Relations
        => ShapeBenchmarkParameters.ReadRelations("LEANCORPUS_SHAPE_RELATIONS");

    public static IEnumerable<string> QueryKinds
        => ShapeBenchmarkParameters.ReadStrings("LEANCORPUS_SHAPE_QUERY_KINDS", ["Rectangle", "Circle", "Polygon", "ComplexPolygon"]);

    public static IEnumerable<double> Selectivities
        => ShapeBenchmarkParameters.ReadDecimals("LEANCORPUS_SHAPE_SELECTIVITIES", [0.001, 0.01, 0.1, 0.5]);

    [ParamsSource(nameof(Relations))]
    public SpatialRelation Relation { get; set; }

    [ParamsSource(nameof(QueryKinds))]
    public string QueryKind { get; set; } = "Rectangle";

    [ParamsSource(nameof(Selectivities))]
    public double RequestedSelectivity { get; set; }

    public int DocumentCount { get; private set; }

    private string _path = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _searcher;
    private XYShapeQuery? _query;
    private ActivityListener? _listener;
    private readonly Dictionary<string, string?> _latestTraversal = new(StringComparer.Ordinal);
    private int _hitCount;

    [GlobalSetup]
    public void Setup()
    {
        DocumentCount = ReadDocumentCount();
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "Rowles.LeanCorpus",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = CapturePackedBkdActivity,
        };
        ActivitySource.AddActivityListener(_listener);

        _path = BenchmarkHelpers.CreateTempDirectory("lc-shape-relations");
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 64,
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = false,
        }))
        {
            for (int documentId = 0; documentId < DocumentCount; documentId++)
            {
                var document = new LeanDocument();
                float minX = documentId * 2f;
                document.Add(new XYShapeField("shape", new XYRectangle(minX, 0, minX + 1, 1)));
                writer.AddDocument(document);
            }
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }

        _directory = new MMapDirectory(_path);
        _searcher = new IndexSearcher(_directory, new IndexSearcherConfig { ParallelSearch = false });
        _query = CreateQuery();
        _hitCount = RunSearch();
    }

    [Benchmark(Description = "XY shape relation over Packed BKD")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SearchShapeRelation()
        => RunSearch();

    [GlobalCleanup]
    public void Cleanup()
    {
        WriteEvidence();
        _searcher?.Dispose();
        _searcher = null;
        _directory?.Dispose();
        _directory = null;
        _listener?.Dispose();
        _listener = null;
        if (!string.IsNullOrWhiteSpace(_path))
            BenchmarkHelpers.DeleteDirectory(_path);
        _path = string.Empty;
    }

    private int RunSearch()
        => _searcher!.Search(_query!, DocumentCount, TestContextCancellationToken).TotalHits;

    private XYShapeQuery CreateQuery()
    {
        int selected = Math.Max(1, (int)Math.Round(DocumentCount * RequestedSelectivity, MidpointRounding.AwayFromZero));
        float minimumX = 0;
        float maximumX = ((selected - 1) * 2f) + 1f;
        float centreX = (maximumX + minimumX) / 2;
        float radius = Math.Max(0.5f, (maximumX - minimumX) / 2);
        return new XYShapeQuery("shape", Relation, QueryKind switch
        {
            "Rectangle" => new XYRectangle(minimumX, -1, maximumX, 2),
            "Circle" => new XYCircle(centreX, 0.5f, radius),
            "Polygon" => new XYPolygon(
            [
                new XYPoint(minimumX, -1), new XYPoint(maximumX, -1),
                new XYPoint(maximumX, 2), new XYPoint(minimumX, 2),
            ]),
            "ComplexPolygon" => ShapeGeometryFactory.CreateQueryPolygon(centreX, 0.5f, radius, 64),
            _ => throw new InvalidOperationException($"Unknown shape query kind '{QueryKind}'."),
        });
    }

    private void CapturePackedBkdActivity(Activity activity)
    {
        if (activity.OperationName != "leancorpus.index.packed_bkd.intersect")
            return;
        _latestTraversal.Clear();
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
            _latestTraversal[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
    }

    private void WriteEvidence()
    {
        string? output = Environment.GetEnvironmentVariable("LEANCORPUS_SHAPE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        var evidence = new
        {
            coordinateSystem = "XY",
            documentCount = DocumentCount,
            relation = Relation.ToString(),
            queryKind = QueryKind,
            requestedSelectivity = RequestedSelectivity,
            observedHitCount = _hitCount,
            allocations = "BenchmarkDotNet MemoryDiagnoser report",
            elapsedTime = "BenchmarkDotNet report",
            packedBkdTraversal = new Dictionary<string, string?>(_latestTraversal, StringComparer.Ordinal),
        };
        string selectivity = RequestedSelectivity.ToString("P1", CultureInfo.InvariantCulture).Replace('%', 'p');
        string name = $"shape-query-{Relation.ToString().ToLowerInvariant()}-{QueryKind.ToLowerInvariant()}-{selectivity}.json";
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int ReadDocumentCount()
    {
        string? configured = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        return int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0
            ? count
            : 1_000;
    }

    private static CancellationToken TestContextCancellationToken => CancellationToken.None;
}

internal static class ShapeBenchmarkParameters
{
    internal static int[] ReadIntegers(string environmentVariable, int[] defaults)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return defaults;
        int[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : throw new InvalidOperationException($"Invalid positive integer '{value}' in {environmentVariable}."))
            .Distinct()
            .ToArray();
        return values.Length > 0 ? values : throw new InvalidOperationException($"{environmentVariable} must contain at least one value.");
    }

    internal static string[] ReadStrings(string environmentVariable, string[] defaults)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return defaults;
        string[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length > 0 ? values : throw new InvalidOperationException($"{environmentVariable} must contain at least one value.");
    }

    internal static double[] ReadDecimals(string environmentVariable, double[] defaults)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return defaults;
        double[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && double.IsFinite(parsed) && parsed > 0 && parsed <= 1
                ? parsed
                : throw new InvalidOperationException($"Invalid selectivity '{value}' in {environmentVariable}."))
            .Distinct()
            .ToArray();
        return values.Length > 0 ? values : throw new InvalidOperationException($"{environmentVariable} must contain at least one value.");
    }

    internal static SpatialRelation[] ReadRelations(string environmentVariable)
    {
        string? configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return Enum.GetValues<SpatialRelation>();
        SpatialRelation[] values = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Enum.TryParse(value, ignoreCase: true, out SpatialRelation relation) && Enum.IsDefined(relation)
                ? relation
                : throw new InvalidOperationException($"Invalid spatial relation '{value}' in {environmentVariable}."))
            .Distinct()
            .ToArray();
        return values.Length > 0 ? values : throw new InvalidOperationException($"{environmentVariable} must contain at least one value.");
    }
}

internal static class ShapeGeometryFactory
{
    internal static int CountVertices(IXYGeometry geometry)
        => geometry switch
        {
            XYPoint => 1,
            XYRectangle => 4,
            XYLineString line => line.Points.Count,
            XYPolygon polygon => checked(polygon.Shell.Count + polygon.Holes.Sum(static hole => hole.Count)),
            XYGeometryCollection collection => collection.Geometries.Sum(CountVertices),
            _ => throw new ArgumentException("Unsupported benchmark geometry.", nameof(geometry)),
        };

    internal static IXYGeometry CreateIndexedGeometry(string shapeKind, int vertexCount)
    {
        if (vertexCount < 4)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        return shapeKind switch
        {
            "Convex" => new XYPolygon(CreateRing(vertexCount, 0, 0, 100, concave: false)),
            "Concave" => new XYPolygon(CreateRing(vertexCount, 0, 0, 100, concave: true)),
            "OneHole" => CreatePolygonWithHoles(vertexCount, holeCount: 1),
            "ManyHoles" => CreatePolygonWithHoles(vertexCount, holeCount: Math.Clamp(vertexCount / 8, 2, 16)),
            _ => throw new InvalidOperationException($"Unknown indexed shape kind '{shapeKind}'."),
        };
    }

    internal static XYPolygon CreateQueryPolygon(float centreX, float centreY, float radius, int vertexCount)
        => new(CreateRing(vertexCount, centreX, centreY, radius, concave: true));

    private static XYPolygon CreatePolygonWithHoles(int requestedVertices, int holeCount)
    {
        int shellCount = Math.Max(4, requestedVertices - (holeCount * 4));
        List<XYPoint> shell = CreateRing(shellCount, 0, 0, 100, concave: false);
        var holes = new List<IEnumerable<XYPoint>>(holeCount);
        for (int hole = 0; hole < holeCount; hole++)
        {
            double angle = 2 * Math.PI * hole / holeCount;
            float centreX = (float)(45 * Math.Cos(angle));
            float centreY = (float)(45 * Math.Sin(angle));
            holes.Add(
            [
                new XYPoint(centreX - 2, centreY - 2),
                new XYPoint(centreX - 2, centreY + 2),
                new XYPoint(centreX + 2, centreY + 2),
                new XYPoint(centreX + 2, centreY - 2),
            ]);
        }
        return new XYPolygon(shell, holes);
    }

    private static List<XYPoint> CreateRing(int count, float centreX, float centreY, float radius, bool concave)
    {
        var points = new List<XYPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = (2 * Math.PI * i) / count;
            float currentRadius = concave && (i & 1) != 0 ? radius * 0.65f : radius;
            points.Add(new XYPoint(
                centreX + (float)(Math.Cos(angle) * currentRadius),
                centreY + (float)(Math.Sin(angle) * currentRadius)));
        }
        return points;
    }
}
