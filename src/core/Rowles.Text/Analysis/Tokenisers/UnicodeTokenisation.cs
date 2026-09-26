using System.Buffers;
using System.Globalization;
using System.Text;

namespace Rowles.LeanCorpus.Analysis.Tokenisers;

internal static class UnicodeTokenisation
{
    internal const string NumberType = "number";

    public static bool IsThai(char c) => c is >= '\u0E00' and <= '\u0E7F';

    internal static bool IsLetterOrDigit(ReadOnlySpan<char> input, int index, out int utf16Length)
    {
        if (!TryDecodeRuneAt(input, index, out Rune rune, out utf16Length))
            return false;

        return Rune.IsLetterOrDigit(rune);
    }

    internal static bool IsWordStart(ReadOnlySpan<char> input, int index, out int utf16Length)
    {
        if (!TryDecodeRuneAt(input, index, out Rune rune, out utf16Length))
            return false;

        return Rune.IsLetterOrDigit(rune) || IsMark(rune);
    }

    internal static bool IsWordPart(ReadOnlySpan<char> input, int index, out int utf16Length)
    {
        if (!TryDecodeRuneAt(input, index, out Rune rune, out utf16Length))
            return false;

        return Rune.IsLetterOrDigit(rune) || IsMark(rune);
    }

    internal static bool IsMark(ReadOnlySpan<char> input, int index, out int utf16Length)
    {
        if (!TryDecodeRuneAt(input, index, out Rune rune, out utf16Length))
            return false;

        return IsMark(rune);
    }

    public static string ClassifyTokenType(ReadOnlySpan<char> text, string defaultType = Token.DefaultType)
    {
        if (text.IsEmpty)
            return defaultType;

        for (int i = 0; i < text.Length;)
        {
            if (!TryDecodeRuneAt(text, i, out Rune rune, out int utf16Length) || !Rune.IsDigit(rune))
                return defaultType;

            i += utf16Length;
        }

        return NumberType;
    }

