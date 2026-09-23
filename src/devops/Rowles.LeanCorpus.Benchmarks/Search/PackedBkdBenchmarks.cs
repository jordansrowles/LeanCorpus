using System.Runtime.CompilerServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
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
    private QueryVisitor _visitor;

    [GlobalSetup]
    public void Setup()
    {
        _packedValues = new byte[checked(PointCount * 8)];
        _documentIds = new int[PointCount];
        var random = new Random(42);
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
        _visitor = new QueryVisitor(-100, 100);
        RecordObserverEvidence();
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

    [Benchmark(Description = "Packed BKD cold open and first selective traversal")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_PackedBkd_ColdOpenSelectiveIntersect()
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

    [Benchmark(Description = "Packed BKD output bytes per point")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public double LeanCorpus_PackedBkd_OutputBytesPerPoint()
        => new FileInfo(_queryPath).Length / (double)PointCount;

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
        return checked((int)new FileInfo(path).Length);
    }

    private PackedBkdFieldBuffer CreateBuffer()
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 512));
        for (int point = 0; point < PointCount; point++)
            buffer.Append(_packedValues.AsSpan(point * 8, 8), _documentIds[point]);
        return buffer;
    }

    private void RecordObserverEvidence()
    {
        var metadata = _reader!.GetFieldMetadata("location");

        _visitor.Reset();
        _reader.Intersect("location", ref _visitor, out var warmStats);
        WriteObserverEvidence("warm-selective-intersect", metadata, warmStats, _visitor.Count);

        using var coldReader = PackedBkdReader.Open(_queryPath);
        _visitor.Reset();
        coldReader.Intersect("location", ref _visitor, out var coldStats);
        WriteObserverEvidence("cold-open-first-selective-intersect", metadata, coldStats, _visitor.Count);
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
        long outputBytes = new FileInfo(_queryPath).Length;
        var evidence = new ObserverEvidence(
            PointCount,
            Distribution.ToString(),
            operation,
            metadata.LeafCount,
            metadata.PointCount,
            outputBytes,
            outputBytes / (double)PointCount,
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

    private (float X, float Y) CreatePoint(int point, Random random)
    {
        return Distribution switch
        {
            PackedBkdDistribution.Uniform => (
                random.NextSingle() * 20_000 - 10_000,
                random.NextSingle() * 20_000 - 10_000),
            PackedBkdDistribution.Clustered => (
                2_000 + (float)(random.NextDouble() * 20 - 10),
                -3_000 + (float)(random.NextDouble() * 20 - 10)),
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

    private struct QueryVisitor(float minimum, float maximum) : IPackedBkdIntersectVisitor
    {
        private readonly byte[] _minimum = Pack(minimum, minimum);
        private readonly byte[] _maximum = Pack(maximum, maximum);

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

        public void Visit(int docId) => Count++;

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (IsValueInRange(packedValue))
                Count++;
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
