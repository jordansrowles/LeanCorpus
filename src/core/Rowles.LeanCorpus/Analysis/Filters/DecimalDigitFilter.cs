using System.Buffers;
using System.Globalization;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises Unicode decimal digits to ASCII digits.
/// </summary>
public sealed class DecimalDigitFilter : ISpanTokenFilter
{
    private const int StackThreshold = 128;

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        int index = IndexOfNormalisableDigit(text);
        if (index < 0)
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        char[]? rented = null;
        try
        {
            Span<char> buffer = text.Length <= StackThreshold
                ? stackalloc char[text.Length]
                : (rented = ArrayPool<char>.Shared.Rent(text.Length));

            text.CopyTo(buffer);
            for (int i = index; i < text.Length; i++)
                buffer[i] = NormaliseDigit(buffer[i]);

            sink.Add(buffer[..text.Length], startOffset, endOffset, type, positionIncrement, payload);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int IndexOfNormalisableDigit(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is >= '0' and <= '9')
                continue;

            if (char.GetUnicodeCategory(c) == UnicodeCategory.DecimalDigitNumber)
                return i;
        }

        return -1;
    }

    private static char NormaliseDigit(char c)
    {
        if (c is >= '0' and <= '9')
            return c;

        if (char.GetUnicodeCategory(c) != UnicodeCategory.DecimalDigitNumber)
            return c;

        double value = char.GetNumericValue(c);
        return value is >= 0 and <= 9 && value == Math.Truncate(value)
            ? (char)('0' + (int)value)
            : c;
    }
}
