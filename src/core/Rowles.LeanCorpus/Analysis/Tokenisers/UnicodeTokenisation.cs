using System.Globalization;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

internal static class UnicodeTokenisation
{
    internal const string NumberType = "number";

    public static bool IsThai(char c) => c is >= '\u0E00' and <= '\u0E7F';

    public static bool IsWordStart(char c) => char.IsLetterOrDigit(c) || IsMark(c);

    public static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || IsMark(c);

    public static string ClassifyTokenType(ReadOnlySpan<char> text, string defaultType = Token.DefaultType)
    {
        if (text.IsEmpty)
            return defaultType;

        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                return defaultType;
        }

        return NumberType;
    }

    public static int ConsumeWord(ReadOnlySpan<char> input, int start, bool allowUnderscore = true, bool allowHyphen = true)
    {
        int i = start + 1;
        while (i < input.Length)
        {
            char c = input[i];
            if (IsWordPart(c))
            {
                i++;
                continue;
            }

            if (IsInfixConnector(input, i, allowUnderscore, allowHyphen))
            {
                i++;
                continue;
            }

            break;
        }

        while (i > start && IsTrailingConnector(input[i - 1], allowUnderscore, allowHyphen))
            i--;

        return i;
    }

    public static bool TryReadUrl(ReadOnlySpan<char> input, int start, out int end)
    {
        end = start;
        var remaining = input[start..];
        if (!remaining.StartsWith("http://".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !remaining.StartsWith("https://".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !remaining.StartsWith("ftp://".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !remaining.StartsWith("www.".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        end = start;
        while (end < input.Length && !char.IsWhiteSpace(input[end]) && input[end] is not ('<' or '>' or '[' or ']' or '{' or '}'))
            end++;

        while (end > start && input[end - 1] is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']')
            end--;

        return end > start;
    }

    public static bool TryReadEmail(ReadOnlySpan<char> input, int start, out int end)
    {
        end = start;
        if (!IsEmailLocalChar(input[start]))
            return false;

        int i = start;
        bool sawAt = false;
        bool sawDomainDot = false;

        while (i < input.Length)
        {
            char c = input[i];
            if (!sawAt)
            {
                if (c == '@')
                {
                    if (i == start || i + 1 >= input.Length)
                        return false;

                    sawAt = true;
                    i++;
                    continue;
                }

                if (!IsEmailLocalChar(c))
                    break;
            }
            else
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                {
                    i++;
                    continue;
                }

                if (c == '.')
                {
                    sawDomainDot = true;
                    i++;
                    continue;
                }

                break;
            }

            i++;
        }

        if (!sawAt || !sawDomainDot)
            return false;

        end = i;
        while (end > start && input[end - 1] is '.' or ',' or ';' or ':' or ')')
            end--;

        return end > start && sawDomainDot;
    }

    public static void AddShiftedTokens(List<Token> target, List<Token> source, int offset, string? typeOverride = null)
    {
        for (int i = 0; i < source.Count; i++)
        {
            var token = source[i];
            var shifted = new Token(
                token.Text,
                token.StartOffset + offset,
                token.EndOffset + offset,
                typeOverride ?? token.Type,
                token.PositionIncrement,
                token.Payload);
            target.Add(shifted);
        }
    }

    private static bool IsMark(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsInfixConnector(ReadOnlySpan<char> input, int index, bool allowUnderscore, bool allowHyphen)
    {
        char c = input[index];
        if (c is not ('\'' or '\u2019' or '_' or '-'))
            return false;

        if ((c == '_' && !allowUnderscore) || (c == '-' && !allowHyphen))
            return false;

        return index > 0
            && index + 1 < input.Length
            && IsWordPart(input[index - 1])
            && IsWordPart(input[index + 1]);
    }

    private static bool IsTrailingConnector(char c, bool allowUnderscore, bool allowHyphen)
        => c is '\'' or '\u2019'
            || (allowUnderscore && c == '_')
            || (allowHyphen && c == '-');

    private static bool IsEmailLocalChar(char c)
        => char.IsLetterOrDigit(c) || c is '.' or '_' or '%' or '+' or '-' or '!' or '#' or '$' or '&' or '\'' or '*' or '/' or '=' or '?' or '^' or '`' or '{' or '|' or '}' or '~';
}
