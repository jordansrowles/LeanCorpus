using System.Runtime.CompilerServices;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

namespace Rowles.LeanCorpus.Benchmarks.Compression;

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

    internal CompressionMeasurement Measurement { get; private set; } = null!;

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

        _payload = GetPayload(Size);
        var codec = CompressionCodecRegistry.Get(Policy);
        _compressed = codec.Compress(_payload);
        _originalSize = _payload.Length;
        Measurement = CompressionFixture.Measure(Policy, _payload, _compressed,
            codec.Decompress(_compressed, _originalSize));
        if (Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR") is { Length: > 0 } artifacts)
            CompressionFixture.WriteEvidence(artifacts, Measurement);
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

    internal static byte[] BuildPayload(int size) => CompressionFixture.BuildPayload(size);

    private static byte[] GetPayload(string size) => size switch
    {
        "small" => SmallPayload,
        "medium" => MediumPayload,
        "large" => LargePayload,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown compression payload size.")
    };

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
            AddColumn(new PayloadMetricColumn("OriginalBytes"));
            AddColumn(new PayloadMetricColumn("CompressedBytes"));
            AddColumn(new PayloadMetricColumn("CompressionRatio"));

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

    private sealed class PayloadMetricColumn(string metric) : IColumn
    {
        public string Id => metric;
        public string ColumnName => metric;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 2;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => metric == "CompressionRatio"
            ? "Original bytes / encoded bytes; this is a size ratio, not a competitive speed ratio"
            : "Exact preflight workload byte count";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            if (GetParamValue(benchmarkCase, "Policy") is not FieldCompressionPolicy policy)
                return "N/A";
            var payload = GetPayload(GetParamValue(benchmarkCase, "Size") as string ?? "medium");
            var codec = CompressionCodecRegistry.Get(policy);
            var compressed = codec.Compress(payload);
            var measurement = CompressionFixture.Measure(policy, payload, compressed,
                codec.Decompress(compressed, payload.Length));
            return metric switch
            {
                "OriginalBytes" => measurement.OriginalBytes.ToString(CultureInfo.InvariantCulture),
                "CompressedBytes" => measurement.CompressedBytes.ToString(CultureInfo.InvariantCulture),
                "CompressionRatio" => measurement.CompressionRatio.ToString("F3", CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException($"Unknown compression metric '{metric}'.")
            };
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
            => GetValue(summary, benchmarkCase);
        public bool IsAvailable(Summary summary) => true;
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    }
}
