using System.Text;

namespace Rowles.LeanCorpus.Codecs.Fst;

/// <summary>
/// The single interpretation of a wildcard pattern shared by every matching route: the FST automaton,
/// literal-prefix narrowing and <c>WildcardQuery.Matches</c>. <c>*</c> matches any sequence of
/// characters, <c>?</c> matches exactly one Unicode scalar value, and a backslash makes the next
/// character literal. A trailing backslash is a literal backslash.
/// </summary>
internal static class WildcardPattern
{
    internal enum TokenKind : byte
    {
        Literal,
        AnyScalar,
        AnySequence,
    }

    /// <summary>One pattern element; <see cref="Literal"/> holds the unescaped text of a literal run.</summary>
    internal readonly record struct Token(TokenKind Kind, string Literal, int RawEnd);

    /// <summary>Splits <paramref name="pattern"/> into literal runs and metacharacters.</summary>
    internal static List<Token> Tokenise(ReadOnlySpan<char> pattern)
    {
        var tokens = new List<Token>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c is '*' or '?')
            {
                FlushLiteral(tokens, literal, i);
                tokens.Add(new Token(c == '*' ? TokenKind.AnySequence : TokenKind.AnyScalar, string.Empty, i + 1));
                i++;
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 == pattern.Length)
                {
                    literal.Append('\\');
                    i++;
                    continue;
                }

                i++;
            }

            int width = ScalarWidth(pattern, i);
            literal.Append(pattern.Slice(i, width));
            i += width;
        }

        FlushLiteral(tokens, literal, pattern.Length);
        return tokens;
    }

    /// <summary>
    /// Returns the unescaped literal text every match must start with, and the raw pattern that follows it.
    /// </summary>
    internal static (string Prefix, string RawSuffix) SplitLeadingLiteral(string pattern)
    {
        var tokens = Tokenise(pattern);
        if (tokens.Count == 0 || tokens[0].Kind != TokenKind.Literal)
            return (string.Empty, pattern);
        return (tokens[0].Literal, pattern[tokens[0].RawEnd..]);
    }

    /// <summary>
    /// True when the pattern is a literal followed by one trailing <c>*</c>, so it is exactly a prefix query.
    /// </summary>
    internal static bool TryGetTrailingStarPrefix(string pattern, out string prefix)
    {
        var tokens = Tokenise(pattern);
        if (tokens.Count == 2 && tokens[0].Kind == TokenKind.Literal && tokens[1].Kind == TokenKind.AnySequence)
        {
            prefix = tokens[0].Literal;
            return true;
        }

        if (tokens.Count == 1 && tokens[0].Kind == TokenKind.AnySequence)
        {
            prefix = string.Empty;
            return true;
        }

        prefix = string.Empty;
        return false;
    }

    /// <summary>Encodes literal text as the UTF-8 bytes the term dictionary stores.</summary>
    internal static byte[] EncodeLiteral(string literal) => Encoding.UTF8.GetBytes(literal);

    /// <summary>Width in UTF-16 units of the scalar at <paramref name="index"/>: 2 for a surrogate pair.</summary>
    internal static int ScalarWidth(ReadOnlySpan<char> text, int index)
        => char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

    private static void FlushLiteral(List<Token> tokens, StringBuilder literal, int rawEnd)
    {
        if (literal.Length == 0)
            return;
        tokens.Add(new Token(TokenKind.Literal, literal.ToString(), rawEnd));
        literal.Clear();
    }
}
