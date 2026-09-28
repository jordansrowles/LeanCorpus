namespace Rowles.LeanCorpus.Search.Parsing;

internal sealed class QuerySyntaxParser
{
    private readonly string _defaultField;
    private readonly QueryParserOptions _options;
    private readonly QueryLexer _lexer;
    private readonly QuerySyntaxBudget _syntaxBudget;
    private readonly bool _parseLimitsAreComplexity;
    private readonly int _maxDepth;
    private int _depth;

    internal QuerySyntaxParser(
        string defaultField,
        QueryParserOptions options,
        QueryLexer lexer,
        QuerySyntaxBudget syntaxBudget,
        bool parseLimitsAreComplexity)
    {
        _defaultField = defaultField;
        _options = options;
        _lexer = lexer;
        _syntaxBudget = syntaxBudget;
        _parseLimitsAreComplexity = parseLimitsAreComplexity;
        _maxDepth = options.MaxSyntaxDepth;
    }

    internal QuerySyntax Parse(string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return new EmptyQuerySyntax();

        List<QueryToken> tokens = _lexer.Lex(queryString);
        int pos = 0;
        ParsedSyntaxClause parsed = ParseExpression(tokens, ref pos);
        if (pos < tokens.Count)
        {
            QueryToken token = tokens[pos];
            throw new QueryParseException(
                $"Unexpected token '{token.Value}' at UTF-16 offset {token.Offset}.", token.Offset);
        }

        return parsed.Query ?? new EmptyQuerySyntax();
    }

    private void ThrowQueryParseLimitExceeded(string message, int? offset = null) =>
        _syntaxBudget.ThrowQueryParseLimitExceeded(message, offset);

    private T CreateSyntaxNode<T>(T syntax) where T : QuerySyntax =>
        _syntaxBudget.CreateSyntaxNode(syntax);

    private T CreateSyntaxNode<T>(T syntax, QuerySourceSpan sourceSpan) where T : QuerySyntax =>
        _syntaxBudget.CreateSyntaxNode((T)syntax.WithSourceSpan(sourceSpan));

    private ParsedSyntaxClause ParseExpression(List<QueryToken> tokens, ref int pos)
    {
        if (++_depth > _maxDepth)
        {
            _depth--;
            string message = $"Query nesting depth exceeds the maximum of {_maxDepth}. Simplify the query by reducing nested parentheses.";
            int? offset = pos < tokens.Count ? tokens[pos].Offset : null;
            if (_parseLimitsAreComplexity)
                throw new QueryParseLimitException(message, offset);
            if (offset is int value)
                throw new QueryParseException(message, value);
            throw new QueryParseException(message);
        }

        try
        {
            var parsed = ParseDisjunction(tokens, ref pos);
            if (parsed.Query is null)
                return new ParsedSyntaxClause(new EmptyQuerySyntax(), Occur.Should);
            if (parsed.Occur == Occur.Should)
                return parsed;
            return new ParsedSyntaxClause(
                CreateSyntaxNode(
                    new BooleanQuerySyntax([new QuerySyntaxClause(parsed.Query, parsed.Occur)]),
                    parsed.Query.SourceSpan),
                Occur.Should);
        }
        finally
        {
            _depth--;
        }
    }

