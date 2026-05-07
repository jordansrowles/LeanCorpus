using BenchmarkDotNet.Running;
using Rowles.LeanLucene.Benchmarks.Compression;
using Rowles.LeanLucene.Compression.LZ4;
using Rowles.LeanLucene.Compression.Snappy;
using Rowles.LeanLucene.Compression.Zstandard;

Lz4Compression.Register();
SnappyCompression.Register();
ZstandardCompression.Register();

BenchmarkRunner.Run<CompressionBenchmarks>(null, args);
