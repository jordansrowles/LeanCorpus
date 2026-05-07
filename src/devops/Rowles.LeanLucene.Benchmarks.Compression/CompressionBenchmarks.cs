using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Codecs.StoredFields;

namespace Rowles.LeanLucene.Benchmarks.Compression;

/// <summary>
/// Benchmarks compress and decompress throughput for every registered
/// <see cref="FieldCompressionPolicy"/>: None, Deflate, Brotli, LZ4, Snappy,
/// and Zstandard. Three payload sizes cover typical stored-field blocks.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class CompressionBenchmarks
{
    /// <summary>Small stored-field block (~128 bytes of typical document text).</summary>
    private static readonly byte[] SmallPayload = BuildPayload(128);

    /// <summary>Medium stored-field block (~4 KB — 16-doc block of short fields).</summary>
    private static readonly byte[] MediumPayload = BuildPayload(4 * 1024);

    /// <summary>Large stored-field block (~64 KB — 16-doc block of long body fields).</summary>
    private static readonly byte[] LargePayload = BuildPayload(64 * 1024);

    private byte[] _payload = [];
    private byte[] _compressed = [];
    private int _originalSize;

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

    /// <summary>Warm up the codec and capture compressed bytes for decompression benchmarks.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _payload = Size switch
        {
            "small"  => SmallPayload,
            "large"  => LargePayload,
            _        => MediumPayload,
        };

        var codec = CompressionCodecRegistry.Get(Policy);
        _compressed = codec.Compress(_payload);
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

    private static byte[] BuildPayload(int size)
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
}