    private ParsedSyntaxClause ParseDisjunction(List<QueryToken> tokens, ref int pos)
    {
        ParsedSyntaxClause first = ParseConjunction(tokens, ref pos);
        List<ParsedSyntaxClause>? additionalClauses = null;
        bool allOperatorsArePipe = true;

        while (pos < tokens.Count && tokens[pos].Type != QueryTokenType.RParen)
        {
            QueryTokenType op;
            if (tokens[pos].Type is QueryTokenType.Or or QueryTokenType.Pipe)
            {
                op = tokens[pos].Type;
                pos++;
            }
            else if (CanStartClause(tokens[pos].Type))
            {
                op = QueryTokenType.Or;
            }
            else
            {
                break;
            }

            var next = ParseConjunction(tokens, ref pos);
            if (next.Query is null)
                continue;
            allOperatorsArePipe &= op == QueryTokenType.Pipe;

            if (first.Query is null)
            {
                first = next;
                continue;
            }

            additionalClauses ??= [first];
            additionalClauses.Add(next);
        }

        if (first.Query is null)
            return default;
        if (additionalClauses is null)
            return first;

        bool allShould = first.Occur == Occur.Should;
        for (int index = 1; allShould && index < additionalClauses.Count; index++)
            allShould = additionalClauses[index].Occur == Occur.Should;

        if (allOperatorsArePipe && allShould)
        {
            QuerySourceSpan sourceSpan = QuerySourceSpan.Cover(
                first.Query.SourceSpan,
                additionalClauses[^1].Query!.SourceSpan);
            var clauses = new QuerySyntax[additionalClauses.Count];
            clauses[0] = first.Query;
            for (int index = 1; index < additionalClauses.Count; index++)
                clauses[index] = additionalClauses[index].Query!;
            return new ParsedSyntaxClause(
                CreateSyntaxNode(new DisjunctionMaxQuerySyntax(clauses), sourceSpan),
                Occur.Should);
        }

        QuerySourceSpan booleanSpan = QuerySourceSpan.Cover(
            first.Query.SourceSpan,
            additionalClauses[^1].Query!.SourceSpan);
        var booleanClauses = new QuerySyntaxClause[additionalClauses.Count];
        booleanClauses[0] = new QuerySyntaxClause(first.Query, first.Occur);
        for (int index = 1; index < additionalClauses.Count; index++)
        {
            ParsedSyntaxClause clause = additionalClauses[index];
            booleanClauses[index] = new QuerySyntaxClause(clause.Query!, clause.Occur);
        }
        return new ParsedSyntaxClause(
            CreateSyntaxNode(new BooleanQuerySyntax(booleanClauses), booleanSpan),
            Occur.Should);
    }

    private ParsedSyntaxClause ParseConjunction(List<QueryToken> tokens, ref int pos)
    {
        var first = ParseUnary(tokens, ref pos);
        if (first.Query is null)
            return first;

        List<ParsedSyntaxClause>? clauses = null;
        while (pos < tokens.Count && tokens[pos].Type is QueryTokenType.And or QueryTokenType.Not)
        {
            var op = tokens[pos].Type;
            int operatorOffset = tokens[pos].Offset;
            pos++;
            if (pos >= tokens.Count || tokens[pos].Type == QueryTokenType.RParen)
            {
                throw new QueryParseException(
                    $"A boolean operator at UTF-16 offset {operatorOffset} must be followed by a query clause.",
                    operatorOffset);
            }
            var next = ParseUnary(tokens, ref pos);
            if (next.Query is null || next.State is QueryClauseState.SyntaxMissing or QueryClauseState.RecoveredError)
            {
                throw new QueryParseException(
                    "A boolean operator must be followed by a query clause.", operatorOffset);
            }

            clauses ??= [new ParsedSyntaxClause(first.Query, PromoteForConjunction(first.Occur))];
            var nextOccur = op == QueryTokenType.Not
                ? Occur.MustNot
                : PromoteForConjunction(next.Occur);
            clauses.Add(new ParsedSyntaxClause(next.Query, nextOccur));
        }

        if (clauses is null)
            return first;

        QuerySourceSpan sourceSpan = QuerySourceSpan.Cover(
            first.Query.SourceSpan,
            clauses[^1].Query!.SourceSpan);
        return new ParsedSyntaxClause(
            CreateSyntaxNode(new BooleanQuerySyntax(clauses.Select(static clause => new QuerySyntaxClause(clause.Query!, clause.Occur)).ToArray()), sourceSpan),
            Occur.Should);
    }

    private ParsedSyntaxClause ParseUnary(List<QueryToken> tokens, ref int pos)
    {
        var occur = Occur.Should;
        int operatorOffset = pos < tokens.Count ? tokens[pos].Offset : 0;
        if (pos < tokens.Count)
        {
            switch (tokens[pos].Type)
            {
                case QueryTokenType.Plus:
                    occur = Occur.Must;
                    pos++;
                    break;
                case QueryTokenType.Minus:
                case QueryTokenType.Not:
                    occur = Occur.MustNot;
                    pos++;
                    break;
            }
        }

        if (pos >= tokens.Count || tokens[pos].Type == QueryTokenType.RParen)
        {
            throw new QueryParseException(
                "A required or prohibited operator must be followed by a query clause.",
                operatorOffset);
        }

        QuerySyntax? query = ParseClause(tokens, ref pos);
        if (query is null)
            throw new QueryParseException("A query clause is required.", operatorOffset);
        return new ParsedSyntaxClause(query, occur, QueryClauseState.Parsed);
    }

    private static bool CanStartClause(QueryTokenType type) =>
        type is QueryTokenType.Term or QueryTokenType.Phrase or QueryTokenType.Regex
            or QueryTokenType.LParen or QueryTokenType.OpenSquare or QueryTokenType.OpenCurly
            or QueryTokenType.Plus or QueryTokenType.Minus or QueryTokenType.Not;

