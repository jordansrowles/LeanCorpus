using BenchmarkDotNet.Running;
using Rowles.LeanLucene.Benchmarks.Compression;
using Rowles.LeanLucene.Compression.LZ4;
using Rowles.LeanLucene.Compression.Snappy;
using Rowles.LeanLucene.Compression.Zstandard;

// Register extension codecs in the parent process so the Ratio column can compress
// payloads for ratio computation during report generation. Child processes receive
// their own registrations via [GlobalSetup] in CompressionBenchmarks.
Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();

BenchmarkRunner.Run<CompressionBenchmarks>(null, args);
