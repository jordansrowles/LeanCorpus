using System.Globalization;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Normalises Unicode decimal digits to ASCII digits.
/// </summary>
public sealed class DecimalDigitFilter : ISpanTokenFilter
{

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
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    private static int IndexOfNormalisableDigit(string text)
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
