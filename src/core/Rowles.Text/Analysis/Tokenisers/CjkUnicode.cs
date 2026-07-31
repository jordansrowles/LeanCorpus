using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

internal static class CjkUnicode
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int DecodeCodePoint(ReadOnlySpan<char> input, int index, out int charsConsumed)
    {
        char first = input[index];
        if (char.IsHighSurrogate(first)
            && index + 1 < input.Length
            && char.IsLowSurrogate(input[index + 1]))
        {
            charsConsumed = 2;
            return char.ConvertToUtf32(first, input[index + 1]);
        }

        charsConsumed = 1;
        return first;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsIdeograph(int codePoint)
    {
        return codePoint is >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2A6DF
            or >= 0x2A700 and <= 0x2B73F
            or >= 0x2B740 and <= 0x2B81F
            or >= 0x2B820 and <= 0x2CEAF
            or >= 0x2CEB0 and <= 0x2EE5F
            or >= 0x2F800 and <= 0x2FA1F
            or >= 0x30000 and <= 0x323AF;
    }
}
