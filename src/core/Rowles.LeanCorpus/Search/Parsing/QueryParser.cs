using Rowles.LeanCorpus.Analysis.Analysers;
namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>
/// Parses a query string into a Query object tree.
/// Supports: term, field:term, "phrase", +required, -excluded, (grouping),
/// explicit boolean operators, ranges, regular expressions, field existence,
/// prefix*, wild?card, fuzzy~N, "phrase"~N, boosts, and constant scores.
/// </summary>
public sealed class QueryParser
{
    private readonly string _defaultField;
    private readonly IAnalyser _analyser;
    private readonly bool _lenient;
    private int _depth;

    /// <summary>Initialises a new <see cref="QueryParser"/> with the given default field and analyser.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="lenient">
    /// When <see langword="true"/>, syntax errors are tolerated and the parser returns the best-effort
    /// result built from valid tokens. When <see langword="false"/> (default), syntax errors throw
    /// <see cref="QueryParseException"/>.
    /// </param>
    public QueryParser(string defaultField, IAnalyser analyser, bool lenient = false)
    {
        _defaultField = defaultField;
        _analyser = analyser;
        _lenient = lenient;
    }

    /// <summary>Parses the query string into a <see cref="Query"/> object tree.</summary>
    /// <param name="queryString">The query string to parse.</param>
    /// <returns>
    /// A <see cref="Query"/> representing the parsed expression, or an empty
    /// <see cref="BooleanQuery"/> when <paramref name="queryString"/> is null or whitespace.
    /// </returns>
    /// <exception cref="QueryParseException">
    /// Thrown when the query string contains a syntax error and the parser is not in lenient mode.
    /// </exception>
    public Query Parse(string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return new BooleanQuery.Builder().Build();

        var tokens = Tokenize(queryString, _lenient);
        int pos = 0;
        Query query;
        if (_lenient)
        {
            try { query = ParseExpression(tokens, ref pos); }
            catch (QueryParseException) { query = new BooleanQuery.Builder().Build(); }
        }
        else
        {
            query = ParseExpression(tokens, ref pos);
            if (pos < tokens.Count)
            {
                var tok = tokens[pos];
                throw new QueryParseException(
                    $"Unexpected token '{tok.Value}' at position {pos}.", tok.Offset);
            }
        }
        return query;
    }

    private Query ParseExpression(List<QToken> tokens, ref int pos)
    {
        const int maxDepth = 64;
        if (++_depth > maxDepth)
        {
            _depth--;
            throw new QueryParseException(
                $"Query nesting depth exceeds the maximum of {maxDepth}. " +
                "Simplify the query by reducing nested parentheses.");
        }

        try
        {
            var parsed = ParseDisjunction(tokens, ref pos);
            if (parsed.Query is null)
                return new BooleanQuery.Builder().Build();
            if (parsed.Occur == Occur.Should)
                return parsed.Query;
            return new BooleanQuery.Builder()
                .Add(parsed.Query, parsed.Occur)
                .Build();
        }
        finally
        {
            _depth--;
        }
    }

    private ParsedClause ParseDisjunction(List<QToken> tokens, ref int pos)
    {
        var clauses = new List<ParsedClause>();
        var operators = new List<QTokenType>();

        var first = ParseConjunction(tokens, ref pos);
        if (first.Query is not null)
            clauses.Add(first);

        while (pos < tokens.Count && tokens[pos].Type != QTokenType.RParen)
        {
            QTokenType op;
            if (tokens[pos].Type is QTokenType.Or or QTokenType.Pipe)
            {
                op = tokens[pos].Type;
                pos++;
            }
            else if (CanStartClause(tokens[pos].Type))
            {
                op = QTokenType.Or;
            }
            else
            {
                break;
            }

            var next = ParseConjunction(tokens, ref pos);
            if (next.Query is null)
                continue;
            operators.Add(op);
            clauses.Add(next);
        }

        if (clauses.Count == 0)
            return default;
        if (clauses.Count == 1)
            return clauses[0];

        if (operators.Count > 0 && operators.All(static op => op == QTokenType.Pipe)
            && clauses.All(static clause => clause.Occur == Occur.Should))
        {
            var disMax = new DisjunctionMaxQuery.Builder();
            foreach (var clause in clauses)
                disMax.Add(clause.Query!);
            return new ParsedClause(disMax.Build(), Occur.Should);
        }

        var builder = new BooleanQuery.Builder();
        foreach (var clause in clauses)
            builder.Add(clause.Query!, clause.Occur);
        return new ParsedClause(builder.Build(), Occur.Should);
    }