    private static Occur PromoteForConjunction(Occur occur) =>
        occur == Occur.Should ? Occur.Must : occur;

    private QuerySyntax? ParseClause(List<QueryToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count) return null;

        // Parenthetical grouping
        if (tokens[pos].Type == QueryTokenType.LParen)
        {
            QueryToken openToken = tokens[pos];
            pos++; // consume '('
            var inner = ParseExpression(tokens, ref pos);
            QueryToken closeToken;
            if (pos < tokens.Count && tokens[pos].Type == QueryTokenType.RParen)
                closeToken = tokens[pos++]; // consume ')'
            else
                throw new QueryParseException("Unmatched opening parenthesis.", openToken.Offset);
            QuerySourceSpan groupSpan = QuerySourceSpan.Cover(openToken.SourceSpan, closeToken.SourceSpan);
            return ApplyBoost(CreateSyntaxNode(new GroupQuerySyntax(inner.Query!), groupSpan), tokens, ref pos);
        }

        // Quoted phrase
        if (tokens[pos].Type == QueryTokenType.Phrase)
        {
            QueryToken phraseToken = tokens[pos];
            var phrase = phraseToken.Value;
            pos++;
            string field = _defaultField;

            int slop = ReadSlop(tokens, ref pos);
            return ApplyBoost(
                CreateSyntaxNode(new PhraseQuerySyntax(field, phrase, slop, RawText: phraseToken.Raw), phraseToken.SourceSpan),
                tokens,
                ref pos);
        }

        if (tokens[pos].Type == QueryTokenType.Regex)
        {
            var token = tokens[pos];
            var query = CreateSyntaxNode(new RegexpQuerySyntax(_defaultField, token.Value), token.SourceSpan);
            pos++;
            return ApplyBoost(query, tokens, ref pos);
        }

        if (tokens[pos].Type is QueryTokenType.OpenSquare or QueryTokenType.OpenCurly)
            return ApplyBoost(ParseRange(_defaultField, tokens, ref pos), tokens, ref pos);

