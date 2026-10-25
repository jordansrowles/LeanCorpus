using Rowles.DataForge;

namespace Rowles.DataForge.Tests.Core;

public sealed class PrngTests
{
    [Theory]
    [InlineData(0UL, new ulong[] { 0xe220a8397b1dcdafUL, 0x6e789e6aa1b965f4UL, 0x06c45d188009454fUL, 0xf88bb8a8724c81ecUL, 0x1b39896a51a8749bUL })]
    [InlineData(42UL, new ulong[] { 0xbdd732262feb6e95UL, 0x28efe333b266f103UL, 0x47526757130f9f52UL, 0x581ce1ff0e4ae394UL, 0x09bc585a244823f2UL })]
    public void SplitMix64_matches_locked_vectors(ulong seed, ulong[] expected)
    {
        var prng = new DataForgePrng(seed);
        foreach (var value in expected)
            Assert.Equal(value, prng.NextUInt64());
    }

    [Fact]
    public void Bounded_integer_apis_observe_exclusive_bounds_including_extreme_ranges()
    {
        var prng = new DataForgePrng(0x123456789abcdef0UL);
        Assert.Equal(0, prng.NextInt32(1));
        Assert.Equal(7, prng.NextInt32(7, 8));

        for (var i = 0; i < 2_000; i++)
        {
            var value = prng.NextInt32(int.MinValue, int.MaxValue);
            Assert.InRange(value, int.MinValue, int.MaxValue - 1);

            var wide = prng.NextInt64(long.MinValue, long.MaxValue);
            Assert.InRange(wide, long.MinValue, long.MaxValue - 1);

            var positive = prng.NextInt64(long.MaxValue);
            Assert.InRange(positive, 0, long.MaxValue - 1);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => prng.NextInt32(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => prng.NextInt32(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => prng.NextInt64(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => prng.NextInt64(2, 2));
    }

    [Fact]
    public void Byte_stream_is_little_endian_by_contract_and_not_platform_endianness()
    {
        var bytes = new byte[8];
        new DataForgePrng(0).NextBytes(bytes);
        Assert.Equal(new byte[] { 0xaf, 0xcd, 0x1d, 0x7b, 0x39, 0xa8, 0x20, 0xe2 }, bytes);
    }

    [Fact]
    public void Double_and_single_values_use_the_locked_high_bits()
    {
        var doublePrng = new DataForgePrng(42);
        var singlePrng = new DataForgePrng(42);
        Assert.Equal((0xbdd732262feb6e95UL >> 11) * (1.0 / 9_007_199_254_740_992.0), doublePrng.NextDouble01());
        Assert.Equal((float)(0xbdd732262feb6e95UL >> 40) * (1.0f / 16_777_216.0f), singlePrng.NextSingle01());
    }
}
