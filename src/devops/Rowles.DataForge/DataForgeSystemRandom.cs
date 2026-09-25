namespace Rowles.DataForge;

/// <summary>System.Random adapter whose complete stream is backed by DataForge SplitMix64.</summary>
public sealed class DataForgeSystemRandom : Random
{
    private readonly DataForgePrng prng;

    public DataForgeSystemRandom(ulong seed) : base(0) => prng = new DataForgePrng(seed);

    protected override double Sample() => prng.NextDouble01();

    public override int Next() => prng.NextInt32(int.MaxValue);

    public override int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        return maxValue == 0 ? 0 : prng.NextInt32(maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue));
        return minValue == maxValue ? minValue : prng.NextInt32(minValue, maxValue);
    }

    public override long NextInt64() => prng.NextInt64(long.MaxValue);

    public override long NextInt64(long maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        return maxValue == 0 ? 0 : prng.NextInt64(maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue));
        return minValue == maxValue ? minValue : prng.NextInt64(minValue, maxValue);
    }

    public override double NextDouble() => prng.NextDouble01();

    public override float NextSingle() => prng.NextSingle01();

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        prng.NextBytes(buffer);
    }

    public override void NextBytes(Span<byte> buffer) => prng.NextBytes(buffer);
}