        // Term (possibly with field: prefix)
        if (tokens[pos].Type == QueryTokenType.Term)
        {
            string field = _defaultField;
            QueryToken termToken = tokens[pos];
            string term;
            int termOffset = termToken.Offset;
            pos++;

            // Check for field:value
            if (pos < tokens.Count && tokens[pos].Type == QueryTokenType.Colon)
            {
                pos++; // consume ':'

                if (termToken.ValueSpan.Equals("_exists_", StringComparison.Ordinal))
                {
                    if (pos < tokens.Count && tokens[pos].Type == QueryTokenType.Term)
                    {
                        QueryToken fieldToken = tokens[pos];
                        var exists = CreateSyntaxNode(new FieldExistsQuerySyntax(fieldToken.Value),
                            QuerySourceSpan.Cover(termToken.SourceSpan, fieldToken.SourceSpan));
                        pos++;
                        return ApplyBoost(exists, tokens, ref pos);
                    }
                    throw new QueryParseException(
                        "_exists_ must be followed by a field name.", termOffset);
                }

                field = termToken.Value;

                if (pos < tokens.Count)
                {
                    if (tokens[pos].Type == QueryTokenType.Phrase)
                    {
                        QueryToken phraseToken = tokens[pos];
                        var phrase = phraseToken.Value;
                        pos++;
                        int slop = ReadSlop(tokens, ref pos);
                        var pq = CreateSyntaxNode(
                            new PhraseQuerySyntax(field, phrase, slop, RawText: phraseToken.Raw), phraseToken.SourceSpan);
                        return ApplyBoost(pq, tokens, ref pos);
                    }
                    else if (tokens[pos].Type == QueryTokenType.Regex)
                    {
                        QueryToken regexToken = tokens[pos];
                        var regex = CreateSyntaxNode(new RegexpQuerySyntax(field, regexToken.Value), regexToken.SourceSpan);
                        pos++;
                        return ApplyBoost(regex, tokens, ref pos);
                    }
                    else if (tokens[pos].Type is QueryTokenType.OpenSquare or QueryTokenType.OpenCurly)
                    {
                        var range = ParseRange(field, tokens, ref pos);
                        return ApplyBoost(range, tokens, ref pos);
                    }
                    else if (tokens[pos].Type == QueryTokenType.Term)
                    {
                        termToken = tokens[pos];
                        term = termToken.Value;
                        pos++;
                    }
                    else
                    {
                        throw new QueryParseException(
                            $"Field '{field}' must be followed by a term or phrase.",
                            tokens[pos].Offset);
                    }
                }
                else
                {
                    throw new QueryParseException(
                        $"Field '{field}' must be followed by a term or phrase.", termOffset);
                }
            }
            else
            {
                term = termToken.Value;
            }

            // Check for wildcard/prefix/fuzzy suffixes
            if (termToken.HasUnescapedWildcard)
            {
                var multiTerm = CreateSyntaxNode(new MultiTermQuerySyntax(field, termToken.Raw, term), termToken.SourceSpan);
                return ApplyBoost(multiTerm, tokens, ref pos);
            }

            // Check for fuzzy ~ suffix
            if (pos < tokens.Count && tokens[pos].Type == QueryTokenType.Tilde)
            {
                int modifierOffset = tokens[pos].Offset;
                int suffixPosition = pos + 1;
                int maxEdits = 0;
                bool hasEditDistance = suffixPosition < tokens.Count &&
                    tokens[suffixPosition].Type == QueryTokenType.Term &&
                    int.TryParse(
                        tokens[suffixPosition].Value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out maxEdits);
                if (!hasEditDistance || maxEdits is < 0 or > 2)
                {
                    throw new QueryParseException(
                        $"Fuzzy edit distance must be an integer between 0 and {_options.MaxFuzzyEdits}.",
                        modifierOffset);
                }
                if (maxEdits > _options.MaxFuzzyEdits)
                {
                    ThrowQueryParseLimitExceeded(
                        $"Fuzzy edit distance exceeds the configured maximum of {_options.MaxFuzzyEdits}.",
                        modifierOffset);
                }

                pos = suffixPosition + 1;
                QuerySourceSpan fuzzySpan = QuerySourceSpan.Cover(termToken.SourceSpan, tokens[suffixPosition].SourceSpan);
                var fuzzy = CreateSyntaxNode(new UnanalysedFuzzyQuerySyntax(field, term, maxEdits, modifierOffset), fuzzySpan);
                return ApplyBoost(fuzzy, tokens, ref pos);
            }

            // Recognise the term now; analysis lowering runs after the complete syntax tree exists.
            var unanalysedTerm = CreateSyntaxNode(new UnanalysedTermQuerySyntax(field, term), termToken.SourceSpan);
            return ApplyBoost(unanalysedTerm, tokens, ref pos);
        }

