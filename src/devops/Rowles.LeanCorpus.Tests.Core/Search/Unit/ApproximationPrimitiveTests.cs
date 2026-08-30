using Rowles.LeanCorpus.Search.Aggregations;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Deterministic coverage for cardinality and percentile primitives.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class ApproximationPrimitiveTests
{
    [Fact(DisplayName = "HLL++: Sparse Dense Merge And Deterministic Accuracy")]
    public void HyperLogLogPlusPlus_CoversCoreContracts()
    {
        var sparse = new HyperLogLogPlusPlus(10);
        Assert.Equal(0, sparse.Estimate());
        for (int i = 0; i < 10; i++) sparse.Add(42L);
        Assert.InRange(sparse.Estimate(), 0.9, 1.1);

        var left = new HyperLogLogPlusPlus(14);
        var right = new HyperLogLogPlusPlus(14);
        for (int i = 0; i < 50_000; i++) (i < 25_000 ? left : right).Add((long)i);
        Assert.False(left.IsSparse);
        left.MergeFrom(right);
        Assert.InRange(Math.Abs(left.Estimate() - 50_000d) / 50_000d, 0, left.ExpectedRelativeError * 4);
        Assert.Throws<ArgumentException>(() => left.MergeFrom(new HyperLogLogPlusPlus(13)));
    }

    [Theory(DisplayName = "HLL++: Deterministic Accuracy Across Cardinalities")]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void HyperLogLogPlusPlus_EstimatesKnownCardinalities(int cardinality)
    {
        var sketch = new HyperLogLogPlusPlus();
        for (int i = 0; i < cardinality; i++) sketch.Add((long)i);

        Assert.InRange(Math.Abs(sketch.Estimate() - cardinality) / cardinality, 0, sketch.ExpectedRelativeError * 4);
    }

    [Fact(DisplayName = "t-digest: Quantiles And Merge Are Tail Aware")]
    public void TDigest_CoversCoreContracts()
    {
        var left = new TDigest(100); var right = new TDigest(100);
        for (int i = 1; i <= 10_000; i++) (i <= 5_000 ? left : right).Add(i);
        left.MergeFrom(right);
        Assert.InRange(left.Quantile(.5), 4_800, 5_200);
        Assert.InRange(left.Quantile(.99), 9_700, 10_000);
        Assert.Equal(0, new TDigest().Quantile(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => left.Add(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => left.Quantile(1.1));
        Assert.Throws<ArgumentException>(() => left.MergeFrom(new TDigest(200)));
    }

    [Fact(DisplayName = "t-digest: Standard Percentiles Match A Deterministic Reference")]
    public void TDigest_StandardPercentilesRemainAccurate()
    {
        var digest = new TDigest(200);
        for (int i = 1; i <= 100_000; i++) digest.Add(i);

        foreach (double percentile in new[] { 0d, .5d, .9d, .95d, .99d, .999d, 1d })
        {
            double expected = Math.Max(1, Math.Ceiling(percentile * 100_000));
            Assert.InRange(Math.Abs(digest.Quantile(percentile) - expected), 0, 250);
        }

        var identical = new TDigest();
        identical.Add(42); identical.Add(42); Assert.Equal(42, identical.Quantile(.5));
    }

    [Fact(DisplayName = "HDR Histogram: Relative Buckets Percentiles And Merge")]
    public void HdrHistogram_CoversCoreContracts()
    {
        var left = new HdrHistogram(1_000_000, 3); var right = new HdrHistogram(1_000_000, 3);
        foreach (long value in new long[] { 1, 10, 100, 1_000 }) left.RecordValue(value);
        foreach (long value in new long[] { 10_000, 100_000 }) right.RecordValue(value);
        left.MergeFrom(right);
        Assert.Equal(6, left.TotalCount); Assert.Equal(1, left.Min); Assert.Equal(100_000, left.Max);
        Assert.InRange(left.ValueAtPercentile(90), 10_000, 100_000);
        Assert.NotEmpty(left.EnumerateDistribution());
        Assert.Throws<ArgumentOutOfRangeException>(() => left.RecordValue(1_000_001));
        Assert.Throws<ArgumentException>(() => left.MergeFrom(new HdrHistogram(10_000, 3)));
    }

    [Fact(DisplayName = "HDR Histogram: Latency Percentiles Stay Within Configured Precision")]
    public void HdrHistogram_HandlesBoundaryAndRepeatedValues()
    {
        var histogram = new HdrHistogram(1_000_000, 3);
        for (long i = 1; i <= 100_000; i++) histogram.RecordValue(i);
        histogram.RecordValue(1_000_000, 10);

        Assert.Equal(100_010, histogram.TotalCount);
        Assert.Equal(1, histogram.Min); Assert.Equal(1_000_000, histogram.Max);
        Assert.InRange(histogram.ValueAtPercentile(50), 49_000, 51_000);
        Assert.InRange(histogram.ValueAtPercentile(90), 89_000, 91_000);
        Assert.InRange(histogram.ValueAtPercentile(99), 98_000, 101_000);
        Assert.InRange(histogram.ValueAtPercentile(100), 999_000, 1_000_000);
    }
}
