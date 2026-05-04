using Rowles.LeanLucene.Analysis.Analysers;
namespace Rowles.LeanLucene.Search.Parsing;

/// <summary>
/// Parses a query string into a Query object tree.
/// Supports: term, field:term, "phrase", +required, -excluded, (grouping),
/// prefix*, wild?card, fuzzy~N, "phrase"~N, field:term^boost.
/// </summary>
public sealed class QueryParser
{
    private readonly string _defaultField;
    private readonly IAnalyser _analyser;
    private readonly bool _lenient;

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
        var clauses = new List<BooleanClause>();

        while (pos < tokens.Count)
        {
            if (tokens[pos].Type == QTokenType.RParen)
                break;

            var occur = Occur.Should;
            int operatorOffset = tokens[pos].Offset;
            if (tokens[pos].Type == QTokenType.Plus)
            {
                occur = Occur.Must;
                pos++;
            }
            else if (tokens[pos].Type == QTokenType.Minus)
            {
                occur = Occur.MustNot;
                pos++;
            }

            if (pos >= tokens.Count)
            {
                if (_lenient) break;
                throw new QueryParseException(
                    "A required or prohibited operator must be followed by a query clause.",
                    operatorOffset);
            }

            Query? subQuery;
            if (_lenient)
            {
                try { subQuery = ParseClause(tokens, ref pos); }
                catch (QueryParseException) { break; }
            }
            else
            {
                subQuery = ParseClause(tokens, ref pos);
            }

            if (subQuery is not null)
                clauses.Add(new BooleanClause(subQuery, occur));
        }

        if (clauses.Count == 1 && clauses[0].Occur == Occur.Should)
            return clauses[0].Query;

        var builder = new BooleanQuery.Builder();
        foreach (var c in clauses)
            builder.Add(c.Query, c.Occur);
        return builder.Build();
    }

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

    private PhraseQuery BuildPhraseQuery(string field, string phraseText)
    {
        var analysedTokens = _analyser.Analyse(phraseText.AsSpan());
        var terms = analysedTokens.Select(t => t.Text).ToArray();
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
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                float.TryParse(tokens[pos].Value, System.Globalization.CultureInfo.InvariantCulture, out float boost))
            {
                query.Boost = boost;
                pos++;
            }
        }
        return query;
    }

    private string AnalyseTerm(string term)
    {
        var tokens = _analyser.Analyse(term.AsSpan());
        return tokens.Count > 0 ? tokens[0].Text : string.Empty;
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

            // Regular term
            {
                int start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i]) &&
                       input[i] != '(' && input[i] != ')' && input[i] != ':' &&
                       input[i] != '"' && input[i] != '~' && input[i] != '^')
                {
                    i++;
                }
                tokens.Add(new QToken(QTokenType.Term, input[start..i], start));
            }
        }

        return tokens;
    }

    private enum QTokenType { Term, Phrase, Plus, Minus, LParen, RParen, Colon, Tilde, Caret }

    private readonly record struct QToken(QTokenType Type, string Value, int Offset);
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
