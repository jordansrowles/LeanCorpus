using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// SIMD-accelerated ASCII character classification for 8-character blocks
/// loaded as <see cref="Vector128{T}"/> of <see cref="ushort"/>.
/// Used by tokenisers and filters that scan for alphanumeric or case boundaries
/// in predominantly-ASCII text.
/// </summary>
internal static class AsciiCharInspector
{
    private static readonly Vector128<ushort> Vec_0xFF80 = Vector128.Create((ushort)0xFF80);
    private static readonly Vector128<ushort> VecA = Vector128.Create((ushort)'A');
    private static readonly Vector128<ushort> VecZ = Vector128.Create((ushort)'Z');
    private static readonly Vector128<ushort> Veca = Vector128.Create((ushort)'a');
    private static readonly Vector128<ushort> Vecz = Vector128.Create((ushort)'z');
    private static readonly Vector128<ushort> Vec0 = Vector128.Create((ushort)'0');
    private static readonly Vector128<ushort> Vec9 = Vector128.Create((ushort)'9');
    private static readonly Vector128<ushort> Vec0x20 = Vector128.Create((ushort)0x20);

    /// <summary>
    /// Returns true if any character in the vector has a codepoint above 0x7F
    /// (i.e. is non-ASCII).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ContainsNonAscii(Vector128<ushort> chars)
        => (chars & Vec_0xFF80) != Vector128<ushort>.Zero;

    /// <summary>
    /// Returns an 8-bit mask where bit <c>i</c> is set if <c>chars[i]</c>
    /// is an ASCII letter or digit (<c>'A'-'Z'</c>, <c>'a'-'z'</c>, <c>'0'-'9'</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint IsAlphanumericMask(Vector128<ushort> chars)
    {
        var isUpper = Vector128.GreaterThanOrEqual(chars, VecA)
                    & Vector128.LessThanOrEqual(chars, VecZ);
        var isLower = Vector128.GreaterThanOrEqual(chars, Veca)
                    & Vector128.LessThanOrEqual(chars, Vecz);
        var isDigit = Vector128.GreaterThanOrEqual(chars, Vec0)
                    & Vector128.LessThanOrEqual(chars, Vec9);
        return (isUpper | isLower | isDigit).ExtractMostSignificantBits();
    }

    /// <summary>
    /// Returns an 8-bit mask where bit <c>i</c> is set if <c>chars[i]</c>
    /// is an ASCII uppercase letter (<c>'A'-'Z'</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint IsUppercaseMask(Vector128<ushort> chars)
    {
        var isUpper = Vector128.GreaterThanOrEqual(chars, VecA)
                    & Vector128.LessThanOrEqual(chars, VecZ);
        return isUpper.ExtractMostSignificantBits();
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, converting
    /// all characters to invariant lowercase. The SIMD ASCII path is used only when the
    /// entire source is ASCII; otherwise the BCL performs Unicode invariant casing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AsciiToLower(ReadOnlySpan<char> source, Span<char> destination)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("The destination must be at least as long as the source.", nameof(destination));

        if (!IsAscii(source))
        {
            int written = source.ToLowerInvariant(destination);
            if (written != source.Length)
                throw new InvalidOperationException("Invariant lowercasing did not write the full source span.");
            return;
        }

        int i = 0;
        int length = source.Length;
        ref ushort srcRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(source));
        ref ushort dstRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(destination));

        while (i <= length - Vector128<ushort>.Count)
        {
            Vector128<ushort> chars = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i));
            var isUpper = Vector128.GreaterThanOrEqual(chars, VecA)
                        & Vector128.LessThanOrEqual(chars, VecZ);
            var lowered = chars + Vec0x20;
            Vector128<ushort> result = Vector128.ConditionalSelect(isUpper, lowered, chars);
            result.StoreUnsafe(ref Unsafe.Add(ref dstRef, i));
            i += Vector128<ushort>.Count;
        }

        for (; i < length; i++)
            destination[i] = char.ToLowerInvariant(source[i]);
    }

    /// <summary>
    /// Converts text to invariant lowercase in place. The SIMD ASCII path is used only
    /// when the entire buffer is ASCII; otherwise the BCL performs Unicode invariant
    /// casing through a temporary buffer because the span casing API rejects overlap.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AsciiToLowerInPlace(Span<char> buffer)
    {
        if (!IsAscii(buffer))
        {
            LowerUnicodeInPlace(buffer);
            return;
        }

        int i = 0;
        int length = buffer.Length;
        ref ushort bufRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(buffer));

        while (i <= length - Vector128<ushort>.Count)
        {
            Vector128<ushort> chars = Vector128.LoadUnsafe(ref Unsafe.Add(ref bufRef, i));
            var isUpper = Vector128.GreaterThanOrEqual(chars, VecA)
                        & Vector128.LessThanOrEqual(chars, VecZ);
            var lowered = chars + Vec0x20;
            Vector128<ushort> result = Vector128.ConditionalSelect(isUpper, lowered, chars);
            result.StoreUnsafe(ref Unsafe.Add(ref bufRef, i));
            i += Vector128<ushort>.Count;
        }

        for (; i < length; i++)
            buffer[i] = char.ToLowerInvariant(buffer[i]);
    }

    private static void LowerUnicodeInPlace(Span<char> buffer)
    {
        const int StackThreshold = 256;
        if (buffer.Length <= StackThreshold)
        {
            Span<char> lowered = stackalloc char[buffer.Length];
            int written = buffer.ToLowerInvariant(lowered);
            if (written != buffer.Length)
                throw new InvalidOperationException("Invariant lowercasing did not write the full buffer.");
            lowered.CopyTo(buffer);
            return;
        }

        char[] rented = ArrayPool<char>.Shared.Rent(buffer.Length);
        try
        {
            Span<char> lowered = rented.AsSpan(0, buffer.Length);
            int written = buffer.ToLowerInvariant(lowered);
            if (written != buffer.Length)
                throw new InvalidOperationException("Invariant lowercasing did not write the full buffer.");
            lowered.CopyTo(buffer);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Returns true only when every character in <paramref name="text"/> is ASCII.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsAscii(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] > 0x7F)
                return false;

        return true;
    }
}