    public static int ConsumeWord(ReadOnlySpan<char> input, int start, bool allowUnderscore = true, bool allowHyphen = true)
    {
        if (!IsWordPart(input, start, out int scalarLength))
            return start;

        int i = start + scalarLength;
        while (i < input.Length)
        {
            if (IsWordPart(input, i, out scalarLength))
            {
                i += scalarLength;
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

    /// <summary>
    /// Tokenises the next non-Thai word span at the current position and advances <paramref name="i"/>.
    /// Used by <see cref="IcuTokeniser"/> and <see cref="ThaiTokeniser"/> to avoid duplicating the
    /// same Unicode word-consumption loop.
    /// </summary>
    internal static void TokeniseNonThaiSpan(ReadOnlySpan<char> input, ISpanTokenSink sink, ref int i)
    {
        if (!IsWordStart(input, i, out int scalarLength))
        {
            i += scalarLength;
            return;
        }

        int start = i;
        i = ConsumeWord(input, start);
        var span = input[start..i];
        sink.Add(span, start, i, ClassifyTokenType(span));
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
        while (end < input.Length)
        {
            char c = input[end];
            if (c is '<' or '>' or '[' or ']' or '{' or '}')
                break;

            if (TryDecodeRuneAt(input, end, out Rune rune, out int scalarLength))
            {
                if (Rune.IsWhiteSpace(rune))
                    break;

                end += scalarLength;
            }
            else
            {
                // An unpaired surrogate is not whitespace and is kept in the URL token.
                end++;
            }
        }

        while (end > start && input[end - 1] is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']')
            end--;

        return end > start;
    }

    public static bool TryReadEmail(ReadOnlySpan<char> input, int start, out int end)
    {
        end = start;
        if (!IsEmailLocalChar(input, start, out _))
            return false;

        int i = start;
        bool sawAt = false;
        bool sawDomainDot = false;

        while (i < input.Length)
        {
            if (!sawAt)
            {
                if (input[i] == '@')
                {
                    if (i == start || i + 1 >= input.Length)
                        return false;

                    sawAt = true;
                    i++;
                    continue;
                }

                if (IsEmailLocalChar(input, i, out int localScalarLength))
                {
                    i += localScalarLength;
                    continue;
                }

                break;
            }

            if (input[i] == '-')
            {
                i++;
                continue;
            }

            if (input[i] == '.')
            {
                sawDomainDot = true;
                i++;
                continue;
            }

            if (TryDecodeRuneAt(input, i, out Rune rune, out int domainScalarLength)
                && Rune.IsLetterOrDigit(rune))
            {
                i += domainScalarLength;
                continue;
            }

            break;
        }

        if (!sawAt || !sawDomainDot)
            return false;

        end = i;
        while (end > start && input[end - 1] is '.' or ',' or ';' or ':' or ')')
            end--;

        return end > start && sawDomainDot;
    }

    /// <summary>
    /// An <see cref="ISpanTokenSink"/> that shifts all incoming token offsets by a fixed amount
    /// and optionally overrides the token type before forwarding to the wrapped sink.
    /// </summary>
    internal sealed class OffsetShiftingSink : ISpanTokenSink
    {
        private readonly ISpanTokenSink _inner;
        private readonly int _offset;
        private readonly string? _typeOverride;

        public OffsetShiftingSink(ISpanTokenSink inner, int offset, string? typeOverride = null)
        {
            _inner = inner;
            _offset = offset;
            _typeOverride = typeOverride;
        }

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        {
            _inner.Add(text, startOffset + _offset, endOffset + _offset,
                _typeOverride ?? type, positionIncrement, payload);
        }
    }

    private static bool TryDecodeRuneAt(ReadOnlySpan<char> input, int index, out Rune rune, out int utf16Length)
    {
        if ((uint)index >= (uint)input.Length)
        {
            rune = default;
            utf16Length = 0;
            return false;
        }

        if (Rune.DecodeFromUtf16(input[index..], out rune, out utf16Length) == OperationStatus.Done)
            return true;

        // Treat each unpaired surrogate as one delimiter code unit. This keeps scanning
        // deterministic and preserves all following UTF-16 offsets.
        rune = default;
        utf16Length = 1;
        return false;
    }

    private static bool IsMark(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static bool IsInfixConnector(ReadOnlySpan<char> input, int index, bool allowUnderscore, bool allowHyphen)
    {
        char c = input[index];
        if (c is not ('\'' or '\u2019' or '_' or '-'))
            return false;

        if ((c == '_' && !allowUnderscore) || (c == '-' && !allowHyphen))
            return false;

        return IsWordPartBefore(input, index)
            && index + 1 < input.Length
            && IsWordPart(input, index + 1, out _);
    }

    private static bool IsWordPartBefore(ReadOnlySpan<char> input, int index)
    {
        int previousIndex = index - 1;
        if (previousIndex < 0)
            return false;

        if (char.IsLowSurrogate(input[previousIndex])
            && previousIndex > 0
            && char.IsHighSurrogate(input[previousIndex - 1]))
        {
            previousIndex--;
        }

        return IsWordPart(input, previousIndex, out _);
    }

    private static bool IsTrailingConnector(char c, bool allowUnderscore, bool allowHyphen)
        => c is '\'' or '\u2019'
            || (allowUnderscore && c == '_')
            || (allowHyphen && c == '-');

    private static bool IsEmailLocalChar(ReadOnlySpan<char> input, int index, out int utf16Length)
    {
        if (!TryDecodeRuneAt(input, index, out Rune rune, out utf16Length))
            return false;

        return Rune.IsLetterOrDigit(rune)
            || rune.Value is '.' or '_' or '%' or '+' or '-' or '!' or '#' or '$' or '&' or '\'' or '*' or '/' or '=' or '?' or '^' or '`' or '{' or '|' or '}' or '~';
    }
}
