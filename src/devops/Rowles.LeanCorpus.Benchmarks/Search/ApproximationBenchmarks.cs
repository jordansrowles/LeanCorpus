using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Aggregations;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Compares bounded approximate metric states with their exact collection baselines.</summary>
[MemoryDiagnoser]
public sealed class ApproximationBenchmarks
{
    private long[] _values = [];

    [Params(10_000, 100_000)]
    public int ValueCount { get; set; }

    [GlobalSetup]
    public void Setup()
        => _values = Enumerable.Range(0, ValueCount).Select(static value => (long)(value % 20_000)).ToArray();

    [Benchmark(Baseline = true)]
    public int ExactDistinct()
        => _values.ToHashSet().Count;

    [Benchmark]
    public double HyperLogLogPlusPlus()
    {
        var sketch = new HyperLogLogPlusPlus();
        foreach (long value in _values) sketch.Add(value);
        return sketch.Estimate();
    }

    [Benchmark]
    public long ExactP99()
    {
        var sorted = _values.ToArray();
        Array.Sort(sorted);
        return sorted[(int)Math.Ceiling(sorted.Length * .99) - 1];
    }

    [Benchmark]
    public double TDigestP99()
    {
        var digest = new TDigest();
        foreach (long value in _values) digest.Add(value);
        return digest.Quantile(.99);
    }

    [Benchmark]
    public long HdrHistogramP99()
    {
        var histogram = new HdrHistogram(20_000);
        foreach (long value in _values) histogram.RecordValue(value);
        return histogram.ValueAtPercentile(99);
    }
}
