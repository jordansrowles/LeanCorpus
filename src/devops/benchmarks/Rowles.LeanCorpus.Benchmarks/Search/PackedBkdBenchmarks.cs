using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge;
using Rowles.LeanCorpus.Codecs.PackedBkd;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures the complete Packed BKD writer and bounded reader traversal rather
/// than only the DWPT field buffer. Datasets cover the Sprint 1 cardinalities
/// and representative spatial distributions.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class PackedBkdBenchmarks
{
    private const float SelectiveRangeMinimum = -100;
    private const float SelectiveRangeMaximum = 100;

    [Params(10_000, 100_000, 1_000_000)]
    public int PointCount { get; set; }

    [Params(
        PackedBkdDistribution.Uniform,
        PackedBkdDistribution.Clustered,
        PackedBkdDistribution.Identical,
        PackedBkdDistribution.LineLike)]
    public PackedBkdDistribution Distribution { get; set; }

    private byte[] _packedValues = [];
    private int[] _documentIds = [];
    private string _queryDirectory = string.Empty;
    private string _queryPath = string.Empty;
    private PackedBkdReader? _reader;
    private string? _buildPath;
    private string? _buildDirectory;
    private long _queryOutputBytes;
    private double _queryBytesPerPoint;
    private QueryVisitor _visitor;

    [GlobalSetup]
    public void Setup()
    {
        _packedValues = new byte[checked(PointCount * 8)];
        _documentIds = new int[PointCount];
        var random = BenchmarkDeterministicRandom.Create($"benchmark/packed-bkd/{Distribution}");
        Span<byte> packed = stackalloc byte[8];
        for (int point = 0; point < PointCount; point++)
        {
            (float x, float y) = CreatePoint(point, random);
            XYEncodingUtils.Encode(x, packed[..4]);
            XYEncodingUtils.Encode(y, packed[4..]);
            packed.CopyTo(_packedValues.AsSpan(point * 8, 8));
            _documentIds[point] = point / 2;
        }

        _queryDirectory = BenchmarkHelpers.CreateTempDirectory("lc-packed-bkd-query");
        _queryPath = Path.Combine(_queryDirectory, "query.pbkd");
        using (var buffer = CreateBuffer())
        {
            PackedBkdWriter.Write(
                _queryPath,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(
                    MemoryBudgetBytes: 128L * 1024 * 1024,
                    SpillDirectory: _queryDirectory));
        }

        _reader = PackedBkdReader.Open(_queryPath);
        PackedBkdFieldMetadata metadata = _reader.GetFieldMetadata("location");
        if (metadata.PointCount != PointCount)
            throw new InvalidOperationException($"Packed BKD query index contains {metadata.PointCount} points; expected {PointCount}.");

        _queryOutputBytes = new FileInfo(_queryPath).Length;
        _queryBytesPerPoint = _queryOutputBytes / (double)metadata.PointCount;
        if (_queryOutputBytes <= 0 || !double.IsFinite(_queryBytesPerPoint) || _queryBytesPerPoint <= 0d)
            throw new InvalidOperationException("Packed BKD post-build size metadata is invalid.");

        _visitor = new QueryVisitor(SelectiveRangeMinimum, SelectiveRangeMaximum);
        ValidateSelectiveRangeResults();
        RecordObserverEvidence();
        RecordBval12MetricAndReaderEvidence(metadata);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        _reader = null;
        if (!string.IsNullOrWhiteSpace(_queryDirectory))
            BenchmarkHelpers.DeleteDirectory(_queryDirectory);
        _queryDirectory = string.Empty;
        _queryPath = string.Empty;
    }

    [IterationSetup(Target = nameof(LeanCorpus_PackedBkd_Build))]
    public void SetupBuild()
        => PrepareBuildDirectory();

    [IterationSetup(Target = nameof(LeanCorpus_PackedBkd_SpillBuild))]
    public void SetupSpillBuild()
        => PrepareBuildDirectory();

    [IterationSetup(Target = nameof(LeanCorpus_PackedBkd_WarmSelectiveIntersect))]
    public void SetupWarmSelectiveIntersect()
    {
        _visitor.Reset();
        _reader!.Intersect("location", ref _visitor);
    }

    private void PrepareBuildDirectory()
    {
        _buildDirectory = BenchmarkHelpers.CreateTempDirectory("lc-packed-bkd-build");
        _buildPath = Path.Combine(_buildDirectory, "packed.pbkd");
    }

    [IterationCleanup(Target = nameof(LeanCorpus_PackedBkd_Build))]
    public void CleanupBuild()
        => CleanupBuildDirectory();

    [IterationCleanup(Target = nameof(LeanCorpus_PackedBkd_SpillBuild))]
    public void CleanupSpillBuild()
        => CleanupBuildDirectory();

    private void CleanupBuildDirectory()
    {
        if (_buildPath is not null)
            BenchmarkHelpers.DeleteDirectory(_buildDirectory!);
        _buildPath = null;
        _buildDirectory = null;
    }

    [Benchmark(Baseline = true, Description = "Packed BKD full writer")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_Build()
        => Build(forceSpill: false);

    [Benchmark(Description = "Packed BKD forced spill writer")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_SpillBuild()
        => Build(forceSpill: true);

    [Benchmark(Description = "Packed BKD warm selective range traversal")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_WarmSelectiveIntersect()
    {
        _visitor.Reset();
        _reader!.Intersect("location", ref _visitor);
        return _visitor.Count;
    }

    [Benchmark(Description = "Packed BKD new reader with warm OS page cache and selective traversal")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_NewReaderWarmPageCacheSelectiveIntersect()
    {
        using var reader = PackedBkdReader.Open(_queryPath);
        _visitor.Reset();
        reader.Intersect("location", ref _visitor);
        return _visitor.Count;
    }

    [Benchmark(Description = "Packed BKD tail-directory open")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_Open()
    {
        using var reader = PackedBkdReader.Open(_queryPath);
        return reader.FieldNames.Count;
    }

    private int Build(bool forceSpill)
    {
        string path = _buildPath ?? throw new InvalidOperationException("The build path was not prepared.");
        using var buffer = CreateBuffer();
        PackedBkdWriter.Write(
            path,
            new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
            new PackedBkdBuildOptions(
                MemoryBudgetBytes: forceSpill ? 64L * 1024 * 1024 : 512L * 1024 * 1024,
                SpillDirectory: Path.GetDirectoryName(path),
                ForceSpill: forceSpill));
        return PointCount;
    }

    private PackedBkdFieldBuffer CreateBuffer()
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 512));
        for (int point = 0; point < PointCount; point++)
            buffer.Append(_packedValues.AsSpan(point * 8, 8), _documentIds[point]);
        return buffer;
    }

    private void ValidateSelectiveRangeResults()
    {
        var expectedDocumentIds = new HashSet<int>();
        for (int point = 0; point < PointCount; point++)
        {
            ReadOnlySpan<byte> packedValue = _packedValues.AsSpan(point * 8, 8);
            float x = XYEncodingUtils.Decode(packedValue[..4]);
            float y = XYEncodingUtils.Decode(packedValue[4..8]);
            if (x >= SelectiveRangeMinimum && x <= SelectiveRangeMaximum
                && y >= SelectiveRangeMinimum && y <= SelectiveRangeMaximum)
            {
                expectedDocumentIds.Add(_documentIds[point]);
            }
        }

        var actualDocumentIds = new HashSet<int>();
        var verificationVisitor = new QueryVisitor(
            SelectiveRangeMinimum,
            SelectiveRangeMaximum,
            actualDocumentIds);
        if (!_reader!.Intersect("location", ref verificationVisitor))
            throw new InvalidOperationException("Packed BKD selective XY range field is missing.");

        if (!actualDocumentIds.SetEquals(expectedDocumentIds))
        {
            string[] missing = [.. expectedDocumentIds.Except(actualDocumentIds).Order().Take(8)
                .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            string[] unexpected = [.. actualDocumentIds.Except(expectedDocumentIds).Order().Take(8)
                .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            throw new InvalidOperationException(
                $"Packed BKD selective XY range returned {actualDocumentIds.Count} document IDs; " +
                $"expected {expectedDocumentIds.Count}. Missing [{string.Join(", ", missing)}], " +
                $"unexpected [{string.Join(", ", unexpected)}].");
        }
    }

    private void RecordBval12MetricAndReaderEvidence(PackedBkdFieldMetadata metadata)
    {
        const string fixtureRelativePath =
            ".config/devops/benchmark-correctness-fixtures/bval-12-packed-bkd-metric-and-warm-reader.json";
        const string fixtureId = "bval-12-packed-bkd-metric-and-warm-reader";
        string fixturePath = Path.Combine(
            BenchmarkHelpers.RepositoryRoot,
            fixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] fixtureBytes = File.ReadAllBytes(fixturePath);
        string fixtureSha256 = Convert.ToHexString(SHA256.HashData(fixtureBytes)).ToLowerInvariant();
        using JsonDocument fixture = JsonDocument.Parse(fixtureBytes);
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(), fixtureId, StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean()
            || !string.Equals(root.GetProperty("domain").GetString(), "packed-bkd-metric-and-reader-warmth", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-12 fixture metadata is invalid: {fixtureRelativePath}.");
        }

        JsonElement input = root.GetProperty("input");
        JsonElement completedIndex = input.GetProperty("completedIndex");
        JsonElement fixtureReaderProfile = input.GetProperty("readerProfile");
        JsonElement expected = root.GetProperty("expected");
        long fixtureOutputBytes = completedIndex.GetProperty("bytes").GetInt64();
        int fixturePointCount = completedIndex.GetProperty("pointCount").GetInt32();
        double fixtureBytesPerPoint = fixtureOutputBytes / (double)fixturePointCount;
        string readerProfileLabel = fixtureReaderProfile.GetProperty("name").GetString() ?? string.Empty;
        string expectedReaderProfileLabel = expected.GetProperty("readerProfileLabel").GetString() ?? string.Empty;
        string measurementRegion = expected.GetProperty("bytesPerPointMeasurementRegion").GetString() ?? string.Empty;
        string[] timedOperations = input.GetProperty("timedOperations").EnumerateArray()
            .Select(static operation => operation.GetString() ?? string.Empty)
            .ToArray();

        if (fixtureOutputBytes <= 0
            || fixturePointCount <= 0
            || Math.Abs(fixtureBytesPerPoint - expected.GetProperty("bytesPerPoint").GetDouble()) > 1e-12
            || measurementRegion != "post-build-report-metric"
            || expected.GetProperty("fileInfoLengthLookupInTimedMethod").GetBoolean()
            || readerProfileLabel != expectedReaderProfileLabel
            || !fixtureReaderProfile.GetProperty("readerInstanceNew").GetBoolean()
            || fixtureReaderProfile.GetProperty("osPageCacheProvenCold").GetBoolean()
            || expected.GetProperty("diskColdClaimAllowed").GetBoolean()
            || !timedOperations.SequenceEqual(["reader-open", "point-query"], StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-12 fixture contract is invalid: {fixtureRelativePath}.");
        }

        if (metadata.PointCount != PointCount)
            throw new InvalidOperationException("BVAL-12 post-build metric point count does not match the completed Packed BKD index.");

        string? artifactDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(artifactDirectory))
            throw new InvalidOperationException("LEANCORPUS_ARTIFACT_DIR is required for BVAL-12 evidence output.");

        string outputDirectory = Path.Combine(artifactDirectory, "packed-bkd");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, $"{PointCount}-{Distribution}-bval-12.json");
        string temporaryPath = outputPath + $".{Guid.NewGuid():N}.tmp";
        var evidence = new
        {
            FixtureId = fixtureId,
            FixtureRelativePath = fixtureRelativePath,
            FixtureSha256 = fixtureSha256,
            FixtureNegativeControl = new
            {
                OutputBytes = fixtureOutputBytes,
                PointCount = fixturePointCount,
                BytesPerPoint = fixtureBytesPerPoint,
                MeasurementRegion = measurementRegion
            },
            CompletedBuildMetric = new
            {
                PointCount = metadata.PointCount,
                OutputBytes = _queryOutputBytes,
                BytesPerPoint = _queryBytesPerPoint,
                MeasurementRegion = "post-build-report-metric",
                FileInfoLengthLookupInTimedMethod = false
            },
            ReaderProfile = new
            {
                Name = readerProfileLabel,
                ReaderInstanceNew = true,
                OsPageCacheProvenCold = false,
                DiskColdClaimAllowed = false,
                TimedOperation = "new-reader-warm-page-cache-selective-intersect"
            }
        };

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(evidence, JsonOptions));
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void RecordObserverEvidence()
    {
        var metadata = _reader!.GetFieldMetadata("location");

        _visitor.Reset();
        _reader.Intersect("location", ref _visitor, out var warmStats);
        WriteObserverEvidence("warm-selective-intersect", metadata, warmStats, _visitor.Count);

        using var newReader = PackedBkdReader.Open(_queryPath);
        _visitor.Reset();
        newReader.Intersect("location", ref _visitor, out var newReaderStats);
        WriteObserverEvidence("new-reader-warm-page-cache-selective-intersect", metadata, newReaderStats, _visitor.Count);
    }

    private void WriteObserverEvidence(
        string operation,
        PackedBkdFieldMetadata metadata,
        PackedBkdTraversalStats stats,
        int matches)
    {
        string? evidenceDirectory = Environment.GetEnvironmentVariable(
            "LEANCORPUS_PACKED_BKD_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
            return;

        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new ObserverEvidence(
            PointCount,
            Distribution.ToString(),
            operation,
            metadata.LeafCount,
            metadata.PointCount,
            _queryOutputBytes,
            _queryBytesPerPoint,
            matches,
            stats.CellsVisited,
            stats.CellsPruned,
            stats.LeavesVisited,
            stats.LeavesSemanticallyValidated,
            stats.PackedValuesDecoded,
            stats.DocumentsVisited,
            stats.PeakLeafScratch);
        string fileName = $"{PointCount}-{Distribution}-{operation}-{Environment.ProcessId}-{Guid.NewGuid():N}.json";
        File.WriteAllText(
            Path.Combine(evidenceDirectory, fileName),
            JsonSerializer.Serialize(evidence, JsonOptions));
    }

    private (float X, float Y) CreatePoint(int point, DataForgePrng random)
    {
        return Distribution switch
        {
            PackedBkdDistribution.Uniform => (
                random.NextSingle01() * 20_000 - 10_000,
                random.NextSingle01() * 20_000 - 10_000),
            PackedBkdDistribution.Clustered => (
                2_000 + (float)(random.NextDouble01() * 20 - 10),
                -3_000 + (float)(random.NextDouble01() * 20 - 10)),
            PackedBkdDistribution.Identical => (7, 7),
            PackedBkdDistribution.LineLike => (
                -10_000 + point * (20_000f / Math.Max(1, PointCount - 1)),
                -10_000 + point * (20_000f / Math.Max(1, PointCount - 1)) + (point % 3) * 0.01f),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public enum PackedBkdDistribution
    {
        Uniform,
        Clustered,
        Identical,
        LineLike,
    }

    private struct QueryVisitor(float minimum, float maximum, HashSet<int>? matchingDocumentIds = null)
        : IPackedBkdIntersectVisitor
    {
        private readonly byte[] _minimum = Pack(minimum, minimum);
        private readonly byte[] _maximum = Pack(maximum, maximum);
        private readonly HashSet<int>? _matchingDocumentIds = matchingDocumentIds;

        internal int Count { get; private set; }

        internal void Reset() => Count = 0;

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        {
            if (IsOutside(minimum, maximum))
                return PackedBkdCellRelation.Outside;
            return IsInside(minimum, maximum)
                ? PackedBkdCellRelation.Inside
                : PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId)
        {
            Count++;
            _matchingDocumentIds?.Add(docId);
        }

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (IsValueInRange(packedValue))
            {
                Count++;
                _matchingDocumentIds?.Add(docId);
            }
        }

        private bool IsOutside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => maximum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) < 0
                || minimum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) > 0
                || maximum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) < 0
                || minimum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) > 0;

        private bool IsInside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => minimum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && maximum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && minimum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && maximum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private bool IsValueInRange(ReadOnlySpan<byte> packedValue)
            => packedValue[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && packedValue[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && packedValue[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && packedValue[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private static byte[] Pack(float x, float y)
        {
            byte[] packed = new byte[8];
            XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
            return packed;
        }
    }

    private sealed record ObserverEvidence(
        int PointCount,
        string Distribution,
        string Operation,
        int LeafCount,
        long PointTotal,
        long OutputBytes,
        double OutputBytesPerPoint,
        int Matches,
        long CellsVisited,
        long CellsPruned,
        long LeavesVisited,
        long LeavesSemanticallyValidated,
        long PackedValuesDecoded,
        long DocumentsVisited,
        int PeakLeafScratch);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
