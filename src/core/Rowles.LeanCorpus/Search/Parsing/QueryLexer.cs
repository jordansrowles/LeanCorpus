namespace Rowles.LeanCorpus.Search.Parsing;

internal sealed class QueryLexer
{
    private readonly QueryParserOptions _options;
    private readonly bool _limitsAreComplexity;

    internal QueryLexer(QueryParserOptions options, bool limitsAreComplexity)
    {
        _options = options;
        _limitsAreComplexity = limitsAreComplexity;
    }

    private void ThrowQueryParseLimitExceeded(string message, int? offset = null)
    {
        if (_limitsAreComplexity)
            throw new QueryParseLimitException(message);

        if (offset is int value)
            throw new QueryParseException(message, value);

        throw new QueryParseException(message);
    }

    private static bool ContainsEscape(ReadOnlySpan<char> raw)
    {
        for (int index = 0; index + 1 < raw.Length; index++)
        {
            if (raw[index] == '\\')
                return true;
        }

        return false;
    }

    internal List<QueryToken> Lex(string input)
    {
        var tokens = new List<QueryToken>();
        int i = 0;

        void AddToken(QueryToken token)
        {
            if (tokens.Count >= _options.MaxTokens)
                ThrowQueryParseLimitExceeded($"The query exceeds the configured parser token limit of {_options.MaxTokens}.");
            tokens.Add(token);
        }

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '+': AddToken(new QueryToken(QueryTokenType.Plus, "+", i)); i++; continue;
                case '-': AddToken(new QueryToken(QueryTokenType.Minus, "-", i)); i++; continue;
                case '(': AddToken(new QueryToken(QueryTokenType.LParen, "(", i)); i++; continue;
                case ')': AddToken(new QueryToken(QueryTokenType.RParen, ")", i)); i++; continue;
                case ':': AddToken(new QueryToken(QueryTokenType.Colon, ":", i)); i++; continue;
                case '~': AddToken(new QueryToken(QueryTokenType.Tilde, "~", i)); i++; continue;
                case '^': AddToken(new QueryToken(QueryTokenType.Caret, "^", i)); i++; continue;
                case '=': AddToken(new QueryToken(QueryTokenType.Equal, "=", i)); i++; continue;
                case '|': AddToken(new QueryToken(QueryTokenType.Pipe, "|", i)); i++; continue;
                case '[': AddToken(new QueryToken(QueryTokenType.OpenSquare, "[", i)); i++; continue;
                case ']': AddToken(new QueryToken(QueryTokenType.CloseSquare, "]", i)); i++; continue;
                case '{': AddToken(new QueryToken(QueryTokenType.OpenCurly, "{", i)); i++; continue;
                case '}': AddToken(new QueryToken(QueryTokenType.CloseCurly, "}", i)); i++; continue;
            }

