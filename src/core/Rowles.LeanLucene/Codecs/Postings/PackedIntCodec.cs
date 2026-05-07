using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Frame-of-Reference bit-packing codec for blocks of 128 integers.
/// Packs values using the minimum number of bits needed for the largest value in the block.
/// <para>
/// Output format: <c>[numBits : 1 byte][packed data : numBits × 16 bytes]</c>.
/// When <c>numBits</c> is 0 (all values are zero) the output is a single byte.
/// </para>
/// </summary>
public static class PackedIntCodec
{
    /// <summary>Number of integers processed per block.</summary>
    public const int BlockSize = 128;

    /// <summary>
    /// Computes the number of bits needed to represent the maximum value in the span.
    /// Returns 0 if all values are 0.
    /// </summary>
    public static int BitsRequired(ReadOnlySpan<int> values)
    {
#if NET11_0_OR_GREATER
        // .NET 11 guarantees SSE4.2/POPCNT — use unconditional Vector128 path.
        // Try Vector256 (AVX2) first for 8 ints per iteration.
        if (Vector256.IsHardwareAccelerated && values.Length >= Vector256<int>.Count)
        {
            ref int valuesRef = ref MemoryMarshal.GetReference(values);
            var orVec = Vector256<int>.Zero;
            int i = 0;

            for (; i + Vector256<int>.Count <= values.Length; i += Vector256<int>.Count)
            {
                var v = Vector256.LoadUnsafe(ref valuesRef, (nuint)i);
                orVec = Vector256.BitwiseOr(orVec, v);
            }

            // Reduce 256 → scalar via two 128-bit halves
            var lo = orVec.GetLower();
            var hi = orVec.GetUpper();
            var combined = Vector128.BitwiseOr(lo, hi);
            int ored = combined[0] | combined[1] | combined[2] | combined[3];

            for (; i < values.Length; i++)
                ored |= values[i];

            return ored == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)ored);
        }

        // Fallback to Vector128 (unconditional on .NET 11)
        if (values.Length >= Vector128<int>.Count)
        {
            ref int valuesRef = ref MemoryMarshal.GetReference(values);
            var orVec = Vector128<int>.Zero;
            int i = 0;

            for (; i + Vector128<int>.Count <= values.Length; i += Vector128<int>.Count)
            {
                var v = Vector128.LoadUnsafe(ref valuesRef, (nuint)i);
                orVec = Vector128.BitwiseOr(orVec, v);
            }

            int ored = orVec[0] | orVec[1] | orVec[2] | orVec[3];

            for (; i < values.Length; i++)
                ored |= values[i];

            return ored == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)ored);
        }
#else
        // .NET 10: runtime check before using SIMD
        if (Vector128.IsHardwareAccelerated && values.Length >= Vector128<int>.Count)
        {
            ref int valuesRef = ref MemoryMarshal.GetReference(values);
            var orVec = Vector128<int>.Zero;
            int i = 0;

            for (; i + Vector128<int>.Count <= values.Length; i += Vector128<int>.Count)
            {
                var v = Vector128.LoadUnsafe(ref valuesRef, (nuint)i);
                orVec = Vector128.BitwiseOr(orVec, v);
            }

            int ored = orVec[0] | orVec[1] | orVec[2] | orVec[3];

            for (; i < values.Length; i++)
                ored |= values[i];

            return ored == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)ored);
        }
