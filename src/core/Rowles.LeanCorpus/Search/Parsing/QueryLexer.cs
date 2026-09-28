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
            throw new QueryParseLimitException(message, offset);

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
            {
                ThrowQueryParseLimitExceeded(
                    $"The query exceeds the configured parser token limit of {_options.MaxTokens}.",
                    token.Offset);
            }
            tokens.Add(token);
        }

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '+': AddToken(new QueryToken(QueryTokenType.Plus, "+", i, EndOffset: i + 1)); i++; continue;
                case '-': AddToken(new QueryToken(QueryTokenType.Minus, "-", i, EndOffset: i + 1)); i++; continue;
                case '(': AddToken(new QueryToken(QueryTokenType.LParen, "(", i, EndOffset: i + 1)); i++; continue;
                case ')': AddToken(new QueryToken(QueryTokenType.RParen, ")", i, EndOffset: i + 1)); i++; continue;
                case ':': AddToken(new QueryToken(QueryTokenType.Colon, ":", i, EndOffset: i + 1)); i++; continue;
                case '~': AddToken(new QueryToken(QueryTokenType.Tilde, "~", i, EndOffset: i + 1)); i++; continue;
                case '^': AddToken(new QueryToken(QueryTokenType.Caret, "^", i, EndOffset: i + 1)); i++; continue;
                case '=': AddToken(new QueryToken(QueryTokenType.Equal, "=", i, EndOffset: i + 1)); i++; continue;
                case '|': AddToken(new QueryToken(QueryTokenType.Pipe, "|", i, EndOffset: i + 1)); i++; continue;
                case '[': AddToken(new QueryToken(QueryTokenType.OpenSquare, "[", i, EndOffset: i + 1)); i++; continue;
                case ']': AddToken(new QueryToken(QueryTokenType.CloseSquare, "]", i, EndOffset: i + 1)); i++; continue;
                case '{': AddToken(new QueryToken(QueryTokenType.OpenCurly, "{", i, EndOffset: i + 1)); i++; continue;
                case '}': AddToken(new QueryToken(QueryTokenType.CloseCurly, "}", i, EndOffset: i + 1)); i++; continue;
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
                AddToken(new QueryToken(
                    QueryTokenType.Regex,
                    pattern.ToString(),
                    slashOffset,
                    rawPattern,
                    HasEscapes: ContainsEscape(rawPattern),
                    EndOffset: i));
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
                ReadOnlySpan<char> phraseRawSpan = input.AsSpan(start, i - start);
                if (!ContainsEscape(phraseRawSpan))
                {
                    AddToken(QueryToken.FromSource(
                        QueryTokenType.Phrase,
                        input,
                        quoteOffset,
                        start,
                        phraseRawSpan.Length,
                        hasUnescapedWildcard: false,
                        EndOffset: i + 1));
                }
                else
                {
                    string phraseRaw = phraseRawSpan.ToString();
                    AddToken(new QueryToken(
                        QueryTokenType.Phrase,
                        Unescape(phraseRaw.AsSpan()),
                        quoteOffset,
                        phraseRaw,
                        HasEscapes: true,
                        EndOffset: i + 1));
                }
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

                if (!hasEscapes)
                {
                    QueryTokenType type = GetKeywordType(rawSpan);
                    AddToken(QueryToken.FromSource(
                        type,
                        input,
                        start,
                        start,
                        rawSpan.Length,
                        hasUnescapedWildcard,
                        EndOffset: i));
                    continue;
                }

                string raw = rawSpan.ToString();
                string termValue = Unescape(raw.AsSpan());
                AddToken(new QueryToken(
                    QueryTokenType.Term,
                    termValue,
                    start,
                    raw,
                    hasUnescapedWildcard,
                    HasEscapes: true,
                    EndOffset: i));
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

    private static QueryTokenType GetKeywordType(ReadOnlySpan<char> value)
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

internal struct QueryToken
{
    private string? _materialisedValue;
    private readonly string? _sourceText;
    private readonly int _valueStart;
    private readonly int _valueLength;

    public QueryToken(
        QueryTokenType Type,
        string Value,
        int Offset,
        string? RawValue = null,
        bool HasUnescapedWildcard = false,
        bool HasEscapes = false,
        int EndOffset = -1)
    {
        this.Type = Type;
        _materialisedValue = Value;
        _sourceText = null;
        _valueStart = 0;
        _valueLength = Value.Length;
        this.Offset = Offset;
        this.RawValue = RawValue;
        this.HasUnescapedWildcard = HasUnescapedWildcard;
        this.HasEscapes = HasEscapes;
        this.EndOffset = EndOffset;
    }

    private QueryToken(
        QueryTokenType type,
        string sourceText,
        int offset,
        int valueStart,
        int valueLength,
        bool hasUnescapedWildcard,
        int endOffset)
    {
        Type = type;
        _materialisedValue = null;
        _sourceText = sourceText;
        _valueStart = valueStart;
        _valueLength = valueLength;
        Offset = offset;
        RawValue = null;
        HasUnescapedWildcard = hasUnescapedWildcard;
        HasEscapes = false;
        EndOffset = endOffset;
    }

    public static QueryToken FromSource(
        QueryTokenType type,
        string sourceText,
        int offset,
        int valueStart,
        int valueLength,
        bool hasUnescapedWildcard,
        int EndOffset) =>
        new(type, sourceText, offset, valueStart, valueLength, hasUnescapedWildcard, EndOffset);

    public QueryTokenType Type { get; }
    public string Value
    {
        get
        {
            if (_materialisedValue is null)
                _materialisedValue = _sourceText!.Substring(_valueStart, _valueLength);
            return _materialisedValue;
        }
    }

    public ReadOnlySpan<char> ValueSpan =>
        _sourceText is null
            ? _materialisedValue.AsSpan()
            : _sourceText.AsSpan(_valueStart, _valueLength);

    public ReadOnlySpan<char> RawSpan => RawValue is null ? ValueSpan : RawValue.AsSpan();

    public int Offset { get; }
    public string? RawValue { get; }
    public bool HasUnescapedWildcard { get; }
    public bool HasEscapes { get; }
    public int EndOffset { get; }
    public bool IsSourceBackedValue => _sourceText is not null;
    public string Raw => RawValue ?? Value;
    public QuerySourceSpan SourceSpan => new(Offset, EndOffset < Offset ? Offset : EndOffset);
}