        throw new QueryParseException(
            $"Unexpected token '{tokens[pos].Value}' at UTF-16 offset {tokens[pos].Offset}.", tokens[pos].Offset);
    }

    private QuerySyntax ParseRange(string field, List<QueryToken> tokens, ref int pos)
    {
        var opening = tokens[pos];
        bool includeLower = opening.Type == QueryTokenType.OpenSquare;
        pos++;

        if (!TryReadRangeBound(tokens, ref pos, out QueryToken lower))
            throw new QueryParseException("A range query must include a lower bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type != QueryTokenType.To)
            throw new QueryParseException("A range query must separate its bounds with TO.", opening.Offset);
        pos++;
        if (!TryReadRangeBound(tokens, ref pos, out QueryToken upper))
            throw new QueryParseException("A range query must include an upper bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type is not (QueryTokenType.CloseSquare or QueryTokenType.CloseCurly))
            throw new QueryParseException("A range query must end with ']' or '}'.", opening.Offset);

        bool includeUpper = tokens[pos].Type == QueryTokenType.CloseSquare;
        QueryToken closing = tokens[pos];
        pos++;
        string? lowerTerm = IsUnboundedRangeMarker(lower) ? null : lower.Value;
        string? upperTerm = IsUnboundedRangeMarker(upper) ? null : upper.Value;
        return CreateSyntaxNode(new TermRangeQuerySyntax(
            field,
            lowerTerm,
            upperTerm,
            includeLower,
            includeUpper), QuerySourceSpan.Cover(opening.SourceSpan, closing.SourceSpan));
    }

    private static bool TryReadRangeBound(List<QueryToken> tokens, ref int pos, out QueryToken value)
    {
        if (pos < tokens.Count && tokens[pos].Type is QueryTokenType.Term or QueryTokenType.Phrase)
        {
            value = tokens[pos];
            pos++;
            return true;
        }
        value = default;
        return false;
    }

    private static bool IsUnboundedRangeMarker(QueryToken token) =>
        token.Type == QueryTokenType.Term && token.RawSpan.SequenceEqual("*");

    private int ReadSlop(List<QueryToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Type != QueryTokenType.Tilde)
            return 0;

        int modifierOffset = tokens[pos].Offset;
        int suffixPosition = pos + 1;
        int slop = 0;
        bool hasSlop = suffixPosition < tokens.Count &&
            tokens[suffixPosition].Type == QueryTokenType.Term &&
            int.TryParse(
                tokens[suffixPosition].Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out slop);
        if (!hasSlop || slop < 0 || slop > PhraseQuery.MaximumSlop)
        {
            throw new QueryParseException(
                $"Phrase slop must be an integer between 0 and {_options.MaxPhraseSlop}.",
                modifierOffset);
        }
        if (slop > _options.MaxPhraseSlop)
        {
            ThrowQueryParseLimitExceeded(
                $"Phrase slop exceeds the configured maximum of {_options.MaxPhraseSlop}.",
                modifierOffset);
        }

        pos = suffixPosition + 1;
        return slop;
    }

    private QuerySyntax ApplyBoost(QuerySyntax query, List<QueryToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Type != QueryTokenType.Caret)
            return query;

        int modifierOffset = tokens[pos].Offset;
        int suffixPosition = pos + 1;
        bool constantScore = suffixPosition < tokens.Count && tokens[suffixPosition].Type == QueryTokenType.Equal;
        if (constantScore)
            suffixPosition++;

        if (suffixPosition >= tokens.Count ||
            tokens[suffixPosition].Type != QueryTokenType.Term ||
            !float.TryParse(
                tokens[suffixPosition].Value,
                System.Globalization.CultureInfo.InvariantCulture,
                out float boost) ||
            !float.IsFinite(boost))
        {
            throw new QueryParseException("Boost modifiers require a finite numeric value.", modifierOffset);
        }

        pos = suffixPosition + 1;
        QuerySourceSpan boostSpan = QuerySourceSpan.Cover(query.SourceSpan, tokens[suffixPosition].SourceSpan);
        QuerySyntax syntax = new BoostQuerySyntax(query, boost, constantScore).WithSourceSpan(boostSpan);
        return constantScore ? CreateSyntaxNode(syntax) : syntax;
    }


    private readonly record struct ParsedSyntaxClause(
        QuerySyntax? Query,
        Occur Occur,
        QueryClauseState State = QueryClauseState.Parsed);
}

internal sealed class QuerySyntaxBudget(QueryParserOptions options, bool limitsAreComplexity)
{
    private int _queryClauseCount;
    private int _syntaxNodeCount;
    private bool _countQueryClauses;

    internal void BeginQueryClauseCounting() => _countQueryClauses = true;

    internal void EndQueryClauseCounting() => _countQueryClauses = false;

    internal T CreateSyntaxNode<T>(T syntax) where T : QuerySyntax
    {
        if (_syntaxNodeCount >= options.MaxSyntaxNodes)
            ThrowQueryParseLimitExceeded(
                $"The query exceeds the configured syntax-node limit of {options.MaxSyntaxNodes}.",
                syntax.SourceSpan.Length == 0 ? null : syntax.SourceSpan.Start);
        _syntaxNodeCount++;

        if (_countQueryClauses && IsQueryClauseNode(syntax))
            ConsumeQueryClauses(1, syntax.SourceSpan.Length == 0 ? null : syntax.SourceSpan.Start);

        return syntax;
    }

    internal void ConsumeQueryClauses(int count, int? offset = null)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count > options.MaxQueryClauses - _queryClauseCount)
            ThrowQueryParseLimitExceeded(
                $"The query exceeds the configured query-clause limit of {options.MaxQueryClauses}.", offset);

        _queryClauseCount += count;
    }

    internal void ThrowQueryParseLimitExceeded(string message, int? offset = null)
    {
        if (limitsAreComplexity)
            throw new QueryParseLimitException(message, offset);

        if (offset is int value)
            throw new QueryParseException(message, value);

        throw new QueryParseException(message);
    }

    private static bool IsQueryClauseNode(QuerySyntax syntax) => syntax is
        AnalysedEmptyQuerySyntax or RecoveredQuerySyntax or BooleanQuerySyntax or
        DisjunctionMaxQuerySyntax or TermQuerySyntax or FuzzyQuerySyntax or
        MultiTermQuerySyntax or PhraseQuerySyntax or RegexpQuerySyntax or
        TermRangeQuerySyntax or FieldExistsQuerySyntax ||
        syntax is BoostQuerySyntax { ConstantScore: true };
}