#endif

        // Scalar fallback
        int or = 0;
        for (int i = 0; i < values.Length; i++)
            or |= values[i];

        return or == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)or);
    }

    /// <summary>
    /// Packs <see cref="BlockSize"/> values into minimum-width bit-packed format.
    /// Returns the number of bytes written to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// The first byte of <paramref name="output"/> contains <c>numBits</c>.
    /// Remaining bytes hold the tightly packed data (<c>numBits × 16</c> bytes).
    /// When all values are zero, only 1 byte is written.
    /// </remarks>
    public static int Pack(ReadOnlySpan<int> values, Span<byte> output)
    {
        if (values.Length < BlockSize)
            throw new ArgumentException($"Input must contain at least {BlockSize} values.", nameof(values));

        int numBits = BitsRequired(values[..BlockSize]);
        output[0] = (byte)numBits;

        if (numBits == 0)
            return 1;

        int totalDataBytes = numBits * 16; // numBits × 128 / 8
        if (output.Length < 1 + totalDataBytes)
            throw new ArgumentException("Output buffer is too small.", nameof(output));

        ulong buffer = 0;
        int bufferedBits = 0;
        int pos = 1;

        for (int i = 0; i < BlockSize; i++)
        {
            buffer |= ((ulong)(uint)values[i]) << bufferedBits;
            bufferedBits += numBits;

            // Flush complete bytes from the accumulator.
            while (bufferedBits >= 8)
            {
                output[pos++] = (byte)buffer;
                buffer >>= 8;
                bufferedBits -= 8;
            }
        }

        // 128 × numBits is always a multiple of 8, so no trailing bits remain.
        return 1 + totalDataBytes;
    }

    /// <summary>
    /// Unpacks bit-packed data into <see cref="BlockSize"/> integer values.
    /// <paramref name="numBits"/> is the bit width per value (0–32).
    /// </summary>
    /// <remarks>
    /// The <paramref name="input"/> span must point past the 1-byte header written by
    /// <see cref="Pack"/>; the caller is responsible for reading that header to obtain
    /// <paramref name="numBits"/>.
    /// </remarks>
    public static void Unpack(ReadOnlySpan<byte> input, int numBits, Span<int> output)
    {
        if (output.Length < BlockSize)
            throw new ArgumentException($"Output must have space for at least {BlockSize} values.", nameof(output));

        if (numBits == 0)
        {
            output[..BlockSize].Clear();
            return;
        }

        if (numBits is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(numBits), "Bit width must be between 1 and 32.");

        ulong mask = (1UL << numBits) - 1;
        ulong buffer = 0;
        int bufferedBits = 0;
        int inputPos = 0;

        for (int i = 0; i < BlockSize; i++)
        {
            // Fill the accumulator until we have enough bits for one value.
            while (bufferedBits < numBits)
            {
                buffer |= (ulong)input[inputPos++] << bufferedBits;
                bufferedBits += 8;
            }

            output[i] = (int)(buffer & mask);
            buffer >>= numBits;
            bufferedBits -= numBits;
        }
    }

    /// <summary>
    /// Packs <see cref="BlockSize"/> sorted values using delta encoding.
    /// Each value is stored as the difference from the previous value;
    /// <paramref name="offset"/> is subtracted from the first value.
    /// Returns (<c>numBits</c>, <c>bytesWritten</c>). The caller must store
    /// <c>numBits</c> and <c>offset</c> separately.
    /// </summary>
    public static (int numBits, int bytesWritten) PackDelta(
        ReadOnlySpan<int> sortedValues, int offset, Span<byte> output)
    {
        if (sortedValues.Length < BlockSize)
            throw new ArgumentException($"Input must contain at least {BlockSize} values.", nameof(sortedValues));

        Span<int> deltas = stackalloc int[BlockSize];
        deltas[0] = sortedValues[0] - offset;

        for (int i = 1; i < BlockSize; i++)
            deltas[i] = sortedValues[i] - sortedValues[i - 1];

        int bytesWritten = Pack(deltas, output);
        int numBits = output[0];
        return (numBits, bytesWritten);
    }

    /// <summary>
    /// Unpacks delta-encoded packed data and integrates (prefix sum)
    /// to recover the original absolute values.
    /// </summary>
    public static void UnpackDelta(
        ReadOnlySpan<byte> input, int numBits, int offset, Span<int> output)
    {
        Unpack(input, numBits, output);

        // Prefix-sum integration to restore absolute values from deltas.
        if (Vector128.IsHardwareAccelerated)
        {
            // SIMD path: Hillis-Steele 4-wide prefix sum with inter-block carry.
            // Two shuffle-and-add steps produce [a, a+b, a+b+c, a+b+c+d] per vector,
            // then the carry from the previous block is broadcast-added.
            ref int outRef = ref MemoryMarshal.GetReference(output);
            int carry = offset;

            for (int i = 0; i < BlockSize; i += Vector128<int>.Count)
            {
                var v = Vector128.LoadUnsafe(ref outRef, (nuint)i);

                v += Vector128.Shuffle(v, Vector128.Create(4, 0, 1, 2));
                v += Vector128.Shuffle(v, Vector128.Create(4, 4, 0, 1));

                v += Vector128.Create(carry);
                Vector128.StoreUnsafe(v, ref outRef, (nuint)i);
                carry = v.GetElement(3);
            }

            // Lightweight overflow guard: since all deltas are non-negative,
            // the final sum must be >= offset. A wrap indicates corrupt postings.
            if (output[BlockSize - 1] < offset)
                throw new InvalidDataException(
                    "Postings data is corrupt: doc ID delta overflow during prefix-sum integration.");
        }
        else
        {
            // Scalar fallback with checked arithmetic for platforms without SIMD.
            try
            {
                output[0] = checked(output[0] + offset);
                for (int i = 1; i < BlockSize; i++)
                    output[i] = checked(output[i] + output[i - 1]);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException(
                    "Postings data is corrupt: doc ID delta overflow during prefix-sum integration.", ex);
            }
        }
    }
}
