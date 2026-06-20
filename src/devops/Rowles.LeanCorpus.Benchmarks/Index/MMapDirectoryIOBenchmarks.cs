using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures raw I/O throughput of <see cref="MMapDirectory"/>,
/// <see cref="IndexOutput"/>, and <see cref="IndexInput"/>.
/// Covers sequential write, sequential read, random read, and
/// byte-level random access patterns.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class MMapDirectoryIOBenchmarks
{
    private const int BlockSize = 4096;

    [Params(1_000, 10_000, 100_000)]
    public int BlockCount { get; set; }

    private string _dirPath = string.Empty;
    private MMapDirectory? _directory;
    private string _dataFilePath = string.Empty;
    private byte[] _writeBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _dirPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-io-bench-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_dirPath);
        _directory = new MMapDirectory(_dirPath);
        _dataFilePath = Path.Combine(_dirPath, "data.bin");
        _writeBuffer = new byte[BlockSize];
        new Random(7).NextBytes(_writeBuffer);

        // Pre-write data once for read benchmarks.
        using (var output = new IndexOutput(_dataFilePath))
        {
            for (int i = 0; i < BlockCount; i++)
                output.WriteBytes(_writeBuffer);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (!string.IsNullOrWhiteSpace(_dirPath) && IODirectory.Exists(_dirPath))
            IODirectory.Delete(_dirPath, recursive: true);
    }

    [Benchmark(Baseline = true, Description = "Sequential write (4 KiB blocks)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_IO_SequentialWrite()
    {
        var path = Path.Combine(_dirPath!, $"write-{Guid.NewGuid():N}.bin");
        using var output = new IndexOutput(path);
        for (int i = 0; i < BlockCount; i++)
            output.WriteBytes(_writeBuffer);
        return BlockCount;
    }

    [Benchmark(Description = "Sequential read (4 KiB blocks)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_IO_SequentialRead()
    {
        var buf = new byte[BlockSize];
        using var input = new IndexInput(_dataFilePath);
        for (int i = 0; i < BlockCount; i++)
            _ = input.ReadBytes(BlockSize);
        return BlockCount;
    }

    [Benchmark(Description = "Random read (page-stride)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_IO_RandomRead()
    {
        var rnd = new Random(7);
        var buf = new byte[BlockSize];
        using var input = new IndexInput(_dataFilePath);
        for (int i = 0; i < BlockCount; i++)
        {
            long pos = (long)rnd.Next(BlockCount) * BlockSize;
            input.Seek(pos);
            _ = input.ReadBytes(BlockSize);
        }
        return BlockCount;
    }

    [Benchmark(Description = "Byte random read (mmap fault stress)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_IO_ReadByte()
    {
        var rnd = new Random(7);
        int sum = 0;
        using var input = new IndexInput(_dataFilePath);
        for (int i = 0; i < BlockCount; i++)
        {
            long pos = (long)rnd.Next(BlockCount) * BlockSize;
            input.Seek(pos);
            sum += input.ReadByte();
        }
        return sum;
    }
}