using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Checksum.Providers;

internal static class XxHash64
{
    private const ulong P1 = 11400714785074694791ul, P2 = 14029467366897019727ul, P3 = 1609587929392839161ul;
    private const ulong P4 = 9650029242287828579ul, P5 = 2870177450012600261ul;
    private const ulong V1 = 6983438078262162902ul;  // P1 + P2 (wrapping)
    private const ulong V4 = 7046029288634856825ul;  // unchecked(0ul - P1)
    private const int Strip = 32;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong Compute(ReadOnlySpan<byte> d)
    {
        ulong h; int n = d.Length;
        if (n >= Strip) { ulong a = V1, b = P2, c = 0, e = V4; int lim = n - Strip, i = 0;
            while (i <= lim) { a = R(a, G64(d, i)); i += 8; b = R(b, G64(d, i)); i += 8; c = R(c, G64(d, i)); i += 8; e = R(e, G64(d, i)); i += 8; }
            h = RL(a, 1) + RL(b, 7) + RL(c, 12) + RL(e, 18);
            h = M(h, a); h = M(h, b); h = M(h, c); h = M(h, e); } else h = P5;
        h += (ulong)n; int r = n & (Strip - 1), o = n - r;
        while (r >= 8) { h ^= R(0, G64(d, o)); h = RL(h, 27) * P1 + P4; o += 8; r -= 8; }
        if (r >= 4) { h ^= G32(d, o) * P1; h = RL(h, 23) * P2 + P3; o += 4; r -= 4; }
        while (r > 0) { h ^= d[o] * P5; h = RL(h, 11) * P1; o++; r--; }
        h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P3; h ^= h >> 32; return h;
    }

    public static byte[] ToBytes(ReadOnlySpan<byte> d) { ulong h = Compute(d); return [(byte)h, (byte)(h >> 8), (byte)(h >> 16), (byte)(h >> 24), (byte)(h >> 32), (byte)(h >> 40), (byte)(h >> 48), (byte)(h >> 56)]; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] static ulong R(ulong a, ulong i) { a += i * P2; a = RL(a, 31); a *= P1; return a; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static ulong M(ulong h, ulong a) { a = R(0, a); h ^= a; h = h * P1 + P4; return h; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static ulong G64(ReadOnlySpan<byte> d, int o) => (ulong)d[o] | ((ulong)d[o + 1] << 8) | ((ulong)d[o + 2] << 16) | ((ulong)d[o + 3] << 24) | ((ulong)d[o + 4] << 32) | ((ulong)d[o + 5] << 40) | ((ulong)d[o + 6] << 48) | ((ulong)d[o + 7] << 56);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static uint G32(ReadOnlySpan<byte> d, int o) => (uint)d[o] | ((uint)d[o + 1] << 8) | ((uint)d[o + 2] << 16) | ((uint)d[o + 3] << 24);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] static ulong RL(ulong v, int b) => (v << b) | (v >> (64 - b));
}
