using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Aggregations;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class ShapeDocValuesBenchmarks
{
    public static IEnumerable<int> PrimitiveCounts
        => ShapeBenchmarkParameters.ReadIntegers(
            "LEANCORPUS_SHAPEDV_PRIMITIVE_COUNTS",
            [1, 16, 100, 1_000, 10_000]);

    [ParamsSource(nameof(PrimitiveCounts))]
    public int PrimitiveCount { get; set; }

    private string _directoryPath = string.Empty;
    private string _readPath = string.Empty;
    private string _writePath = string.Empty;
    private IXYGeometry _geometry = new XYPoint(0, 0);
    private PreparedSpatialDocument? _prepared;
    private ShapeDocValuesFieldBuffer? _fieldBuffer;
    private IReadOnlyDictionary<string, ShapeDocValuesFieldBuffer>? _fields;
    private ShapeDocValuesReader? _reader;
    private long _preparedOwnedCapacityBytes;
    private long _shapeDocValuesBytes;
    private IndexFootprint _withShapeDocValues;
    private IndexFootprint _withoutShapeDocValues;

    [GlobalSetup]
    public void Setup()
    {
        _geometry = CreateGeometry(PrimitiveCount);
        _directoryPath = BenchmarkHelpers.CreateTempDirectory("lc-shape-docvalues");
        string readDirectory = Path.Combine(_directoryPath, "read");
        string writeDirectory = Path.Combine(_directoryPath, "write");
        Directory.CreateDirectory(readDirectory);
        Directory.CreateDirectory(writeDirectory);
        _readPath = Path.Combine(readDirectory, "seg_0.dvg");
        _writePath = Path.Combine(writeDirectory, "seg_0.dvg");

        _prepared = new PreparedSpatialDocument();
        EncodedShapePrimitiveSink sink = _prepared.CreateSink(SpatialFieldKind.XYShape);
        ShapeTessellator.TessellateXY(_geometry, valueOrdinal: 0, output: sink);
        if (sink.Count != PrimitiveCount)
            throw new InvalidDataException(
                $"Shape DocValues benchmark expected {PrimitiveCount} primitives but prepared {sink.Count}.");
        _prepared.AddValue(
            fieldIndex: 0,
            fieldName: "shape",
            fieldKind: SpatialFieldKind.XYShape,
            valueOrdinal: 0,
            storeDocValues: true,
            sink: sink);
        PreparedShapeValue value = _prepared.Values[0];
        _preparedOwnedCapacityBytes = _prepared.AllocatedBytes;

        _fieldBuffer = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
        _fieldBuffer.AppendValue(0, value.ValueOrdinal, _prepared.GetPackedPrimitives(value).Span);
        _fields = new Dictionary<string, ShapeDocValuesFieldBuffer>(StringComparer.Ordinal)
        {
            ["shape"] = _fieldBuffer,
        };

        ShapeDocValuesWriter.Write(_readPath, maxDoc: 1, _fields);
        _shapeDocValuesBytes = new FileInfo(_readPath).Length;
        _reader = ShapeDocValuesReader.Open(_readPath);
        _withShapeDocValues = CreateIndexFootprint(storeDocValues: true);
        _withoutShapeDocValues = CreateIndexFootprint(storeDocValues: false);
    }

    [Benchmark(Baseline = true, Description = "Shape DocValues v1 serialise")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SerialiseShapeDocValues()
    {
        ShapeDocValuesWriter.Write(_writePath, maxDoc: 1, _fields!);
        return PrimitiveCount;
    }

    [Benchmark(Description = "Shape DocValues metadata-only record read")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ReadMetadataOnly()
        => _reader!.TryGetRecordMetadata("shape", 0, out ShapeDocValuesRecordMetadata metadata)
            ? checked((int)metadata.PrimitiveCount)
            : 0;

    [Benchmark(Description = "Shape DocValues component-tree traversal")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int TraverseComponentTree()
        => _reader!.VisitPrimitives("shape", 0, static _ => { });

    [GlobalCleanup]
    public void Cleanup()
    {
        WriteEvidence();
        _reader?.Dispose();
        _reader = null;
        _fieldBuffer?.Dispose();
        _fieldBuffer = null;
        _prepared?.Dispose();
        _prepared = null;
        if (!string.IsNullOrWhiteSpace(_directoryPath))
            BenchmarkHelpers.DeleteDirectory(_directoryPath);
        _directoryPath = string.Empty;
    }

    private void WriteEvidence()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_SHAPE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;
        Directory.CreateDirectory(outputDirectory);
        var evidence = new
        {
            coordinateSystem = "XY",
            recordCount = 1,
            primitiveCount = PrimitiveCount,
            rawPrimitiveBytes = checked(PrimitiveCount * 28L),
            preparedOwnedCapacityBytes = _preparedOwnedCapacityBytes,
            preparedOwnedCapacityBytesPerPrimitive = _preparedOwnedCapacityBytes / (double)PrimitiveCount,
            shapeDocValuesBytesPerDocument = _shapeDocValuesBytes,
            shapeDocValuesBytesPerPrimitive = _shapeDocValuesBytes / (double)PrimitiveCount,
            withShapeDocValues = _withShapeDocValues,
            withoutShapeDocValues = _withoutShapeDocValues,
            indexSizeDeltaBytes = _withShapeDocValues.TotalBytes - _withoutShapeDocValues.TotalBytes,
            allocatedBytesForMetadataAndTraversal = "BenchmarkDotNet MemoryDiagnoser report",
            serialiseTime = "BenchmarkDotNet report",
            metadataReadTime = "BenchmarkDotNet report",
            componentTraversalTime = "BenchmarkDotNet report",
        };
        string name = $"shape-docvalues-{PrimitiveCount.ToString(CultureInfo.InvariantCulture)}-primitives.json";
        File.WriteAllText(
            Path.Combine(outputDirectory, name),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private IndexFootprint CreateIndexFootprint(bool storeDocValues)
    {
        string path = BenchmarkHelpers.CreateTempDirectory(
            storeDocValues ? "lc-shape-dv-on" : "lc-shape-dv-off");
        try
        {
            using (var directory = new MMapDirectory(path))
            using (var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                BKDMaxLeafSize = 64,
                MergePolicy = NoMergePolicy.Instance,
                UseCompoundFile = false,
            }))
            {
                var document = new LeanDocument();
                document.Add(new XYShapeField("shape", _geometry, storeDocValues: storeDocValues));
                writer.AddDocument(document);
                writer.Commit();
            }

            FileInfo[] files = Directory.GetFiles(path)
                .Select(static file => new FileInfo(file))
                .Where(static file => file.Extension is ".pbkd" or ".dvg")
                .ToArray();
            return new IndexFootprint(
                files.Where(static file => file.Extension == ".pbkd").Sum(static file => file.Length),
                files.Where(static file => file.Extension == ".dvg").Sum(static file => file.Length));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(path);
        }
    }

    private static IXYGeometry CreateGeometry(int primitiveCount)
        => primitiveCount == 1
            ? new XYPolygon(
            [
                new XYPoint(-100, -100),
                new XYPoint(100, -100),
                new XYPoint(0, 100),
            ])
            : ShapeGeometryFactory.CreateIndexedGeometry("Convex", checked(primitiveCount + 2));

    private readonly record struct IndexFootprint(long PackedBkdBytes, long ShapeDocValuesBytes)
    {
        internal long TotalBytes => PackedBkdBytes + ShapeDocValuesBytes;
    }
}

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class ShapeDocValuesMergeBenchmarks
{
    public static IEnumerable<int> PrimitiveCounts => ShapeDocValuesBenchmarks.PrimitiveCounts;

    [ParamsSource(nameof(PrimitiveCounts))]
    public int PrimitiveCount { get; set; }

    private IXYGeometry _geometry = new XYPoint(0, 0);
    private string _path = string.Empty;
    private MMapDirectory? _directory;
    private IndexWriter? _writer;

    [GlobalSetup]
    public void Setup()
        => _geometry = PrimitiveCount == 1
            ? new XYPolygon([new XYPoint(-100, -100), new XYPoint(100, -100), new XYPoint(0, 100)])
            : ShapeGeometryFactory.CreateIndexedGeometry("Convex", checked(PrimitiveCount + 2));

    [IterationSetup(Target = nameof(ForceMergeShapeDocValues))]
    public void SetupSourceSegments()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("lc-shape-docvalues-merge");
        _directory = new MMapDirectory(_path);
        _writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 64,
            MaxBufferedDocs = 1,
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = false,
        });

        for (int i = 0; i < 2; i++)
        {
            var document = new LeanDocument();
            document.Add(new XYShapeField("shape", _geometry));
            _writer.AddDocument(document);
            _writer.Commit();
        }
    }

    [Benchmark(Description = "Merge copy of Shape DocValues records")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ForceMergeShapeDocValues()
    {
        _writer!.ForceMerge(1);
        _writer.Commit();
        return Directory.GetFiles(_path, "*.dvg").Sum(static file => new FileInfo(file).Length);
    }

    [IterationCleanup(Target = nameof(ForceMergeShapeDocValues))]
    public void CleanupSourceSegments()
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

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class ShapeDocValuesCopyBenchmarks
{
    public static IEnumerable<int> PrimitiveCounts => ShapeDocValuesBenchmarks.PrimitiveCounts;

    [ParamsSource(nameof(PrimitiveCounts))]
    public int PrimitiveCount { get; set; }

    private string _directoryPath = string.Empty;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private ShapeDocValuesReader? _sourceReader;
    private long _sourceFileBytes;
    private long _recordBytesPerMerge;

    [GlobalSetup]
    public void Setup()
    {
        _directoryPath = BenchmarkHelpers.CreateTempDirectory("lc-shape-docvalues-copy");
        _sourcePath = Path.Combine(_directoryPath, "source.dvg");
        _destinationPath = Path.Combine(_directoryPath, "destination.dvg");
        IXYGeometry geometry = PrimitiveCount == 1
            ? new XYPolygon([new XYPoint(-100, -100), new XYPoint(100, -100), new XYPoint(0, 100)])
            : ShapeGeometryFactory.CreateIndexedGeometry("Convex", checked(PrimitiveCount + 2));

        using (var prepared = new PreparedSpatialDocument())
        {
            EncodedShapePrimitiveSink sink = prepared.CreateSink(SpatialFieldKind.XYShape);
            ShapeTessellator.TessellateXY(geometry, valueOrdinal: 0, output: sink);
            if (sink.Count != PrimitiveCount)
                throw new InvalidDataException(
                    $"Shape DocValues copy benchmark expected {PrimitiveCount} primitives but prepared {sink.Count}.");
            prepared.AddValue(0, "shape", SpatialFieldKind.XYShape, 0, storeDocValues: true, sink);
            PreparedShapeValue value = prepared.Values[0];

            using var sourceField = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
            sourceField.AppendValue(0, value.ValueOrdinal, prepared.GetPackedPrimitives(value).Span);
            sourceField.AppendValue(1, value.ValueOrdinal, prepared.GetPackedPrimitives(value).Span);
            ShapeDocValuesWriter.Write(
                _sourcePath,
                maxDoc: 2,
                new Dictionary<string, ShapeDocValuesFieldBuffer>(StringComparer.Ordinal)
                {
                    ["shape"] = sourceField,
                });
        }

        _sourceFileBytes = new FileInfo(_sourcePath).Length;
        _sourceReader = ShapeDocValuesReader.Open(_sourcePath);
        for (int documentId = 0; documentId < 2; documentId++)
        {
            if (!_sourceReader.TryGetRecordMetadata("shape", documentId, out ShapeDocValuesRecordMetadata metadata))
                throw new InvalidDataException($"Shape DocValues copy source is missing document {documentId}.");
            _recordBytesPerMerge = checked(_recordBytesPerMerge + _sourceReader.ReadRecordBytes("shape", documentId).Length);
            if (metadata.PrimitiveCount != (uint)PrimitiveCount)
                throw new InvalidDataException("Shape DocValues copy source has an unexpected primitive count.");
        }
    }

    [Benchmark(Description = "Copy validated Shape DocValues records during merge")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long CopyShapeDocValuesRecords()
    {
        using var destinationField = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
        long copiedBytes = 0;
        for (int documentId = 0; documentId < 2; documentId++)
        {
            if (!_sourceReader!.TryGetRecordMetadata(
                    "shape", documentId, out ShapeDocValuesRecordMetadata metadata))
                throw new InvalidDataException($"Shape DocValues copy source is missing document {documentId}.");
            byte[] recordBytes = _sourceReader.ReadRecordBytes("shape", documentId);
            destinationField.AppendRawRecord(
                documentId,
                metadata.ValueCount,
                metadata.PrimitiveCount,
                recordBytes);
            copiedBytes = checked(copiedBytes + recordBytes.Length);
        }

        ShapeDocValuesWriter.Write(
            _destinationPath,
            maxDoc: 2,
            new Dictionary<string, ShapeDocValuesFieldBuffer>(StringComparer.Ordinal)
            {
                ["shape"] = destinationField,
            });
        return copiedBytes;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        WriteEvidence();
        _sourceReader?.Dispose();
        _sourceReader = null;
        if (!string.IsNullOrWhiteSpace(_directoryPath))
            BenchmarkHelpers.DeleteDirectory(_directoryPath);
        _directoryPath = string.Empty;
    }

    private void WriteEvidence()
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_SHAPE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;
        Directory.CreateDirectory(outputDirectory);
        var evidence = new
        {
            primitiveCountPerRecord = PrimitiveCount,
            recordCount = 2,
            sourceFileBytes = _sourceFileBytes,
            recordBytesCopiedPerOperation = _recordBytesPerMerge,
            copyThroughput = "BenchmarkDotNet report: bytes copied per operation divided by mean operation time",
        };
        string name = $"shape-docvalues-merge-copy-{PrimitiveCount.ToString(CultureInfo.InvariantCulture)}-primitives.json";
        File.WriteAllText(
            Path.Combine(outputDirectory, name),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class SpatialAggregationBenchmarks
{
    public static IEnumerable<int> DocumentCounts => ReadDocumentCounts();

    [ParamsSource(nameof(DocumentCounts))]
    public int DocumentCount { get; set; }

    private string _path = string.Empty;
    private MMapDirectory? _directory;
    private IndexSearcher? _searcher;
    private ISearchAggregationRequest[] _distanceOnly = [];
    private ISearchAggregationRequest[] _centroidOnly = [];
    private ISearchAggregationRequest[] _boundsOnly = [];
    private ISearchAggregationRequest[] _combined = [];

    [GlobalSetup]
    public void Setup()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("lc-spatial-aggregations");
        _directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = false,
        }))
        {
            for (int i = 0; i < DocumentCount; i++)
            {
                double latitude = ((i * 37) % 120) - 60;
                double longitude = ((i * 71) % 360) - 180;
                var document = new LeanDocument();
                document.Add(new NumericField("rank", i));
                document.Add(new GeoPointField("location", latitude, longitude));
                document.Add(new LatLonShapeField("shape", new GeoRectangle(-1, 179, 1, -179)));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        _searcher = new IndexSearcher(_directory, new IndexSearcherConfig { ParallelSearch = false });
        _distanceOnly =
        [
            new GeoDistanceAggregationRequest(
                "distance", "location", new GeoPoint(0, 0),
                [new GeoDistanceRange(0, 250_000), new GeoDistanceRange(250_000, null)]),
        ];
        _centroidOnly = [new GeoCentroidAggregationRequest("centre", "shape")];
        _boundsOnly = [new GeoBoundsAggregationRequest("bounds", "shape")];
        _combined =
        [
            new AggregationRequest("rank", "rank"),
            new GeoDistanceAggregationRequest(
                "distance", "location", new GeoPoint(0, 0),
                [new GeoDistanceRange(0, 250_000), new GeoDistanceRange(250_000, null)]),
            new GeoCentroidAggregationRequest("centre", "shape"),
            new GeoBoundsAggregationRequest("bounds", "shape"),
        ];
    }

    [Benchmark(Description = "Geo distance aggregation")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long GeoDistanceAggregation()
    {
        var (_, results) = _searcher!.SearchWithAggregations(
            new MatchAllDocsQuery(), 0, _distanceOnly, CancellationToken.None);
        return AssertCount<GeoDistanceAggregationResult>(results[0], static result => result.Buckets.Sum(static bucket => bucket.DocumentCount));
    }

    [Benchmark(Description = "Shape Geo centroid aggregation")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long GeoCentroidAggregation()
    {
        var (_, results) = _searcher!.SearchWithAggregations(
            new MatchAllDocsQuery(), 0, _centroidOnly, CancellationToken.None);
        return AssertCount<GeoCentroidAggregationResult>(results[0], static result => result.ContributingDocumentCount);
    }

    [Benchmark(Description = "Shape Geo wrapped bounds aggregation")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long GeoBoundsAggregation()
    {
        var (_, results) = _searcher!.SearchWithAggregations(
            new MatchAllDocsQuery(), 0, _boundsOnly, CancellationToken.None);
        return AssertCount<GeoBoundsAggregationResult>(results[0], static result => result.ContributingDocumentCount);
    }

    [Benchmark(Description = "One-pass numeric and spatial aggregations")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long HeterogeneousAggregations()
    {
        var (_, results) = _searcher!.SearchWithAggregations(
            new MatchAllDocsQuery(), 0, _combined, CancellationToken.None);
        long numeric = AssertCount<AggregationResult>(results[0], static result => result.Count);
        long distance = AssertCount<GeoDistanceAggregationResult>(results[1],
            static result => result.Buckets.Sum(static bucket => bucket.DocumentCount));
        long centroid = AssertCount<GeoCentroidAggregationResult>(results[2], static result => result.ContributingDocumentCount);
        long bounds = AssertCount<GeoBoundsAggregationResult>(results[3], static result => result.ContributingDocumentCount);
        return numeric + distance + centroid + bounds;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _searcher = null;
        _directory?.Dispose();
        _directory = null;
        if (!string.IsNullOrWhiteSpace(_path))
            BenchmarkHelpers.DeleteDirectory(_path);
        _path = string.Empty;
    }

    private static long AssertCount<TResult>(ISearchAggregationResult result, Func<TResult, long> getCount)
        where TResult : ISearchAggregationResult
        => result is TResult typed
            ? getCount(typed)
            : throw new InvalidDataException($"Expected benchmark result '{typeof(TResult).Name}', got '{result.GetType().Name}'.");

    private static int[] ReadDocumentCounts()
    {
        string? configured = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0)
            return [count];
        return [1_000];
    }
}

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(1)]
[IterationCount(3)]
public class SpatialUtilityBenchmarks
{
    public static IEnumerable<int> VertexCounts
        => ShapeBenchmarkParameters.ReadIntegers("LEANCORPUS_SPATIAL_UTILITY_VERTICES", [16, 128, 1_024]);

    [ParamsSource(nameof(VertexCounts))]
    public int VertexCount { get; set; }

    private XYLineString _xyLine = new([new XYPoint(0, 0), new XYPoint(1, 1)]);
    private GeoLineString _geoLine = new([new GeoPoint(0, 0), new GeoPoint(1, 1)]);
    private string _xyWkt = string.Empty;
    private string _geoWkt = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var xyPoints = new XYPoint[VertexCount];
        var geoPoints = new GeoPoint[VertexCount];
        for (int i = 0; i < VertexCount; i++)
        {
            double ratio = i / (double)(VertexCount - 1);
            double wave = Math.Sin(i * 0.17d);
            xyPoints[i] = new XYPoint(i, (float)wave);
            geoPoints[i] = new GeoPoint(wave * 20d, (ratio * 160d) - 80d);
        }
        _xyLine = new XYLineString(xyPoints);
        _geoLine = new GeoLineString(geoPoints);
        _xyWkt = WktWriter.Write(_xyLine);
        _geoWkt = WktWriter.Write(_geoLine);
    }

    [Benchmark(Description = "XY WKT parse")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ParseXYWkt()
        => ((XYLineString)WktReader.ParseXY(_xyWkt)).Points.Count;

    [Benchmark(Description = "Geo WKT parse")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ParseGeoWkt()
        => ((GeoLineString)WktReader.ParseGeo(_geoWkt)).Points.Count;

    [Benchmark(Description = "XY WKT write")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WriteXYWkt()
        => WktWriter.Write(_xyLine).Length;

    [Benchmark(Description = "Geo WKT write")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WriteGeoWkt()
        => WktWriter.Write(_geoLine).Length;

    [Benchmark(Description = "XY Douglas-Peucker simplification")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SimplifyXYLine()
        => XYSimplifier.Simplify(_xyLine, 0.25f).Points.Count;

    [Benchmark(Description = "Geo great-circle simplification")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SimplifyGeoLine()
        => GeoSimplifier.Simplify(_geoLine, 1_000).Points.Count;
}