            if (c == '/')
            {
                int slashOffset = i++;
                int rawPatternStart = i;
                int rawPatternEnd = -1;
                var pattern = new System.Text.StringBuilder();
                bool closed = false;

                void AppendPattern(char value)
                {
                    if (pattern.Length >= _options.MaxRegexpPatternChars)
                    {
                        ThrowQueryParseLimitExceeded(
                            $"The regular-expression pattern exceeds the configured character limit of {_options.MaxRegexpPatternChars}.",
                            slashOffset);
                    }
                    pattern.Append(value);
                }

                while (i < input.Length)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        if (input[i + 1] == '/')
                        {
                            AppendPattern('/');
                            i += 2;
                            continue;
                        }
                        AppendPattern(input[i]);
                        AppendPattern(input[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (input[i] == '/')
                    {
                        rawPatternEnd = i;
                        i++;
                        closed = true;
                        break;
                    }
                    AppendPattern(input[i++]);
                }
                if (!closed)
                    throw new QueryParseException("Unmatched regular expression delimiter.", slashOffset);
                string rawPattern = input[rawPatternStart..rawPatternEnd];
                AddToken(new QueryToken(QueryTokenType.Regex, pattern.ToString(), slashOffset, rawPattern, HasEscapes: ContainsEscape(rawPattern)));
                continue;
            }

            if (c == '"')
            {
                int quoteOffset = i;
                i++; // skip opening quote
                int start = i;
                while (i < input.Length)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        i += 2;
                        continue;
                    }
                    if (input[i] == '"')
                        break;
                    i++;
                }
                if (i >= input.Length)
                {
                    throw new QueryParseException(
                        "Unmatched quote in query string.", quoteOffset);
                }
                string phraseRaw = input[start..i];
                AddToken(new QueryToken(QueryTokenType.Phrase, Unescape(phraseRaw), quoteOffset, phraseRaw, HasEscapes: ContainsEscape(phraseRaw)));
                i++; // skip closing quote
                continue;
            }

            // Regular term (supports backslash escaping)
            {
                int start = i;
                bool hasEscapes = false;

                while (i < input.Length)
                {
                    char ch = input[i];

                    if (ch == '\\' && i + 1 < input.Length)
                    {
                        hasEscapes = true;
                        i += 2; // skip backslash and escaped char
                        continue;
                    }

                    if (char.IsWhiteSpace(ch) || ch == '(' || ch == ')' ||
                        ch == ':' || ch == '"' || ch == '~' || ch == '^' ||
                        ch == '=' || ch == '|' || ch == '[' || ch == ']' ||
                        ch == '{' || ch == '}')
                    {
                        break;
                    }

                    i++;
                }

                ReadOnlySpan<char> rawSpan = input.AsSpan(start, i - start);
                bool hasUnescapedWildcard = HasUnescapedWildcard(rawSpan);
                if (hasUnescapedWildcard && rawSpan.Length > _options.MaxWildcardPatternChars)
                {
                    ThrowQueryParseLimitExceeded(
                        $"The wildcard pattern exceeds the configured character limit of {_options.MaxWildcardPatternChars}.",
                        start);
                }

                string raw = rawSpan.ToString();
                string termValue = hasEscapes ? Unescape(raw.AsSpan()) : raw;
                var type = !hasEscapes ? GetKeywordType(termValue) : QueryTokenType.Term;
                AddToken(new QueryToken(type, termValue, start, raw, hasUnescapedWildcard, hasEscapes));
            }
        }

        return tokens;
    }

    private static bool HasUnescapedWildcard(ReadOnlySpan<char> raw)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
                continue;
            }
            if (raw[i] is '*' or '?')
                return true;
        }
        return false;
    }


    /// <summary>
    /// Returns a copy of <paramref name="raw"/> with backslash escapes resolved.
    /// <c>\x</c> produces literal <c>x</c>; only backslash-prefixed pairs are
    /// recognised; a trailing lone backslash is treated as literal.
    /// </summary>
    private static string Unescape(ReadOnlySpan<char> raw)
    {
        int escapes = 0;
        for (int j = 0; j < raw.Length; j++)
        {
            if (raw[j] == '\\' && j + 1 < raw.Length)
            {
                escapes++;
                j++; // skip escaped char
            }
        }

        return string.Create(raw.Length - escapes, raw, static (dest, src) =>
        {
            int di = 0;
            for (int si = 0; si < src.Length; si++)
            {
                if (src[si] == '\\' && si + 1 < src.Length)
                {
                    si++; // skip backslash
                    dest[di++] = src[si];
                }
                else
                {
                    dest[di++] = src[si];
                }
            }
        });
    }

    private static QueryTokenType GetKeywordType(string value)
    {
        if (value.Equals("AND", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.And;
        if (value.Equals("OR", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Or;
        if (value.Equals("NOT", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.Not;
        if (value.Equals("TO", StringComparison.OrdinalIgnoreCase)) return QueryTokenType.To;
        return QueryTokenType.Term;
    }

}

internal enum QueryTokenType
{
    Term, Phrase, Regex, Plus, Minus, LParen, RParen, Colon, Tilde, Caret,
    Equal, And, Or, Not, To, Pipe, OpenSquare, CloseSquare, OpenCurly, CloseCurly
}

internal readonly record struct QueryToken(
    QueryTokenType Type,
    string Value,
    int Offset,
    string? RawValue = null,
    bool HasUnescapedWildcard = false,
    bool HasEscapes = false)
{
    public string Raw => RawValue ?? Value;
}
