namespace Rowles.DataForge;

/// <summary>SplitMix64 version 1, with deterministic byte and bounded-value generation.</summary>
public sealed class DataForgePrng
{
    private ulong state;

    public DataForgePrng(ulong seed) => state = seed;

    public ulong NextUInt64()
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    public bool NextBoolean() => (NextUInt64() & 1UL) != 0;

    public int NextInt32(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return (int)NextUInt64((ulong)exclusiveMax);
    }

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (inclusiveMin >= exclusiveMax)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "The exclusive maximum must exceed the inclusive minimum.");

        var width = (ulong)((long)exclusiveMax - inclusiveMin);
        return (int)((long)inclusiveMin + (long)NextUInt64(width));
    }

    public long NextInt64(long exclusiveMax)
    {
        if (exclusiveMax <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return (long)NextUInt64((ulong)exclusiveMax);
    }

    public long NextInt64(long inclusiveMin, long exclusiveMax)
    {
        if (inclusiveMin >= exclusiveMax)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "The exclusive maximum must exceed the inclusive minimum.");

        var width = (ulong)((Int128)exclusiveMax - inclusiveMin);
        return (long)((Int128)inclusiveMin + NextUInt64(width));
    }

    public double NextDouble01() => (NextUInt64() >> 11) * (1.0 / 9_007_199_254_740_992.0);

    public float NextSingle01() => (NextUInt64() >> 40) * (1.0f / 16_777_216.0f);

    public void NextBytes(Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var value = NextUInt64();
            var take = Math.Min(sizeof(ulong), destination.Length - offset);
            for (var i = 0; i < take; i++)
                destination[offset + i] = (byte)(value >> (i * 8));
            offset += take;
        }
    }

    private ulong NextUInt64(ulong exclusiveMax)
    {
        if (exclusiveMax == 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        var threshold = unchecked(0UL - exclusiveMax) % exclusiveMax;
        while (true)
        {
            var value = NextUInt64();
            if (value >= threshold)
                return value % exclusiveMax;
        }
    }
}
