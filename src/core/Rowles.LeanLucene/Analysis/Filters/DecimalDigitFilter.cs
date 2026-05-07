using System.Globalization;

namespace Rowles.LeanLucene.Analysis.Filters;

/// <summary>
/// Normalises Unicode decimal digits to ASCII digits.
/// </summary>
public sealed class DecimalDigitFilter : ITokenFilter
{
    /// <inheritdoc/>
    public void Apply(List<Token> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            string text = token.Text;
            int changedAt = IndexOfNormalisableDigit(text);
            if (changedAt < 0)
                continue;

            string normalised = string.Create(text.Length, text, static (buffer, source) =>
            {
                for (int j = 0; j < source.Length; j++)
                    buffer[j] = NormaliseDigit(source[j]);
            });

            tokens[i] = new Token(normalised, token.StartOffset, token.EndOffset);
        }
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
