using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Postings;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Microbenchmark for <see cref="PackedIntCodec.Pack"/> and <see cref="PackedIntCodec.Unpack"/>
/// scalar loops at the bit widths typical of postings delta encoding.
/// Profiling this determines whether a SIMD gather/scatter rewrite is warranted.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class PackedIntCodecBenchmarks
{
    [Params(3, 5, 7, 9, 11)]
    public int BitsPerValue { get; set; }

    private int[] _values = [];
    private byte[] _packed = [];

    [GlobalSetup]
    public void Setup()
    {
        _values = new int[PackedIntCodec.BlockSize];
        var rnd = new Random(42);
        int maxVal = (1 << BitsPerValue) - 1;
        for (int i = 0; i < _values.Length; i++)
            _values[i] = rnd.Next(maxVal + 1);

        _packed = new byte[1 + BitsPerValue * 16];
    }

    [Benchmark(Description = "Pack")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Pack()
    {
        return PackedIntCodec.Pack(_values, _packed);
    }

    [Benchmark(Description = "Unpack")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Unpack()
    {
        PackedIntCodec.Unpack(_packed.AsSpan(1), _packed[0], _values);
    }
}
