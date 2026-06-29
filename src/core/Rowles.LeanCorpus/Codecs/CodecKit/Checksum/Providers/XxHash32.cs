using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

internal static class XxHash32
{
    private const uint P1 = 2654435761u, P2 = 2246822519u, P3 = 3266489917u, P4 = 668265263u, P5 = 374761393u;
    private const uint V1 = 606290984u;  // P1 + P2 (wrapping)
    private const uint V4 = 1640531535u; // unchecked(0u - P1)
    private const int Strip = 16;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static uint Compute(ReadOnlySpan<byte> d)
    {
        uint h; int n = d.Length;
        if (n >= Strip) { uint a = V1, b = P2, c = 0, e = V4; int lim = n - Strip, i = 0;
            while (i <= lim) { a = R(a, G32(d, i)); i += 4; b = R(b, G32(d, i)); i += 4; c = R(c, G32(d, i)); i += 4; e = R(e, G32(d, i)); i += 4; }
            h = RL(a, 1) + RL(b, 7) + RL(c, 12) + RL(e, 18); } else h = P5;
        h += (uint)n; int r = n & (Strip - 1), o = n - r;
        while (r >= 4) { h += G32(d, o) * P3; h = RL(h, 17) * P4; o += 4; r -= 4; }
        while (r > 0) { h += d[o] * P5; h = RL(h, 11) * P1; o++; r--; }
        h ^= h >> 15; h *= P2; h ^= h >> 13; h *= P3; h ^= h >> 16; return h;
    }

    public static byte[] ToBytes(ReadOnlySpan<byte> d) { uint h = Compute(d); return [(byte)h, (byte)(h >> 8), (byte)(h >> 16), (byte)(h >> 24)]; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] static uint R(uint a, uint i) { a += i * P2; a = RL(a, 13); a *= P1; return a; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static uint G32(ReadOnlySpan<byte> d, int o) => (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static uint RL(uint v, int b) => (v << b) | (v >> (32 - b));
}
