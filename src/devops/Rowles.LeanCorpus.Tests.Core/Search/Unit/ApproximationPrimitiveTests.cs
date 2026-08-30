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
}