    private ParsedClause ParseConjunction(List<QToken> tokens, ref int pos)
    {
        var first = ParseUnary(tokens, ref pos);
        if (first.Query is null)
            return first;

        List<ParsedClause>? clauses = null;
        while (pos < tokens.Count && tokens[pos].Type is QTokenType.And or QTokenType.Not)
        {
            var op = tokens[pos].Type;
            int operatorOffset = tokens[pos].Offset;
            pos++;
            var next = ParseUnary(tokens, ref pos);
            if (next.Query is null)
            {
                if (_lenient)
                    break;
                throw new QueryParseException(
                    "A boolean operator must be followed by a query clause.", operatorOffset);
            }

            clauses ??= [new ParsedClause(first.Query, PromoteForConjunction(first.Occur))];
            var nextOccur = op == QTokenType.Not
                ? Occur.MustNot
                : PromoteForConjunction(next.Occur);
            clauses.Add(new ParsedClause(next.Query, nextOccur));
        }

        if (clauses is null)
            return first;

        var builder = new BooleanQuery.Builder();
        foreach (var clause in clauses)
            builder.Add(clause.Query!, clause.Occur);
        return new ParsedClause(builder.Build(), Occur.Should);
    }

    private ParsedClause ParseUnary(List<QToken> tokens, ref int pos)
    {
        var occur = Occur.Should;
        int operatorOffset = pos < tokens.Count ? tokens[pos].Offset : 0;
        if (pos < tokens.Count)
        {
            switch (tokens[pos].Type)
            {
                case QTokenType.Plus:
                    occur = Occur.Must;
                    pos++;
                    break;
                case QTokenType.Minus:
                case QTokenType.Not:
                    occur = Occur.MustNot;
                    pos++;
                    break;
            }
        }

        if (pos >= tokens.Count || tokens[pos].Type == QTokenType.RParen)
        {
            if (_lenient)
                return default;
            throw new QueryParseException(
                "A required or prohibited operator must be followed by a query clause.",
                operatorOffset);
        }

        Query? query;
        if (_lenient)
        {
            try { query = ParseClause(tokens, ref pos); }
            catch (QueryParseException) { return default; }
        }
        else
        {
            query = ParseClause(tokens, ref pos);
        }
        return new ParsedClause(query, occur);
    }

    private static bool CanStartClause(QTokenType type) =>
        type is QTokenType.Term or QTokenType.Phrase or QTokenType.Regex
            or QTokenType.LParen or QTokenType.OpenSquare or QTokenType.OpenCurly
            or QTokenType.Plus or QTokenType.Minus or QTokenType.Not;

    private static Occur PromoteForConjunction(Occur occur) =>
        occur == Occur.Should ? Occur.Must : occur;

    private Query? ParseClause(List<QToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count) return null;

