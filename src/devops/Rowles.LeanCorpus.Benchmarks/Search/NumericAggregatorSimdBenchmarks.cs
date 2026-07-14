using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using BenchmarkDotNet.Attributes;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares scalar vs <see cref="Vector256{T}"/> reduction for the min/max/sum/count
/// aggregation loop used by <c>NumericAggregator.ComputeStats</c>.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class NumericAggregatorSimdBenchmarks
{
    [Params(64, 256, 1024, 4096)]
    public int SpanLength { get; set; }

    private double[] _data = [];

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(42);
        _data = new double[SpanLength];
        for (int i = 0; i < _data.Length; i++)
            _data[i] = rnd.NextDouble() * 1000 - 500;
    }

    [Benchmark(Baseline = true, Description = "Scalar")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public (double Min, double Max, double Sum, int Count) Scalar()
    {
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int count = 0;
        foreach (double value in _data)
        {
            count++;
            if (value < min) min = value;
            if (value > max) max = value;
            sum += value;
        }
        return (min, max, sum, count);
    }

    [Benchmark(Description = "Vector256")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public (double Min, double Max, double Sum, int Count) Vectorised()
    {
        return ComputeVector256(_data);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (double Min, double Max, double Sum, int Count) ComputeVector256(double[] data)
    {
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int i = 0;
        int len = data.Length;

        if (Vector256.IsHardwareAccelerated && len >= Vector256<double>.Count)
        {
            var vMin = Vector256.Create(double.MaxValue);
            var vMax = Vector256.Create(double.MinValue);
            var vSum = Vector256<double>.Zero;

            int simdEnd = len - (len % Vector256<double>.Count);
            for (; i < simdEnd; i += Vector256<double>.Count)
            {
                var v = Vector256.LoadUnsafe(ref data[i]);
                vMin = Vector256.Min(vMin, v);
                vMax = Vector256.Max(vMax, v);
                vSum = Vector256.Add(vSum, v);
            }

            min = MinHorizontal(vMin);
            max = MaxHorizontal(vMax);
            sum = SumHorizontal(vSum);
        }

        // Scalar tail
        for (; i < len; i++)
        {
            double value = data[i];
            if (value < min) min = value;
            if (value > max) max = value;
            sum += value;
        }
        return (min, max, sum, len);
    }

    private static double SumHorizontal(Vector256<double> v)
    {
        var v128 = v.GetUpper() + v.GetLower();
        return v128.GetElement(0) + v128.GetElement(1);
    }

    private static double MinHorizontal(Vector256<double> v)
    {
        var v128 = Vector128.Min(v.GetUpper(), v.GetLower());
        return Math.Min(v128.GetElement(0), v128.GetElement(1));
    }

    private static double MaxHorizontal(Vector256<double> v)
    {
        var v128 = Vector128.Max(v.GetUpper(), v.GetLower());
        return Math.Max(v128.GetElement(0), v128.GetElement(1));
    }
}
