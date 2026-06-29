using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Slices input text into tokens at word boundaries, splitting on
/// whitespace and punctuation whilst tracking character offsets.
/// </summary>
public sealed class Tokeniser : ISpanTokeniser
{
    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
#if NET11_0_OR_GREATER
        TokeniseSimd(input, sink);
#else
        if (Vector128.IsHardwareAccelerated)
        {
            TokeniseSimd(input, sink);
            return;
        }
        ScalarTokenise(input, sink);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScalarTokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;
        while (i < input.Length)
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && char.IsLetterOrDigit(input[i]))
                i++;

            sink.Add(input[start..i], start, i, UnicodeTokenisation.ClassifyTokenType(input[start..i]));
        }
    }

    /// <summary>
    /// Emits token offsets without allocating any strings.
    /// Used by <see cref="StandardAnalyser"/> to defer string materialisation until after filtering.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="offsets">The list to populate with <c>(Start, End)</c> character offset pairs. Cleared before use.</param>
    public void TokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
#if NET11_0_OR_GREATER
        TokeniseOffsetsSimd(input, offsets);
#else
        if (Vector128.IsHardwareAccelerated)
        {
            TokeniseOffsetsSimd(input, offsets);
            return;
        }
        ScalarTokeniseOffsets(input, offsets);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScalarTokeniseOffsets(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;

        while (i < input.Length)
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < input.Length && char.IsLetterOrDigit(input[i]))
                i++;

            offsets.Add((start, i));
        }
    }

    // ─────────────────────────── SIMD paths ───────────────────────────

    private static void TokeniseSimd(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        int i = 0;
        int? pendingStart = null;
        int length = input.Length;
        ref ushort charsRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(input));

        // Main SIMD loop: process 8 chars per iteration.
        while (i <= length - Vector128<ushort>.Count)
        {
            Vector128<ushort> chars = Vector128.LoadUnsafe(ref Unsafe.Add(ref charsRef, i));

            if (AsciiCharInspector.ContainsNonAscii(chars))
            {
                // Block contains a non-ASCII character; process one scalar
                // position at a time to preserve boundary correctness.
                SimdScalarStep(input, ref i, ref pendingStart, sink);
                continue;
            }

            uint mask = AsciiCharInspector.IsAlphanumericMask(chars);

            if (pendingStart.HasValue)
            {
                // Consuming a word that started in a previous block.
                // Find the first non-alphanumeric character.
                uint nonAlnum = ~mask & 0xFFu;
                int endOff = System.Numerics.BitOperations.TrailingZeroCount(nonAlnum);
                if (endOff < 8)
                {
                    int start = pendingStart.Value;
                    int end = i + endOff;
                    sink.Add(input[start..end], start, end, UnicodeTokenisation.ClassifyTokenType(input[start..end]));
                    pendingStart = null;
                    i += endOff + 1;
                }
                else
                {
                    i += 8;
                }
                continue;
            }

            // Looking for a word start: find the first alphanumeric character.
            int wordStartOff = System.Numerics.BitOperations.TrailingZeroCount(mask);
            if (wordStartOff >= 8)
            {
                i += 8;
                continue;
            }

            // Find word end within the block: find the first non-alphanumeric
            // character after the word start.
            uint afterStart = mask >> wordStartOff;
            uint maskBits = (1u << (8 - wordStartOff)) - 1u;
            uint invertedWindow = (~afterStart) & maskBits;
            int wordEndOff = System.Numerics.BitOperations.TrailingZeroCount(invertedWindow);

            if (wordEndOff >= 8 - wordStartOff)
            {
                // Word runs to end of block; track it for the next iteration.
                pendingStart = i + wordStartOff;
                i += 8;
            }
            else
            {
                int start = i + wordStartOff;
                int end = i + wordStartOff + wordEndOff;
                sink.Add(input[start..end], start, end, UnicodeTokenisation.ClassifyTokenType(input[start..end]));
                i += wordStartOff + wordEndOff + 1;
            }
        }

        // Scalar tail for remaining < 8 characters and pending word.
        ScalarTail(input, ref i, ref pendingStart, length, sink);
    }

    // Shared scalar helpers for the SIMD paths (avoid local functions that
    // capture ref-like parameters).

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SimdScalarStep(
        ReadOnlySpan<char> input, ref int i, ref int? pendingStart, ISpanTokenSink sink)
    {
        if (pendingStart.HasValue)
        {
            if (char.IsLetterOrDigit(input[i]))
            {
                i++;
                return;
            }
            int start = pendingStart.Value;
            sink.Add(input[start..i], start, i, UnicodeTokenisation.ClassifyTokenType(input[start..i]));
            pendingStart = null;
            i++;
        }
        else
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                return;
            }
            pendingStart = i;
            i++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScalarTail(
        ReadOnlySpan<char> input, ref int i, ref int? pendingStart, int length, ISpanTokenSink sink)
    {
        if (pendingStart.HasValue)
        {
            while (i < length && char.IsLetterOrDigit(input[i]))
                i++;
            int start = pendingStart.Value;
            sink.Add(input[start..i], start, i, UnicodeTokenisation.ClassifyTokenType(input[start..i]));
            pendingStart = null;
        }

        while (i < length)
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < length && char.IsLetterOrDigit(input[i]))
                i++;
            sink.Add(input[start..i], start, i, UnicodeTokenisation.ClassifyTokenType(input[start..i]));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SimdScalarStepOffsets(
        ReadOnlySpan<char> input, ref int i, ref int? pendingStart, List<(int Start, int End)> offsets)
    {
        if (pendingStart.HasValue)
        {
            if (char.IsLetterOrDigit(input[i]))
            {
                i++;
                return;
            }
            offsets.Add((pendingStart.Value, i));
            pendingStart = null;
            i++;
        }
        else
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                return;
            }
            pendingStart = i;
            i++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScalarTailOffsets(
        ReadOnlySpan<char> input, ref int i, ref int? pendingStart, int length, List<(int Start, int End)> offsets)
    {
        if (pendingStart.HasValue)
        {
            while (i < length && char.IsLetterOrDigit(input[i]))
                i++;
            offsets.Add((pendingStart.Value, i));
            pendingStart = null;
        }

        while (i < length)
        {
            if (!char.IsLetterOrDigit(input[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < length && char.IsLetterOrDigit(input[i]))
                i++;
            offsets.Add((start, i));
        }
    }

    private static void TokeniseOffsetsSimd(ReadOnlySpan<char> input, List<(int Start, int End)> offsets)
    {
        offsets.Clear();
        int i = 0;
        int? pendingStart = null;
        int length = input.Length;
        ref ushort charsRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(input));

        // Main SIMD loop: process 8 chars per iteration.
        while (i <= length - Vector128<ushort>.Count)
        {
            Vector128<ushort> chars = Vector128.LoadUnsafe(ref Unsafe.Add(ref charsRef, i));

            if (AsciiCharInspector.ContainsNonAscii(chars))
            {
                SimdScalarStepOffsets(input, ref i, ref pendingStart, offsets);
                continue;
            }

            uint mask = AsciiCharInspector.IsAlphanumericMask(chars);

            if (pendingStart.HasValue)
            {
                uint nonAlnum = ~mask & 0xFFu;
                int endOff = System.Numerics.BitOperations.TrailingZeroCount(nonAlnum);
                if (endOff < 8)
                {
                    offsets.Add((pendingStart.Value, i + endOff));
                    pendingStart = null;
                    i += endOff + 1;
                }
                else
                {
                    i += 8;
                }
                continue;
            }

            int wordStartOff = System.Numerics.BitOperations.TrailingZeroCount(mask);
            if (wordStartOff >= 8)
            {
                i += 8;
                continue;
            }

            uint afterStart = mask >> wordStartOff;
            uint maskBits = (1u << (8 - wordStartOff)) - 1u;
            uint invertedWindow = (~afterStart) & maskBits;
            int wordEndOff = System.Numerics.BitOperations.TrailingZeroCount(invertedWindow);

            if (wordEndOff >= 8 - wordStartOff)
            {
                pendingStart = i + wordStartOff;
                i += 8;
            }
            else
            {
                offsets.Add((i + wordStartOff, i + wordStartOff + wordEndOff));
                i += wordStartOff + wordEndOff + 1;
            }
        }

        // Scalar tail for remaining < 8 characters.
        ScalarTailOffsets(input, ref i, ref pendingStart, length, offsets);
    }
}
