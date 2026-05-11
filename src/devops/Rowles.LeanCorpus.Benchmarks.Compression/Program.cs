using BenchmarkDotNet.Running;
using Rowles.LeanCorpus.Benchmarks.Compression;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;

// Register extension codecs in the parent process so the Ratio column can compress
// payloads for ratio computation during report generation. Child processes receive
// their own registrations via [GlobalSetup] in CompressionBenchmarks.
Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();

BenchmarkRunner.Run<CompressionBenchmarks>(null, args);