        // Parenthetical grouping
        if (tokens[pos].Type == QTokenType.LParen)
        {
            int openOffset = tokens[pos].Offset;
            pos++; // consume '('
            var inner = ParseExpression(tokens, ref pos);
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.RParen)
                pos++; // consume ')'
            else if (!_lenient)
                throw new QueryParseException("Unmatched opening parenthesis.", openOffset);
            return ApplyBoost(inner, tokens, ref pos);
        }

        // Quoted phrase
        if (tokens[pos].Type == QTokenType.Phrase)
        {
            var phrase = tokens[pos].Value;
            pos++;
            string field = _defaultField;

            var query = BuildPhraseQuery(field, phrase);
            query = ApplySlop(query, tokens, ref pos);
            return ApplyBoost(query, tokens, ref pos);
        }

        if (tokens[pos].Type == QTokenType.Regex)
        {
            var query = new RegexpQuery(_defaultField, tokens[pos].Value);
            pos++;
            return ApplyBoost(query, tokens, ref pos);
        }

        if (tokens[pos].Type is QTokenType.OpenSquare or QTokenType.OpenCurly)
            return ApplyBoost(ParseRange(_defaultField, tokens, ref pos), tokens, ref pos);

        // Term (possibly with field: prefix)
        if (tokens[pos].Type == QTokenType.Term)
        {
            string field = _defaultField;
            string term = tokens[pos].Value;
            int termOffset = tokens[pos].Offset;
            pos++;

            // Check for field:value
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Colon)
            {
                pos++; // consume ':'

                if (string.Equals(term, "_exists_", StringComparison.Ordinal))
                {
                    if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term)
                    {
                        var exists = new FieldExistsQuery(tokens[pos].Value);
                        pos++;
                        return ApplyBoost(exists, tokens, ref pos);
                    }
                    if (_lenient) return null;
                    throw new QueryParseException(
                        "_exists_ must be followed by a field name.", termOffset);
                }

                field = term;

                if (pos < tokens.Count)
                {
                    if (tokens[pos].Type == QTokenType.Phrase)
                    {
                        var phrase = tokens[pos].Value;
                        pos++;
                        var pq = BuildPhraseQuery(field, phrase);
                        pq = ApplySlop(pq, tokens, ref pos);
                        return ApplyBoost(pq, tokens, ref pos);
                    }
                    else if (tokens[pos].Type == QTokenType.Regex)
                    {
                        var regex = new RegexpQuery(field, tokens[pos].Value);
                        pos++;
                        return ApplyBoost(regex, tokens, ref pos);
                    }
                    else if (tokens[pos].Type is QTokenType.OpenSquare or QTokenType.OpenCurly)
                    {
                        var range = ParseRange(field, tokens, ref pos);
                        return ApplyBoost(range, tokens, ref pos);
                    }
                    else if (tokens[pos].Type == QTokenType.Term)
                    {
                        term = tokens[pos].Value;
                        pos++;
                    }
                    else
                    {
                        if (_lenient) return null;
                        throw new QueryParseException(
                            $"Field '{field}' must be followed by a term or phrase.",
                            tokens[pos].Offset);
                    }
                }
                else
                {
                    if (_lenient) return null;
                    throw new QueryParseException(
                        $"Field '{field}' must be followed by a term or phrase.", termOffset);
                }
            }

            // Check for wildcard/prefix/fuzzy suffixes
            if (term.Contains('*') || term.Contains('?'))
            {
                if (term.EndsWith('*') && !term.AsSpan()[..^1].Contains('*') && !term.AsSpan()[..^1].Contains('?'))
                {
                    var q = new PrefixQuery(field, term[..^1]);
                    return ApplyBoost(q, tokens, ref pos);
                }
                var wq = new WildcardQuery(field, term);
                return ApplyBoost(wq, tokens, ref pos);
            }

            // Check for fuzzy ~ suffix
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Tilde)
            {
                pos++;
                int maxEdits = 2;
                if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                    int.TryParse(tokens[pos].Value, out int edits))
                {
                    maxEdits = edits;
                    pos++;
                }
                var analysed = AnalyseTerm(term);
                var fq = new FuzzyQuery(field, analysed, maxEdits);
                return ApplyBoost(fq, tokens, ref pos);
            }

            // Regular term — analyse it
            var analysedTerm = AnalyseTerm(term);
            if (string.IsNullOrEmpty(analysedTerm))
                return null; // stop word removed

            var tq = new TermQuery(field, analysedTerm);
            return ApplyBoost(tq, tokens, ref pos);
        }

        if (_lenient) return null;
        throw new QueryParseException(
            $"Unexpected token '{tokens[pos].Value}' at position {pos}.", tokens[pos].Offset);
    }

    private Query ParseRange(string field, List<QToken> tokens, ref int pos)
    {
        var opening = tokens[pos];
        bool includeLower = opening.Type == QTokenType.OpenSquare;
        pos++;

        if (!TryReadRangeBound(tokens, ref pos, out var lower))
            throw new QueryParseException("A range query must include a lower bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type != QTokenType.To)
            throw new QueryParseException("A range query must separate its bounds with TO.", opening.Offset);
        pos++;
        if (!TryReadRangeBound(tokens, ref pos, out var upper))
            throw new QueryParseException("A range query must include an upper bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type is not (QTokenType.CloseSquare or QTokenType.CloseCurly))
            throw new QueryParseException("A range query must end with ']' or '}'.", opening.Offset);

        bool includeUpper = tokens[pos].Type == QTokenType.CloseSquare;
        pos++;
        return new TermRangeQuery(
            field,
            lower == "*" ? null : lower,
            upper == "*" ? null : upper,
            includeLower,
            includeUpper);
    }

    private static bool TryReadRangeBound(List<QToken> tokens, ref int pos, out string value)
    {
        if (pos < tokens.Count && tokens[pos].Type is QTokenType.Term or QTokenType.Phrase)
        {
            value = tokens[pos].Value;
            pos++;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private PhraseQuery BuildPhraseQuery(string field, string phraseText)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens);
        _analyser.Analyse(phraseText.AsSpan(), sink);
        var terms = tokens.Select(t => t.Text).ToArray();
        return terms.Length > 0 ? new PhraseQuery(field, terms) : new PhraseQuery(field, phraseText.Split(' '));
    }

    private static PhraseQuery ApplySlop(PhraseQuery query, List<QToken> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos].Type == QTokenType.Tilde)
        {
            pos++;
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                int.TryParse(tokens[pos].Value, out int slop))
            {
                query.Slop = slop;
                pos++;
            }
        }
        return query;
    }

    private static Query ApplyBoost(Query query, List<QToken> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos].Type == QTokenType.Caret)
        {
            pos++;
            bool constantScore = pos < tokens.Count && tokens[pos].Type == QTokenType.Equal;
            if (constantScore)
                pos++;
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                float.TryParse(tokens[pos].Value, System.Globalization.CultureInfo.InvariantCulture, out float boost))
            {
                pos++;
                if (constantScore)
                    return new ConstantScoreQuery(query, boost);
                query.Boost = boost;
            }
        }
        return query;
    }

    private string AnalyseTerm(string term)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens);
        _analyser.Analyse(term.AsSpan(), sink);
        return tokens.Count > 0 ? tokens[0].Text : string.Empty;
    }

    private sealed class CapturingSink : Analysis.ISpanTokenSink
    {
        private readonly List<Analysis.Token> _tokens;
        public CapturingSink(List<Analysis.Token> tokens) => _tokens = tokens;
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Analysis.Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
            => _tokens.Add(new Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }

    private static List<QToken> Tokenize(string input, bool lenient)
    {
        var tokens = new List<QToken>();
        int i = 0;

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '+': tokens.Add(new QToken(QTokenType.Plus, "+", i)); i++; continue;
                case '-': tokens.Add(new QToken(QTokenType.Minus, "-", i)); i++; continue;
                case '(': tokens.Add(new QToken(QTokenType.LParen, "(", i)); i++; continue;
                case ')': tokens.Add(new QToken(QTokenType.RParen, ")", i)); i++; continue;
                case ':': tokens.Add(new QToken(QTokenType.Colon, ":", i)); i++; continue;
                case '~': tokens.Add(new QToken(QTokenType.Tilde, "~", i)); i++; continue;
                case '^': tokens.Add(new QToken(QTokenType.Caret, "^", i)); i++; continue;
                case '=': tokens.Add(new QToken(QTokenType.Equal, "=", i)); i++; continue;
                case '|': tokens.Add(new QToken(QTokenType.Pipe, "|", i)); i++; continue;
                case '[': tokens.Add(new QToken(QTokenType.OpenSquare, "[", i)); i++; continue;
                case ']': tokens.Add(new QToken(QTokenType.CloseSquare, "]", i)); i++; continue;
                case '{': tokens.Add(new QToken(QTokenType.OpenCurly, "{", i)); i++; continue;
                case '}': tokens.Add(new QToken(QTokenType.CloseCurly, "}", i)); i++; continue;
            }

            if (c == '/')
            {
                int slashOffset = i++;
                var pattern = new System.Text.StringBuilder();
                bool closed = false;
                while (i < input.Length)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        if (input[i + 1] == '/')
                        {
                            pattern.Append('/');
                            i += 2;
                            continue;
                        }
                        pattern.Append(input[i]);
                        pattern.Append(input[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (input[i] == '/')
                    {
                        i++;
                        closed = true;
                        break;
                    }
                    pattern.Append(input[i++]);
                }
                if (!closed && !lenient)
                    throw new QueryParseException("Unmatched regular expression delimiter.", slashOffset);
                tokens.Add(new QToken(QTokenType.Regex, pattern.ToString(), slashOffset));
                continue;
            }

            if (c == '"')
            {
                int quoteOffset = i;
                i++; // skip opening quote
                int start = i;
                while (i < input.Length && input[i] != '"')
                    i++;
                if (i >= input.Length)
                {
                    if (lenient)
                    {
                        // Treat the unterminated phrase content as a plain term token.
                        tokens.Add(new QToken(QTokenType.Term, input[start..], quoteOffset));
                        continue;
                    }
                    throw new QueryParseException(
                        "Unmatched quote in query string.", quoteOffset);
                }
                tokens.Add(new QToken(QTokenType.Phrase, input[start..i], quoteOffset));
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

                string termValue;
                if (hasEscapes)
                {
                    var raw = input.AsSpan(start, i - start);
                    termValue = Unescape(raw);
                }
                else
                {
                    termValue = input[start..i];
                }

                var type = !hasEscapes ? GetKeywordType(termValue) : QTokenType.Term;
                tokens.Add(new QToken(type, termValue, start));
            }
        }

        return tokens;
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

    private static QTokenType GetKeywordType(string value)
    {
        if (value.Equals("AND", StringComparison.OrdinalIgnoreCase)) return QTokenType.And;
        if (value.Equals("OR", StringComparison.OrdinalIgnoreCase)) return QTokenType.Or;
        if (value.Equals("NOT", StringComparison.OrdinalIgnoreCase)) return QTokenType.Not;
        if (value.Equals("TO", StringComparison.OrdinalIgnoreCase)) return QTokenType.To;
        return QTokenType.Term;
    }

    private enum QTokenType
    {
        Term, Phrase, Regex, Plus, Minus, LParen, RParen, Colon, Tilde, Caret,
        Equal, And, Or, Not, To, Pipe, OpenSquare, CloseSquare, OpenCurly, CloseCurly
    }

    private readonly record struct QToken(QTokenType Type, string Value, int Offset);
    private readonly record struct ParsedClause(Query? Query, Occur Occur);
}

/// <summary>Exception thrown when a query string cannot be parsed.</summary>
public sealed class QueryParseException : FormatException
{
    /// <summary>Gets the zero-based character offset within the query string where the error was detected.</summary>
    public int Offset { get; }

    /// <summary>Initialises a new <see cref="QueryParseException"/> with the supplied message.</summary>
    /// <param name="message">Description of the parse error.</param>
    public QueryParseException(string message) : base(message)
    {
    }

    /// <summary>Initialises a new <see cref="QueryParseException"/> with the supplied message and character offset.</summary>
    /// <param name="message">Description of the parse error.</param>
    /// <param name="offset">Zero-based character offset within the query string where the error was detected.</param>
    public QueryParseException(string message, int offset) : base(message)
    {
        Offset = offset;
    }
}
