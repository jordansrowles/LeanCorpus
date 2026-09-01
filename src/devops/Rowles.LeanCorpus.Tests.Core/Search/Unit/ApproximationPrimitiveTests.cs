using System.Buffers.Binary;
using System.Numerics;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;
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

    [Fact(DisplayName = "HLL++: Precision Thresholds And Bias Neighbours Match Reference Data")]
    public void HyperLogLogPlusPlus_ReferenceDataMatchesPublishedValues()
    {
        double[] expected = [10, 20, 40, 80, 220, 400, 900, 1_800, 3_100, 6_500, 11_500, 20_000, 50_000, 120_000, 350_000];
        for (int precision = 4; precision <= 18; precision++)
            Assert.Equal(expected[precision - 4], HyperLogLogPlusPlusData.Threshold(precision));

        Assert.Equal(17.1612, HyperLogLogPlusPlusData.EstimateBias(5, 27.5), precision: 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyperLogLogPlusPlus(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyperLogLogPlusPlus(19));
        Assert.Equal(4, new HyperLogLogPlusPlus(4).Precision);
        Assert.Equal(18, new HyperLogLogPlusPlus(18).Precision);
    }

    [Fact(DisplayName = "HLL++: Sparse Encoding Uses Corrected Dense Index And Rank")]
    public void HyperLogLogPlusPlus_SparseEncodingReconstructsDenseRegisters()
    {
        const int precision = 14;
        const int index = 7_321;
        const int rank = 9;
        ulong hash = ((ulong)index << (64 - precision)) | (1UL << (64 - precision - rank));
        uint encoded = HyperLogLogPlusPlus.EncodeSparseHash(hash);

        Assert.Equal(index, HyperLogLogPlusPlus.DecodeDenseIndex(encoded, precision));
        Assert.Equal(rank, HyperLogLogPlusPlus.DecodeDenseRank(encoded, precision));
    }

    [Fact(DisplayName = "HLL++: Sparse Storage Deduplicates Compresses And Converts Without Register Loss")]
    public void HyperLogLogPlusPlus_SparseStorageAndDenseTransitionPreserveState()
    {
        var sketch = new HyperLogLogPlusPlus(4);
        var expected = new byte[sketch.RegisterCount];
        for (ulong i = 0; i < 200; i++)
        {
            ulong hash = Mix(i);
            sketch.AddHash(hash);
            sketch.AddHash(hash);
            int index = (int)(hash >> 60);
            byte rank = (byte)Math.Min(61, BitOperations.LeadingZeroCount(hash << 4) + 1);
            expected[index] = Math.Max(expected[index], rank);
        }

        Assert.False(sketch.IsSparse);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], sketch.GetRegisterForTesting(i));
    }

    [Fact(DisplayName = "HLL++: Numeric Hash Input Is Explicitly Little Endian")]
    public void HyperLogLogPlusPlus_NumericHashInputIsLittleEndian()
    {
        const long value = 0x0102030405060708;
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        uint expected = HyperLogLogPlusPlus.EncodeSparseHash(XxHash64.Compute(bytes));
        var sketch = new HyperLogLogPlusPlus();
        sketch.Add(value);
        Assert.True(sketch.ContainsSparseValueForTesting(expected));
    }

    [Fact(DisplayName = "HLL++: All Sparse And Dense Merge Combinations Match Union")]
    public void HyperLogLogPlusPlus_AllMergeCombinationsMatchUnion()
    {
        AssertMergeEquivalent(14, 100, 100);
        AssertMergeEquivalent(10, 100, 20_000);
        AssertMergeEquivalent(10, 20_000, 100);
        AssertMergeEquivalent(10, 20_000, 20_000);
    }

    [Fact(DisplayName = "HLL++: Bounded Statistical Error Across Precisions")]
    public void HyperLogLogPlusPlus_StatisticalErrorRemainsBounded()
    {
        foreach (int precision in new[] { 8, 12, 16 })
        {
            double squaredError = 0;
            const int cardinality = 50_000;
            const int trials = 5;
            for (int trial = 0; trial < trials; trial++)
            {
                var sketch = new HyperLogLogPlusPlus(precision);
                for (ulong i = 0; i < cardinality; i++) sketch.AddHash(Mix(i + (ulong)trial * cardinality));
                double relative = (sketch.Estimate() - cardinality) / cardinality;
                squaredError += relative * relative;
            }
            double rmse = Math.Sqrt(squaredError / trials);
            Assert.InRange(rmse, 0, new HyperLogLogPlusPlus(precision).ExpectedRelativeError * 3);
        }
    }

    private static void AssertMergeEquivalent(int precision, int leftCount, int rightCount)
    {
        var left = new HyperLogLogPlusPlus(precision);
        var right = new HyperLogLogPlusPlus(precision);
        var union = new HyperLogLogPlusPlus(precision);
        for (ulong i = 0; i < (ulong)leftCount; i++) { ulong hash = Mix(i); left.AddHash(hash); union.AddHash(hash); }
        for (ulong i = 0; i < (ulong)rightCount; i++) { ulong hash = Mix(i + (ulong)leftCount / 2); right.AddHash(hash); union.AddHash(hash); }
        left.MergeFrom(right);
        Assert.Equal(union.Estimate(), left.Estimate(), precision: 10);
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
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
        const int count = 10_000;
        for (int i = 1; i <= count; i++) digest.Add(i);

        foreach (double percentile in new[] { 0d, .5d, .9d, .95d, .99d, .999d, 1d })
        {
            double expected = Math.Max(1, Math.Ceiling(percentile * count));
            Assert.InRange(Math.Abs(digest.Quantile(percentile) - expected), 0, 75);
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
        for (long i = 1; i <= 10_000; i++) histogram.RecordValue(i);
        histogram.RecordValue(1_000_000, 10);

        Assert.Equal(10_010, histogram.TotalCount);
        Assert.Equal(1, histogram.Min); Assert.Equal(1_000_000, histogram.Max);
        Assert.InRange(histogram.ValueAtPercentile(50), 4_900, 5_100);
        Assert.InRange(histogram.ValueAtPercentile(90), 8_900, 9_100);
        Assert.InRange(histogram.ValueAtPercentile(99), 9_800, 10_100);
        Assert.InRange(histogram.ValueAtPercentile(100), 999_000, 1_000_000);
    }
}
