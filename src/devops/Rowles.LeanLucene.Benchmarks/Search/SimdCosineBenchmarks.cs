using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Compares the two SIMD cosine implementations (System.Numerics.Vector vs explicit
/// Runtime.Intrinsics) so the slower path can be removed.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class SimdCosineBenchmarks
{
    [Params(64, 128, 256, 512, 1024)]
    public int Dimension { get; set; }

    private float[] _a = [];
    private float[] _b = [];

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _a = new float[Dimension];
        _b = new float[Dimension];
        for (int i = 0; i < Dimension; i++)
        {
            _a[i] = (float)(rnd.NextDouble() * 2 - 1);
            _b[i] = (float)(rnd.NextDouble() * 2 - 1);
        }
    }

    [Benchmark(Baseline = true, Description = "System.Numerics.Vector")]
    public float Numerics()
    {
        float total = 0f;
        for (int i = 0; i < 1000; i++)
            total += SimdVectorOps.CosineSimilarity(_a, _b);
        return total;
    }

    [Benchmark(Description = "Runtime.Intrinsics")]
    public float Intrinsics()
    {
        float total = 0f;
        for (int i = 0; i < 1000; i++)
            total += SimdIntrinsicsVectorOps.CosineSimilarity(_a, _b);
        return total;
    }

    [Benchmark(Description = "Numerics dot product")]
    public float NumericsDot()
    {
        float total = 0f;
        for (int i = 0; i < 1000; i++)
            total += SimdVectorOps.DotProduct(_a, _b);
        return total;
    }

    [Benchmark(Description = "Intrinsics dot product")]
    public float IntrinsicsDot()
    {
        float total = 0f;
        for (int i = 0; i < 1000; i++)
            total += SimdIntrinsicsVectorOps.DotProduct(_a, _b);
        return total;
    }
}
