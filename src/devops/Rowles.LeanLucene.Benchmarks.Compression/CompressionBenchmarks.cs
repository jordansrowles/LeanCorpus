using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Compression.LZ4;
using Rowles.LeanLucene.Compression.Snappy;
using Rowles.LeanLucene.Compression.Zstandard;

namespace Rowles.LeanLucene.Benchmarks.Compression;

/// <summary>
/// Benchmarks compress and decompress throughput for every registered
/// <see cref="FieldCompressionPolicy"/>: None, Deflate, Brotli, LZ4, Snappy,
/// and Zstandard. Three payload sizes cover typical stored-field blocks.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(CompressionBenchmarkConfig))]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class CompressionBenchmarks
{
    internal static readonly byte[] SmallPayload  = BuildPayload(128);
    internal static readonly byte[] MediumPayload = BuildPayload(4 * 1024);
    internal static readonly byte[] LargePayload  = BuildPayload(64 * 1024);

    private byte[] _payload     = [];
    private byte[] _compressed  = [];
    private int    _originalSize;

    /// <summary>Compression policy under test.</summary>
    [Params(
        FieldCompressionPolicy.None,
        FieldCompressionPolicy.Deflate,
        FieldCompressionPolicy.Brotli,
        FieldCompressionPolicy.Lz4,
        FieldCompressionPolicy.Snappy,
        FieldCompressionPolicy.Zstandard)]
    public FieldCompressionPolicy Policy { get; set; }

    /// <summary>Payload size label.</summary>
    [Params("small", "medium", "large")]
    public string Size { get; set; } = "medium";

    /// <summary>
    /// Registers extension codecs and warms up the codec for the current parameters.
    /// </summary>
    /// <remarks>
    /// BDN spawns a new child process for each benchmark. The extension codec assemblies
    /// (LZ4, Snappy, Zstandard) are never loaded in those child processes because nothing in
    /// the generated entry point directly references their types, so <c>[ModuleInitializer]</c>
    /// never fires. Calling <c>Register()</c> here forces the assembly to load and the codec
    /// to be available before the benchmark runs.
    /// </remarks>
    [GlobalSetup]
    public void Setup()
    {
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();

        _payload = Size switch
        {
            "small"  => SmallPayload,
            "large"  => LargePayload,
            _        => MediumPayload,
        };

        var codec = CompressionCodecRegistry.Get(Policy);
        _compressed  = codec.Compress(_payload);
        _originalSize = _payload.Length;
    }

    /// <summary>Benchmarks the compress path for the current policy and payload size.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public byte[] Compress()
    {
        var codec = CompressionCodecRegistry.Get(Policy);
        return codec.Compress(_payload);
    }

    /// <summary>Benchmarks the decompress path for the current policy and payload size.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public byte[] Decompress()
    {
        var codec = CompressionCodecRegistry.Get(Policy);
        return codec.Decompress(_compressed, _originalSize);
    }

    internal static byte[] BuildPayload(int size)
    {
        const string source =
            "The quick brown fox jumps over the lazy dog. " +
            "LeanLucene stores documents in compressed blocks for efficient retrieval. " +
            "Segment-centric indexing provides atomic commit semantics and memory-mapped reads. " +
            "Compression benchmarks compare throughput and ratio across all registered codecs. ";

        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var result = new byte[size];
        for (var i = 0; i < size; i++)
            result[i] = sourceBytes[i % sourceBytes.Length];

        return result;
    }

    private static object? GetParamValue(BenchmarkCase benchmarkCase, string name)
    {
        foreach (var item in benchmarkCase.Parameters.Items)
            if (item.Definition.Name == name)
                return item.Value;
        return null;
    }

    // ---- config ----

    private sealed class CompressionBenchmarkConfig : ManualConfig
    {
        public CompressionBenchmarkConfig()
        {
            AddColumnProvider(DefaultColumnProviders.Instance);
            AddColumn(RankColumn.Arabic);
            AddColumn(StatisticColumn.Min);
            AddColumn(StatisticColumn.Max);
            AddColumn(new ThroughputColumn());
            AddColumn(new CompressionRatioColumn());

            AddExporter(MarkdownExporter.GitHub);
            AddExporter(HtmlExporter.Default);

            AddLogger(ConsoleLogger.Default);

            AddValidator(JitOptimizationsValidator.FailOnError);
            AddValidator(ExecutionValidator.FailOnError);
        }
    }

    // ---- custom columns ----

    /// <summary>Shows estimated throughput in MB/s derived from payload size and mean time.</summary>
    private sealed class ThroughputColumn : IColumn
    {
        public string        Id                  => nameof(ThroughputColumn);
        public string        ColumnName          => "Throughput";
        public bool          AlwaysShow          => true;
        public ColumnCategory Category           => ColumnCategory.Custom;
        public int           PriorityInCategory  => 1;
        public bool          IsNumeric           => true;
        public UnitType      UnitType            => UnitType.Dimensionless;
        public string        Legend              => "Estimated throughput: payload bytes / mean time";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            var report = summary[benchmarkCase];
            if (report?.ResultStatistics is null) return "N/A";

            var mean = report.ResultStatistics.Mean; // nanoseconds
            if (mean <= 0) return "N/A";

            var sizeLabel = GetParamValue(benchmarkCase, "Size") as string ?? "medium";
            var bytes = sizeLabel switch
            {
                "small"  => 128,
                "large"  => 64 * 1024,
                _        => 4 * 1024,
            };

            var mbPerSec = bytes / (mean / 1e9) / (1024.0 * 1024.0);
            return $"{mbPerSec:F1} MB/s";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
            => GetValue(summary, benchmarkCase);

        public bool IsAvailable(Summary summary)                                  => true;
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase)       => false;
    }

    /// <summary>
    /// Shows the compression ratio (original size / compressed size). Always shows "1:1"
    /// for <see cref="FieldCompressionPolicy.None"/>; shows "N/A" for unregistered codecs.
    /// </summary>
    private sealed class CompressionRatioColumn : IColumn
    {
        public string        Id                  => nameof(CompressionRatioColumn);
        public string        ColumnName          => "Ratio";
        public bool          AlwaysShow          => true;
        public ColumnCategory Category           => ColumnCategory.Custom;
        public int           PriorityInCategory  => 2;
        public bool          IsNumeric           => false;
        public UnitType      UnitType            => UnitType.Dimensionless;
        public string        Legend              => "Compression ratio: original size / compressed size";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            if (GetParamValue(benchmarkCase, "Policy") is not FieldCompressionPolicy policy)
                return "N/A";

            if (policy == FieldCompressionPolicy.None)
                return "1:1";

            var sizeLabel = GetParamValue(benchmarkCase, "Size") as string ?? "medium";
            var payload = sizeLabel switch
            {
                "small"  => SmallPayload,
                "large"  => LargePayload,
                _        => MediumPayload,
            };

            if (!CompressionCodecRegistry.TryGet((byte)policy, out var codec) || codec is null)
                return "N/A";

            try
            {
                var compressed = codec.Compress(payload);
                return $"{(double)payload.Length / compressed.Length:F2}x";
            }
            catch
            {
                return "N/A";
            }
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
            => GetValue(summary, benchmarkCase);

        public bool IsAvailable(Summary summary)                                  => true;
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase)       => false;
    }
}
