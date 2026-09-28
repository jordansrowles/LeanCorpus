using Rowles.LeanCorpus.Analysis.Analysers;
namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>
/// Parses a query string into a Query object tree.
/// Supports: term, field:term, "phrase", +required, -excluded, (grouping),
/// explicit boolean operators, ranges, regular expressions, field existence,
/// prefix*, wild?card, fuzzy~N, "phrase"~N, boosts, and constant scores.
/// </summary>
/// <remarks>
/// When one unquoted syntax term analyses to multiple independent positions, those
/// positions use the parser's implicit OR operator. Same-position alternatives are
/// retained as Boolean alternatives, and non-unit graph edges use bounded phrase-path
/// compilation.
///
/// Each <see cref="Parse(string)"/> call uses independent parser state. Instances created
/// with an <see cref="IAnalyser"/> accept sequential calls and reject overlapping non-empty calls;
/// use the analyser-factory constructor to share a parser across threads. The factory is
/// called once per non-empty parse and must return an analyser instance that is safe for that parse.
/// Custom subclasses must keep per-parse data out of mutable instance fields and should be
/// configured before concurrent parsing begins.
/// </remarks>
public class QueryParser
{
    private readonly string _defaultField;
    private IAnalyser? _analyser;
    private readonly Func<IAnalyser>? _analyserFactory;
    private readonly Func<string, QueryFieldCompilationContext>? _fieldContextResolver;
    private readonly QueryParserOptions _options;
    private Dictionary<string, QueryFieldCompilationContext> _fieldContexts = new(StringComparer.Ordinal);
    private int _depth;
    private int _maxDepth = 64;
    private int _maxSyntaxNodes = int.MaxValue;
    private int _queryClauseCount;
    private int _maxQueryClauses = int.MaxValue;
    private int _syntaxNodeCount;
    private QueryCompilationBudget? _queryCompilationBudget;
    private bool _parseLimitsAreComplexity;
    private bool _graphPathLimitIsComplexity;
    private bool _countQueryClauses;
    private int _parseInProgress;

    /// <summary>Gets the analyser used to build query terms.</summary>
    protected IAnalyser Analyser => _analyser
        ?? throw new InvalidOperationException("An analyser is available only during a parse invocation.");

    /// <summary>Initialises a new <see cref="QueryParser"/> with the given default field and analyser.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="lenient">Must be <see langword="false"/>. Lenient recovery was removed because the parser has no deterministic recovery grammar.</param>
    /// <param name="maxGraphPaths">The maximum complete analysed phrase paths permitted across one parse.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public QueryParser(string defaultField, IAnalyser analyser, bool lenient = false, int maxGraphPaths = 256)
        : this(defaultField, analyser, fieldContextResolver: null, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    /// <summary>Initialises a parser with explicit parser-time budgets.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public QueryParser(string defaultField, IAnalyser analyser, QueryParserOptions options)
        : this(defaultField, analyser, fieldContextResolver: null, ValidateOptions(options))
    {
    }

    /// <summary>
    /// Initialises a parser whose analyser factory is invoked once for each non-empty parse.
    /// This constructor supports concurrent calls on the same parser instance.
    /// </summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for one parse invocation.</param>
    /// <param name="lenient">Must be <see langword="false"/>. Lenient recovery was removed because the parser has no deterministic recovery grammar.</param>
    /// <param name="maxGraphPaths">The maximum complete analysed phrase paths permitted across one parse.</param>
    /// <remarks>
    /// The factory must be safe for concurrent calls and return a fresh analyser per call,
    /// or an analyser that is independently safe for concurrent use.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public QueryParser(string defaultField, Func<IAnalyser> analyserFactory, bool lenient = false, int maxGraphPaths = 256)
        : this(defaultField, analyserFactory, fieldContextResolver: null, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    /// <summary>Initialises a parser with explicit parser-time budgets and a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyserFactory">Creates an analyser for one parse invocation.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public QueryParser(string defaultField, Func<IAnalyser> analyserFactory, QueryParserOptions options)
        : this(defaultField, analyserFactory, fieldContextResolver: null, ValidateOptions(options))
    {
    }

    internal QueryParser(
        string defaultField,
        IAnalyser analyser,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        bool lenient = false,
        int maxGraphPaths = 256)
        : this(defaultField, analyser, fieldContextResolver, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    internal QueryParser(
        string defaultField,
        IAnalyser analyser,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(defaultField);
        ArgumentNullException.ThrowIfNull(analyser);
        _options = ValidateOptions(options);
        _defaultField = defaultField;
        _analyser = analyser;
        _fieldContextResolver = fieldContextResolver;
    }

    internal QueryParser(
        string defaultField,
        Func<IAnalyser> analyserFactory,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        bool lenient = false,
        int maxGraphPaths = 256)
        : this(defaultField, analyserFactory, fieldContextResolver, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    internal QueryParser(
        string defaultField,
        Func<IAnalyser> analyserFactory,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(defaultField);
        ArgumentNullException.ThrowIfNull(analyserFactory);
        _options = ValidateOptions(options);
        _defaultField = defaultField;
        _analyserFactory = analyserFactory;
        _fieldContextResolver = fieldContextResolver;
    }

    private static QueryParserOptions CreateLegacyOptions(bool lenient, int maxGraphPaths)
    {
        EnsureStrictParsing(lenient);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGraphPaths, 1);
        return ValidateOptions(QueryParserOptions.Trusted with { MaxGraphPaths = maxGraphPaths });
    }

    private static QueryParserOptions ValidateOptions(QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Validate();
    }

    private static void EnsureStrictParsing(bool lenient)
    {
        if (lenient)
        {
            throw new NotSupportedException(
                "Lenient parsing has been removed because query recovery has no deterministic clause boundaries. Construct the parser without lenient mode.");
        }
    }

    /// <summary>Parses the query string into a <see cref="Query"/> object tree.</summary>
    /// <param name="queryString">The query string to parse.</param>
    /// <returns>
    /// A <see cref="Query"/> representing the parsed expression, or an empty
    /// <see cref="BooleanQuery"/> when <paramref name="queryString"/> is null or whitespace.
    /// </returns>
    /// <exception cref="QueryParseException">Thrown when the query string contains a syntax error.</exception>
    public Query Parse(string queryString)
    {
        if (queryString is not null && queryString.Length > _options.MaxInputChars)
            throw new QueryParseException($"The query exceeds the configured input character limit of {_options.MaxInputChars}.");
        if (string.IsNullOrWhiteSpace(queryString))
            return new BooleanQuery.Builder().Build();

        bool hasFixedAnalyser = _analyserFactory is null;
        if (hasFixedAnalyser && System.Threading.Interlocked.CompareExchange(ref _parseInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "This QueryParser uses a fixed analyser and cannot parse concurrently. Use an analyser factory to share it across threads.");
        }

        try
        {
            QueryParser invocation = CreateInvocationParser();
            return invocation.CompileSyntax(invocation.ParseSyntax(queryString));
        }
        catch (QueryParseLimitException exception)
        {
            throw new QueryParseException(exception.Message);
        }
        finally
        {
            if (hasFixedAnalyser)
                System.Threading.Volatile.Write(ref _parseInProgress, 0);
        }
    }

    private QueryParser CreateInvocationParser()
    {
        var invocation = (QueryParser)MemberwiseClone();
        invocation._analyser = _analyserFactory is null
            ? _analyser
            : _analyserFactory()
                ?? throw new InvalidOperationException("The analyser factory returned null.");
        invocation._fieldContexts = new Dictionary<string, QueryFieldCompilationContext>(StringComparer.Ordinal);
        invocation._parseInProgress = 0;
        return invocation;
    }

    internal QuerySyntax ParseSyntax(
        string queryString,
        bool limitsAreComplexity = false)
    {
        _parseLimitsAreComplexity = limitsAreComplexity;
        if (queryString is not null && queryString.Length > _options.MaxInputChars)
            ThrowQueryParseLimitExceeded($"The query exceeds the configured input character limit of {_options.MaxInputChars}.");
        if (string.IsNullOrWhiteSpace(queryString))
            return new EmptyQuerySyntax();

        _depth = 0;
        _syntaxNodeCount = 0;
        _queryClauseCount = 0;
        _countQueryClauses = false;
        ResetPhraseGraphBudget();
        _maxDepth = _options.MaxSyntaxDepth;
        _maxSyntaxNodes = _options.MaxSyntaxNodes;
        _maxQueryClauses = _options.MaxQueryClauses;
        var tokens = Tokenize(queryString);
        int pos = 0;
        var parsed = ParseExpression(tokens, ref pos);
        if (pos < tokens.Count)
        {
            var tok = tokens[pos];
            throw new QueryParseException(
                $"Unexpected token '{tok.Value}' at position {pos}.", tok.Offset);
        }
        _countQueryClauses = true;
        try
        {
            return LowerAnalysedSyntax(parsed.Query ?? new EmptyQuerySyntax());
        }
        finally
        {
            _countQueryClauses = false;
        }
    }

    internal QuerySyntax PrepareSyntax(QuerySyntax syntax, Action<int, int> consumeAdditionalClauses, bool graphPathLimitIsComplexity)
    {
        _graphPathLimitIsComplexity = graphPathLimitIsComplexity;
        try
        {
            return Prepare(syntax, consumeAdditionalClauses, depth: 0);
        }
        finally
        {
            _graphPathLimitIsComplexity = false;
        }
    }

    internal Query CompileSyntax(QuerySyntax syntax)
    {
        Query? query = syntax is AnalysedEmptyQuerySyntax ? null : Compile(syntax);
        return query switch
        {
            NoClauseQuery => new MatchNoDocsQuery(),
            null => new BooleanQuery.Builder().Build(),
            _ => query
        };
    }

    private ParsedSyntaxClause ParseExpression(List<QToken> tokens, ref int pos)
    {
        if (++_depth > _maxDepth)
        {
            _depth--;
            string message = $"Query nesting depth exceeds the maximum of {_maxDepth}. Simplify the query by reducing nested parentheses.";
            if (_parseLimitsAreComplexity)
                throw new QueryParseLimitException(message);
            throw new QueryParseException(message);
        }

        try
        {
            var parsed = ParseDisjunction(tokens, ref pos);
            if (parsed.Query is null)
                return new ParsedSyntaxClause(new EmptyQuerySyntax(), Occur.Should);
            if (parsed.Occur == Occur.Should)
                return parsed;
            return new ParsedSyntaxClause(CreateSyntaxNode(new BooleanQuerySyntax([new QuerySyntaxClause(parsed.Query, parsed.Occur)])), Occur.Should);
        }
        finally
        {
            _depth--;
        }
    }

    private ParsedSyntaxClause ParseDisjunction(List<QToken> tokens, ref int pos)
    {
        var clauses = new List<ParsedSyntaxClause>();
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
            return new ParsedSyntaxClause(
                CreateSyntaxNode(new DisjunctionMaxQuerySyntax(clauses.Select(static clause => clause.Query!).ToArray())),
                Occur.Should);
        }

        return new ParsedSyntaxClause(
            CreateSyntaxNode(new BooleanQuerySyntax(clauses.Select(static clause => new QuerySyntaxClause(clause.Query!, clause.Occur)).ToArray())),
            Occur.Should);
    }

    private ParsedSyntaxClause ParseConjunction(List<QToken> tokens, ref int pos)
    {
        var first = ParseUnary(tokens, ref pos);
        if (first.Query is null)
            return first;

        List<ParsedSyntaxClause>? clauses = null;
        while (pos < tokens.Count && tokens[pos].Type is QTokenType.And or QTokenType.Not)
        {
            var op = tokens[pos].Type;
            int operatorOffset = tokens[pos].Offset;
            pos++;
            var next = ParseUnary(tokens, ref pos);
            if (next.Query is null || next.State is QueryClauseState.SyntaxMissing or QueryClauseState.RecoveredError)
            {
                throw new QueryParseException(
                    "A boolean operator must be followed by a query clause.", operatorOffset);
            }

            clauses ??= [new ParsedSyntaxClause(first.Query, PromoteForConjunction(first.Occur))];
            var nextOccur = op == QTokenType.Not
                ? Occur.MustNot
                : PromoteForConjunction(next.Occur);
            clauses.Add(new ParsedSyntaxClause(next.Query, nextOccur));
        }

        if (clauses is null)
            return first;

        return new ParsedSyntaxClause(
            CreateSyntaxNode(new BooleanQuerySyntax(clauses.Select(static clause => new QuerySyntaxClause(clause.Query!, clause.Occur)).ToArray())),
            Occur.Should);
    }

    private ParsedSyntaxClause ParseUnary(List<QToken> tokens, ref int pos)
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
            throw new QueryParseException(
                "A required or prohibited operator must be followed by a query clause.",
                operatorOffset);
        }

        QuerySyntax? query = ParseClause(tokens, ref pos);
        if (query is null)
            throw new QueryParseException("A query clause is required.", operatorOffset);
        return new ParsedSyntaxClause(query, occur, QueryClauseState.Parsed);
    }

    private static bool CanStartClause(QTokenType type) =>
        type is QTokenType.Term or QTokenType.Phrase or QTokenType.Regex
            or QTokenType.LParen or QTokenType.OpenSquare or QTokenType.OpenCurly
            or QTokenType.Plus or QTokenType.Minus or QTokenType.Not;

    private static Occur PromoteForConjunction(Occur occur) =>
        occur == Occur.Should ? Occur.Must : occur;

    private QuerySyntax? ParseClause(List<QToken> tokens, ref int pos)
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
            else
                throw new QueryParseException("Unmatched opening parenthesis.", openOffset);
            return ApplyBoost(new GroupQuerySyntax(inner.Query!), tokens, ref pos);
        }

        // Quoted phrase
        if (tokens[pos].Type == QTokenType.Phrase)
        {
            QToken phraseToken = tokens[pos];
            var phrase = phraseToken.Value;
            pos++;
            string field = _defaultField;

            int slop = ReadSlop(tokens, ref pos);
            return ApplyBoost(
                CreateSyntaxNode(new PhraseQuerySyntax(field, phrase, slop, RawText: phraseToken.Raw)),
                tokens,
                ref pos);
        }

        if (tokens[pos].Type == QTokenType.Regex)
        {
            var query = CreateSyntaxNode(new RegexpQuerySyntax(_defaultField, tokens[pos].Value));
            pos++;
            return ApplyBoost(query, tokens, ref pos);
        }

        if (tokens[pos].Type is QTokenType.OpenSquare or QTokenType.OpenCurly)
            return ApplyBoost(ParseRange(_defaultField, tokens, ref pos), tokens, ref pos);

        // Term (possibly with field: prefix)
        if (tokens[pos].Type == QTokenType.Term)
        {
            string field = _defaultField;
            QToken termToken = tokens[pos];
            string term = termToken.Value;
            int termOffset = termToken.Offset;
            pos++;

            // Check for field:value
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Colon)
            {
                pos++; // consume ':'

                if (string.Equals(term, "_exists_", StringComparison.Ordinal))
                {
                    if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term)
                    {
                        var exists = CreateSyntaxNode(new FieldExistsQuerySyntax(tokens[pos].Value));
                        pos++;
                        return ApplyBoost(exists, tokens, ref pos);
                    }
                    throw new QueryParseException(
                        "_exists_ must be followed by a field name.", termOffset);
                }

                field = term;

                if (pos < tokens.Count)
                {
                    if (tokens[pos].Type == QTokenType.Phrase)
                    {
                        QToken phraseToken = tokens[pos];
                        var phrase = phraseToken.Value;
                        pos++;
                        int slop = ReadSlop(tokens, ref pos);
                        var pq = CreateSyntaxNode(
                            new PhraseQuerySyntax(field, phrase, slop, RawText: phraseToken.Raw));
                        return ApplyBoost(pq, tokens, ref pos);
                    }
                    else if (tokens[pos].Type == QTokenType.Regex)
                    {
                        var regex = CreateSyntaxNode(new RegexpQuerySyntax(field, tokens[pos].Value));
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

            // Check for wildcard/prefix/fuzzy suffixes
            if (termToken.HasUnescapedWildcard)
            {
                var multiTerm = CreateSyntaxNode(new MultiTermQuerySyntax(field, termToken.Raw, term));
                return ApplyBoost(multiTerm, tokens, ref pos);
            }

            // Check for fuzzy ~ suffix
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Tilde)
            {
                int modifierOffset = tokens[pos].Offset;
                int suffixPosition = pos + 1;
                int maxEdits = 0;
                bool hasEditDistance = suffixPosition < tokens.Count &&
                    tokens[suffixPosition].Type == QTokenType.Term &&
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
                var fuzzy = CreateSyntaxNode(new UnanalysedFuzzyQuerySyntax(field, term, maxEdits, modifierOffset));
                return ApplyBoost(fuzzy, tokens, ref pos);
            }

            // Recognise the term now; analysis lowering runs after the complete syntax tree exists.
            var unanalysedTerm = CreateSyntaxNode(new UnanalysedTermQuerySyntax(field, term));
            return ApplyBoost(unanalysedTerm, tokens, ref pos);
        }

        throw new QueryParseException(
            $"Unexpected token '{tokens[pos].Value}' at position {pos}.", tokens[pos].Offset);
    }

    private QuerySyntax LowerAnalysedSyntax(QuerySyntax syntax) => syntax switch
    {
        UnanalysedTermQuerySyntax term => LowerUnanalysedTermSyntax(term.Field, term.Term),
        UnanalysedFuzzyQuerySyntax fuzzy => LowerUnanalysedTermSyntax(
            fuzzy.Field,
            fuzzy.Term,
            fuzzy.MaxEdits,
            fuzzy.ModifierOffset),
        GroupQuerySyntax group => LowerGroupSyntax(group),
        BooleanQuerySyntax boolean => LowerBooleanSyntax(boolean),
        DisjunctionMaxQuerySyntax disjunction => LowerDisjunctionSyntax(disjunction),
        BoostQuerySyntax boost => LowerBoostSyntax(boost),
        _ => CreateSyntaxNode(syntax)
    };

    private QuerySyntax LowerUnanalysedTermSyntax(
        string field,
        string term,
        int? fuzzyMaxEdits = null,
        int modifierOffset = 0)
    {
        QuerySyntax? lowered = LowerAnalysedTokens(
            field,
            term,
            AnalyseTerm(field, term),
            fuzzyMaxEdits,
            modifierOffset);
        return lowered ?? CreateSyntaxNode(new AnalysedEmptyQuerySyntax());
    }

    private QuerySyntax LowerGroupSyntax(GroupQuerySyntax group)
    {
        QuerySyntax inner = LowerAnalysedSyntax(group.Inner);
        return IsEmptySyntax(inner)
            ? inner
            : CreateSyntaxNode(group with { Inner = inner });
    }

    private QuerySyntax LowerBoostSyntax(BoostQuerySyntax boost)
    {
        QuerySyntax inner = LowerAnalysedSyntax(boost.Inner);
        return IsEmptySyntax(inner)
            ? inner
            : CreateSyntaxNode(boost with { Inner = inner });
    }

    private QuerySyntax LowerBooleanSyntax(BooleanQuerySyntax boolean)
    {
        var clauses = new List<QuerySyntaxClause>(boolean.Clauses.Count);
        bool removedEmptyClause = false;
        foreach (QuerySyntaxClause clause in boolean.Clauses)
        {
            QuerySyntax loweredQuery = LowerAnalysedSyntax(clause.Query);
            QueryClauseState state = GetClauseState(loweredQuery);
            QuerySyntaxClause loweredClause = clause with { Query = loweredQuery, State = state };
            if (state is QueryClauseState.SyntaxMissing or QueryClauseState.RecoveredError)
            {
                removedEmptyClause = true;
                continue;
            }

            clauses.Add(loweredClause);
        }

        if (!removedEmptyClause)
            return CreateSyntaxNode(boolean with { Clauses = clauses.ToArray() });
        if (clauses.Count == 0)
            return CreateSyntaxNode(new AnalysedEmptyQuerySyntax());
        if (clauses.Count == 1
            && clauses[0].Occur != Occur.MustNot
            && clauses[0].State != QueryClauseState.AnalysedEmpty)
            return clauses[0].Query;

        return CreateSyntaxNode(new BooleanQuerySyntax(clauses));
    }

    private QuerySyntax LowerDisjunctionSyntax(DisjunctionMaxQuerySyntax disjunction)
    {
        var clauses = new List<QuerySyntax>(disjunction.Clauses.Count);
        bool removedEmptyClause = false;
        foreach (QuerySyntax clause in disjunction.Clauses)
        {
            QuerySyntax loweredClause = LowerAnalysedSyntax(clause);
            if (GetClauseState(loweredClause) is QueryClauseState.SyntaxMissing or QueryClauseState.RecoveredError)
            {
                removedEmptyClause = true;
                continue;
            }

            clauses.Add(loweredClause);
        }

        if (!removedEmptyClause)
            return CreateSyntaxNode(disjunction with { Clauses = clauses.ToArray() });
        return clauses.Count switch
        {
            0 => CreateSyntaxNode(new AnalysedEmptyQuerySyntax()),
            1 when GetClauseState(clauses[0]) != QueryClauseState.AnalysedEmpty => clauses[0],
            _ => CreateSyntaxNode(new DisjunctionMaxQuerySyntax(clauses))
        };
    }

    private static QueryClauseState GetClauseState(QuerySyntax syntax) => syntax switch
    {
        EmptyQuerySyntax => QueryClauseState.SyntaxMissing,
        AnalysedEmptyQuerySyntax => QueryClauseState.AnalysedEmpty,
        RecoveredQuerySyntax => QueryClauseState.RecoveredError,
        _ => QueryClauseState.Parsed
    };

    private static bool IsEmptySyntax(QuerySyntax syntax) =>
        GetClauseState(syntax) != QueryClauseState.Parsed;

    private QuerySyntax ParseRange(string field, List<QToken> tokens, ref int pos)
    {
        var opening = tokens[pos];
        bool includeLower = opening.Type == QTokenType.OpenSquare;
        pos++;

        if (!TryReadRangeBound(tokens, ref pos, out QToken lower))
            throw new QueryParseException("A range query must include a lower bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type != QTokenType.To)
            throw new QueryParseException("A range query must separate its bounds with TO.", opening.Offset);
        pos++;
        if (!TryReadRangeBound(tokens, ref pos, out QToken upper))
            throw new QueryParseException("A range query must include an upper bound.", opening.Offset);
        if (pos >= tokens.Count || tokens[pos].Type is not (QTokenType.CloseSquare or QTokenType.CloseCurly))
            throw new QueryParseException("A range query must end with ']' or '}'.", opening.Offset);

        bool includeUpper = tokens[pos].Type == QTokenType.CloseSquare;
        pos++;
        string? lowerTerm = IsUnboundedRangeMarker(lower) ? null : lower.Value;
        string? upperTerm = IsUnboundedRangeMarker(upper) ? null : upper.Value;
        return CreateSyntaxNode(new TermRangeQuerySyntax(
            field,
            lowerTerm,
            upperTerm,
            includeLower,
            includeUpper));
    }

    private static bool TryReadRangeBound(List<QToken> tokens, ref int pos, out QToken value)
    {
        if (pos < tokens.Count && tokens[pos].Type is QTokenType.Term or QTokenType.Phrase)
        {
            value = tokens[pos];
            pos++;
            return true;
        }
        value = default;
        return false;
    }

    private static bool IsUnboundedRangeMarker(QToken token) =>
        token.Type == QTokenType.Term && string.Equals(token.Raw, "*", StringComparison.Ordinal);

    /// <summary>Builds a phrase query from analysed phrase text.</summary>
    protected virtual Query BuildPhraseQuery(string field, string phraseText, int slop) =>
        CompilePhraseExpansion(field, slop, CreatePhraseExpansion(field, phraseText));

    /// <summary>Builds a phrase query while retaining the raw escaped phrase content.</summary>
    /// <param name="field">The field analysed by the phrase query.</param>
    /// <param name="phraseText">The unescaped phrase content.</param>
    /// <param name="rawPhraseText">The phrase content as it appeared between the quotes.</param>
    /// <param name="slop">The phrase slop.</param>
    /// <returns>The compiled phrase query.</returns>
    private protected virtual Query BuildPhraseQuery(string field, string phraseText, string rawPhraseText, int slop) =>
        BuildPhraseQuery(field, phraseText, slop);

    private PhraseQuerySyntaxExpansion CreatePhraseExpansion(string field, string phraseText)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens, this);
        ResolveFieldContext(field).QueryAnalyser.Analyse(phraseText.AsSpan(), sink);
        return CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: true);
    }

    private PhraseQuerySyntaxExpansion CreatePhraseExpansionFromTokens(
        IReadOnlyList<Analysis.Token> tokens,
        bool tokensAlreadyCounted)
    {
        if (tokens.Count == 0)
            return new PhraseQuerySyntaxExpansion(null, null, 0, 0);

        if (!tokensAlreadyCounted)
        {
            foreach (var _ in tokens)
                ConsumeAnalysedPhraseToken();
        }

        QueryCompilationBudget budget = GetQueryCompilationBudget();
        var graph = new Analysis.TokenGraph();
        foreach (var token in tokens)
        {
            string? edgeLimit = budget.TryReadGraphEdge();
            if (edgeLimit is not null)
                ThrowPhraseGraphLimitExceeded(edgeLimit);
            graph.Add(token);
        }
        graph.ValidateOrdered();

        int start = graph.Edges.Min(static edge => edge.StartPosition);
        int end = graph.Edges.Max(static edge => edge.EndPosition);
        var byStart = graph.Edges.GroupBy(static edge => edge.StartPosition)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        int[] startPositions = byStart.Keys.Order().ToArray();
        var graphPlan = new PhraseGraphPlan(start, end, startPositions, byStart);
        int pathCount = 0;
        int compiledTermCount = 0;
        EnumeratePhraseGraphPaths(graphPlan, path =>
        {
            string? pathLimit = budget.TryEmitPath(path.Count);
            if (pathLimit is not null)
                ThrowPhraseGraphLimitExceeded(pathLimit);

            pathCount++;
            compiledTermCount += path.Count;

            int newBooleanClauses = pathCount == 2 ? 2 : pathCount > 2 ? 1 : 0;
            if (newBooleanClauses > 0)
            {
                string? clauseLimit = budget.TryGenerateBooleanClauses(newBooleanClauses);
                if (clauseLimit is not null)
                    ThrowPhraseGraphLimitExceeded(clauseLimit);
            }
        }, countTraversalSteps: true);

        if (pathCount == 0)
            throw new QueryParseException("Analysed phrase token graph has no complete path.", 0);

        return new PhraseQuerySyntaxExpansion(null, graphPlan, pathCount, compiledTermCount);
    }

    private void EnumeratePhraseGraphPaths(
        PhraseGraphPlan graph,
        Action<IReadOnlyList<Analysis.TokenGraph.TokenEdge>> onCompletePath,
        bool countTraversalSteps)
    {
        var path = new List<Analysis.TokenGraph.TokenEdge>();
        var traversal = new List<PhraseGraphTraversalFrame> { new(graph.StartPosition, pathLength: 0) };
        while (traversal.Count > 0)
        {
            int frameIndex = traversal.Count - 1;
            PhraseGraphTraversalFrame frame = traversal[frameIndex];
            if (path.Count > frame.PathLength)
                path.RemoveRange(frame.PathLength, path.Count - frame.PathLength);

            if (frame.Position == graph.EndPosition)
            {
                onCompletePath(path);
                traversal.RemoveAt(frameIndex);
                continue;
            }

            int nextPositionIndex = Array.BinarySearch(graph.StartPositions, frame.Position);
            if (nextPositionIndex < 0)
                nextPositionIndex = ~nextPositionIndex;
            if (nextPositionIndex == graph.StartPositions.Length)
            {
                traversal.RemoveAt(frameIndex);
                continue;
            }

            // Position increments can leave holes. Continue at the next emitted
            // coordinate, but do not skip a token position that is present.
            Analysis.TokenGraph.TokenEdge[] nextEdges = graph.EdgesByStart[graph.StartPositions[nextPositionIndex]];
            if (frame.NextEdgeIndex >= nextEdges.Length)
            {
                traversal.RemoveAt(frameIndex);
                continue;
            }

            if (countTraversalSteps)
            {
                string? traversalLimit = GetQueryCompilationBudget().TryTakeTraversalStep();
                if (traversalLimit is not null)
                    ThrowPhraseGraphLimitExceeded(traversalLimit);
            }

            Analysis.TokenGraph.TokenEdge edge = nextEdges[frame.NextEdgeIndex];
            frame.NextEdgeIndex++;
            traversal[frameIndex] = frame;
            path.Add(edge);
            traversal.Add(new PhraseGraphTraversalFrame(edge.EndPosition, path.Count));
        }
    }

    private QuerySyntax? LowerAnalysedTokens(
        string field,
        string sourceText,
        IReadOnlyList<Analysis.Token> tokens,
        int? fuzzyMaxEdits = null,
        int modifierOffset = 0)
    {
        if (tokens.Count == 0)
            return null;

        if (tokens.Any(static token => token.PositionLength != 1))
        {
            if (fuzzyMaxEdits.HasValue)
            {
                throw new QueryParseException(
                    "A fuzzy query analyser must not emit multi-position graph edges.", modifierOffset);
            }

            var expansion = CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: false);
            return CreateSyntaxNode(new PhraseQuerySyntax(field, sourceText, 0, expansion));
        }

        var graph = new Analysis.TokenGraph();
        foreach (var token in tokens)
            graph.Add(token);
        graph.ValidateOrdered();

        var positionQueries = new List<QuerySyntax>();
        foreach (var positionGroup in graph.Edges.GroupBy(static edge => edge.StartPosition))
        {
            var alternatives = new List<QuerySyntax>();
            foreach (var edge in positionGroup)
            {
                alternatives.Add(fuzzyMaxEdits is int maxEdits
                    ? CreateSyntaxNode(new FuzzyQuerySyntax(field, edge.Token.Text, maxEdits, modifierOffset))
                    : CreateSyntaxNode(new TermQuerySyntax(field, edge.Token.Text)));
            }

            positionQueries.Add(alternatives.Count == 1
                ? alternatives[0]
                : CreateShouldQuery(alternatives));
        }

        return positionQueries.Count == 1
            ? positionQueries[0]
            : CreateShouldQuery(positionQueries);
    }

    private QuerySyntax CreateShouldQuery(IReadOnlyList<QuerySyntax> queries) =>
        CreateSyntaxNode(new BooleanQuerySyntax(
            queries.Select(static query => new QuerySyntaxClause(query, Occur.Should)).ToArray()));

    private void ConsumeAnalysedPhraseToken()
    {
        string? tokenLimit = GetQueryCompilationBudget().TryConsumePhraseTokens(1);
        if (tokenLimit is not null)
            ThrowPhraseGraphLimitExceeded(tokenLimit);
    }

    private void ThrowPhraseGraphLimitExceeded(string message)
    {
        ThrowQueryParseLimitExceeded(message, offset: 0);
    }

    private void ThrowQueryParseLimitExceeded(string message, int? offset = null)
    {
        if (_graphPathLimitIsComplexity || _parseLimitsAreComplexity)
            throw new QueryParseLimitException(message);

        if (offset is int value)
            throw new QueryParseException(message, value);

        throw new QueryParseException(message);
    }

    private void ResetPhraseGraphBudget()
    {
        _queryCompilationBudget = new QueryCompilationBudget(_options);
    }

    private QueryCompilationBudget GetQueryCompilationBudget() =>
        _queryCompilationBudget ??= new QueryCompilationBudget(_options);

    private Query CompilePhraseExpansion(string field, int slop, PhraseQuerySyntaxExpansion expansion)
    {
        if (expansion.IsNoClause)
            return NoClauseQuery.Instance;
        if (expansion.DirectTerms is not null)
            return new PhraseQuery(field, slop, expansion.DirectTerms);

        PhraseGraphPlan graph = expansion.Graph
            ?? throw new InvalidOperationException("A phrase graph expansion must contain its validated graph.");
        BooleanQuery.Builder? builder = expansion.PathCount > 1 ? new BooleanQuery.Builder() : null;
        PhraseQuery? singlePhrase = null;
        int compiledPathCount = 0;
        int compiledTermCount = 0;
        // The immutable graph was already validated against the per-parse budget.
        // Compile one output path at a time rather than retaining every path array.
        EnumeratePhraseGraphPaths(graph, path =>
        {
            var terms = new string[path.Count];
            var positions = new int[path.Count];
            for (int index = 0; index < path.Count; index++)
            {
                Analysis.TokenGraph.TokenEdge edge = path[index];
                terms[index] = edge.Token.Text;
                positions[index] = edge.StartPosition - graph.StartPosition;
            }

            var phrase = new PhraseQuery(field, terms, positions, slop);
            if (builder is null)
                singlePhrase = phrase;
            else
                builder.Add(phrase, Occur.Should);

            compiledPathCount++;
            compiledTermCount += path.Count;
        }, countTraversalSteps: false);

        if (compiledPathCount != expansion.PathCount || compiledTermCount != expansion.CompiledTermCount)
            throw new InvalidOperationException("The validated phrase graph changed while compiling its query paths.");

        if (builder is not null)
            return builder.Build();

        return singlePhrase
            ?? throw new InvalidOperationException("A validated phrase graph must contain at least one path.");
    }

    private QuerySyntax Prepare(QuerySyntax syntax, Action<int, int> consumeAdditionalClauses, int depth) => syntax switch
    {
        GroupQuerySyntax group => group with { Inner = Prepare(group.Inner, consumeAdditionalClauses, depth + 1) },
        BooleanQuerySyntax boolean => boolean with
        {
            Clauses = boolean.Clauses.Select(clause => clause with { Query = Prepare(clause.Query, consumeAdditionalClauses, depth + 1) }).ToArray()
        },
        DisjunctionMaxQuerySyntax disjunction => disjunction with
        {
            Clauses = disjunction.Clauses.Select(clause => Prepare(clause, consumeAdditionalClauses, depth + 1)).ToArray()
        },
        BoostQuerySyntax boost when boost.ConstantScore => boost with { Inner = Prepare(boost.Inner, consumeAdditionalClauses, depth + 1) },
        BoostQuerySyntax boost => boost with { Inner = Prepare(boost.Inner, consumeAdditionalClauses, depth) },
        PhraseQuerySyntax phrase => PreparePhrase(phrase, consumeAdditionalClauses, depth),
        _ => syntax
    };

    private PhraseQuerySyntax PreparePhrase(PhraseQuerySyntax phrase, Action<int, int> consumeAdditionalClauses, int depth)
    {
        PhraseQuerySyntaxExpansion expansion = phrase.Expansion ?? CreatePhraseExpansion(phrase.Field, phrase.Text);
        if (expansion.PathCount > 1)
            consumeAdditionalClauses(expansion.PathCount, depth + 1);
        return phrase with { Expansion = expansion };
    }

    private int ReadSlop(List<QToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Type != QTokenType.Tilde)
            return 0;

        int modifierOffset = tokens[pos].Offset;
        int suffixPosition = pos + 1;
        int slop = 0;
        bool hasSlop = suffixPosition < tokens.Count &&
            tokens[suffixPosition].Type == QTokenType.Term &&
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

    private QuerySyntax ApplyBoost(QuerySyntax query, List<QToken> tokens, ref int pos)
    {
        if (pos >= tokens.Count || tokens[pos].Type != QTokenType.Caret)
            return query;

        int modifierOffset = tokens[pos].Offset;
        int suffixPosition = pos + 1;
        bool constantScore = suffixPosition < tokens.Count && tokens[suffixPosition].Type == QTokenType.Equal;
        if (constantScore)
            suffixPosition++;

        if (suffixPosition >= tokens.Count ||
            tokens[suffixPosition].Type != QTokenType.Term ||
            !float.TryParse(
                tokens[suffixPosition].Value,
                System.Globalization.CultureInfo.InvariantCulture,
                out float boost) ||
            !float.IsFinite(boost))
        {
            throw new QueryParseException("Boost modifiers require a finite numeric value.", modifierOffset);
        }

        pos = suffixPosition + 1;
        BoostQuerySyntax syntax = new(query, boost, constantScore);
        return constantScore ? CreateSyntaxNode(syntax) : syntax;
    }

    private T CreateSyntaxNode<T>(T syntax) where T : QuerySyntax
    {
        if (_syntaxNodeCount >= _maxSyntaxNodes)
            ThrowQueryParseLimitExceeded(
                $"The query exceeds the configured syntax-node limit of {_maxSyntaxNodes}.");
        _syntaxNodeCount++;

        if (_countQueryClauses && IsQueryClauseNode(syntax))
        {
            if (_queryClauseCount >= _maxQueryClauses)
                ThrowQueryParseLimitExceeded(
                    $"The query exceeds the configured query-clause limit of {_maxQueryClauses}.");
            _queryClauseCount++;
        }

        return syntax;
    }

    private static bool IsQueryClauseNode(QuerySyntax syntax) => syntax is
        AnalysedEmptyQuerySyntax or RecoveredQuerySyntax or BooleanQuerySyntax or
        DisjunctionMaxQuerySyntax or TermQuerySyntax or FuzzyQuerySyntax or
        MultiTermQuerySyntax or PhraseQuerySyntax or RegexpQuerySyntax or
        TermRangeQuerySyntax or FieldExistsQuerySyntax ||
        syntax is BoostQuerySyntax { ConstantScore: true };

    private Query? Compile(QuerySyntax syntax) => syntax switch
    {
        EmptyQuerySyntax => null,
        AnalysedEmptyQuerySyntax => NoClauseQuery.Instance,
        RecoveredQuerySyntax => null,
        GroupQuerySyntax group => Compile(group.Inner),
        TermQuerySyntax term => new TermQuery(term.Field, term.Term),
        FuzzyQuerySyntax fuzzy => CompileFuzzy(fuzzy),
        MultiTermQuerySyntax multiTerm => CompileMultiTerm(multiTerm),
        PhraseQuerySyntax phrase => CompilePhrase(phrase),
        RegexpQuerySyntax regexp => CompileRegexp(regexp),
        TermRangeQuerySyntax range => CompileRange(range),
        FieldExistsQuerySyntax exists => new FieldExistsQuery(exists.Field),
        BoostQuerySyntax boost => CompileBoost(boost),
        BooleanQuerySyntax boolean => CompileBoolean(boolean),
        DisjunctionMaxQuerySyntax disjunction => CompileDisjunctionMax(disjunction),
        _ => throw new InvalidOperationException($"Unsupported query syntax '{syntax.GetType().Name}'.")
    };

    private Query CompilePhrase(PhraseQuerySyntax phrase)
    {
        if (phrase.Expansion is not null)
            return CompilePhraseExpansion(phrase.Field, phrase.Slop, phrase.Expansion);

        return BuildPhraseQuery(phrase.Field, phrase.Text, phrase.RawText ?? phrase.Text, phrase.Slop);
    }

    private static FuzzyQuery CompileFuzzy(FuzzyQuerySyntax syntax)
    {
        try
        {
            return new FuzzyQuery(syntax.Field, syntax.Term, syntax.MaxEdits);
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "maxEdits")
        {
            throw new QueryParseException("Fuzzy edit distance must be between 0 and 2.", syntax.ModifierOffset);
        }
    }

    private Query CompileMultiTerm(MultiTermQuerySyntax syntax)
    {
        string term = AnalyseMultiTerm(syntax.Field, syntax.Pattern);
        if (term.Length > _options.MaxWildcardPatternChars)
        {
            ThrowQueryParseLimitExceeded(
                $"The analysed wildcard pattern exceeds the configured character limit of {_options.MaxWildcardPatternChars}.");
        }
        if (TryGetPrefixLiteral(term.AsSpan(), out string prefix))
            return new PrefixQuery(syntax.Field, prefix);
        return new WildcardQuery(syntax.Field, term);
    }

    private static bool TryGetPrefixLiteral(ReadOnlySpan<char> pattern, out string prefix)
    {
        var literal = new System.Text.StringBuilder(pattern.Length);
        bool foundTrailingStar = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\')
            {
                if (i + 1 < pattern.Length)
                    literal.Append(pattern[++i]);
                else
                    literal.Append('\\');
                continue;
            }

            if (c == '?')
            {
                prefix = string.Empty;
                return false;
            }
            if (c == '*')
            {
                if (foundTrailingStar || i != pattern.Length - 1)
                {
                    prefix = string.Empty;
                    return false;
                }
                foundTrailingStar = true;
                continue;
            }

            literal.Append(c);
        }

        prefix = literal.ToString();
        return foundTrailingStar;
    }

    private Query CompileRange(TermRangeQuerySyntax syntax) => new TermRangeQuery(
        syntax.Field,
        syntax.LowerTerm is null ? null : AnalyseRangeBound(syntax.Field, syntax.LowerTerm),
        syntax.UpperTerm is null ? null : AnalyseRangeBound(syntax.Field, syntax.UpperTerm),
        syntax.IncludeLower,
        syntax.IncludeUpper);

    private Query CompileRegexp(RegexpQuerySyntax syntax)
    {
        QueryFieldCompilationContext context = ResolveFieldContext(syntax.Field);
        return new RegexpQuery(context.Field, syntax.Pattern);
    }

    private Query? CompileBoost(BoostQuerySyntax syntax)
    {
        Query? inner = Compile(syntax.Inner);
        if (inner is NoClauseQuery)
            return inner;
        if (inner is null)
            return null;
        if (syntax.ConstantScore)
            return new ConstantScoreQuery(inner, syntax.Boost);
        inner.Boost = syntax.Boost;
        return inner;
    }

    private Query? CompileBoolean(BooleanQuerySyntax syntax)
    {
        var builder = new BooleanQuery.Builder();
        int count = 0;
        bool hasNoClause = false;
        foreach (QuerySyntaxClause clause in syntax.Clauses)
        {
            Query? query = Compile(clause.Query);
            if (query is NoClauseQuery)
            {
                if (clause.Occur == Occur.Must)
                    return NoClauseQuery.Instance;

                hasNoClause = true;
                continue;
            }
            if (query is null)
                continue;
            builder.Add(query, clause.Occur);
            count++;
        }
        return count > 0 ? builder.Build() : hasNoClause ? NoClauseQuery.Instance : null;
    }

    private Query? CompileDisjunctionMax(DisjunctionMaxQuerySyntax syntax)
    {
        var disjunction = new DisjunctionMaxQuery.Builder();
        Query? only = null;
        int count = 0;
        bool hasNoClause = false;
        foreach (QuerySyntax clause in syntax.Clauses)
        {
            Query? query = Compile(clause);
            if (query is NoClauseQuery)
            {
                hasNoClause = true;
                continue;
            }
            if (query is null)
                continue;
            only = query;
            disjunction.Add(query);
            count++;
        }
        return count switch
        {
            0 => hasNoClause ? NoClauseQuery.Instance : null,
            1 => only,
            _ => disjunction.Build()
        };
    }

    /// <summary>Analyses a literal query term and returns its complete token stream.</summary>
    /// <remarks>
    /// Subclasses that need a single normalised literal for wildcard or range handling
    /// should use <see cref="AnalyseSingleToken(string)"/>, which rejects multi-token output.
    /// </remarks>
    protected IReadOnlyList<Analysis.Token> AnalyseTerm(string term) => AnalyseTerm(_defaultField, term);

    /// <summary>Analyses a literal query term with the analyser resolved for <paramref name="field"/>.</summary>
    protected IReadOnlyList<Analysis.Token> AnalyseTerm(string field, string term)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens);
        ResolveFieldContext(field).QueryAnalyser.Analyse(term.AsSpan(), sink);
        return tokens.ToArray();
    }

    /// <summary>Analyses a literal that must remain a single term, such as a wildcard fragment.</summary>
    /// <exception cref="QueryParseException">The analyser emitted more than one token or a graph edge.</exception>
    protected string AnalyseSingleToken(string term) => AnalyseSingleToken(_defaultField, term);

    /// <summary>Analyses a literal using the analyser resolved for <paramref name="field"/>.</summary>
    /// <exception cref="QueryParseException">The analyser emitted more than one token or a graph edge.</exception>
    protected string AnalyseSingleToken(string field, string term) =>
        AnalyseSingleToken(ResolveFieldContext(field).QueryAnalyser, term);

    /// <summary>Analyses one simple complex-phrase slot within the active phrase budgets.</summary>
    private protected string AnalyseComplexPhraseTerm(string field, string term)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens, this);
        ResolveFieldContext(field).QueryAnalyser.Analyse(term.AsSpan(), sink);
        if (tokens.Count != 1 || tokens[0].PositionLength != 1)
        {
            throw new QueryParseException(
                "Each term in a complex phrase alternative must analyse to exactly one linear token.");
        }

        _ = CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: true);
        return tokens[0].Text;
    }

    /// <summary>Charges custom complex-phrase slots and alternatives to the active query budgets.</summary>
    private protected void ConsumeComplexPhraseClauses(int clauseCount, int alternativeClauseCount)
    {
        if (clauseCount < 0 || alternativeClauseCount < 0 || alternativeClauseCount > clauseCount)
            throw new ArgumentOutOfRangeException(nameof(clauseCount));

        if (clauseCount > _maxQueryClauses - _queryClauseCount)
        {
            ThrowQueryParseLimitExceeded(
                $"The query exceeds the configured query-clause limit of {_maxQueryClauses}.");
        }

        _queryClauseCount += clauseCount;
        if (alternativeClauseCount == 0)
            return;

        string? clauseLimit = GetQueryCompilationBudget().TryGenerateBooleanClauses(alternativeClauseCount);
        if (clauseLimit is not null)
            ThrowPhraseGraphLimitExceeded(clauseLimit);
    }

    private static string AnalyseSingleToken(IAnalyser analyser, string term)
    {
        var tokens = new List<Analysis.Token>();
        analyser.Analyse(term.AsSpan(), new CapturingSink(tokens));
        if (tokens.Count == 0)
            return string.Empty;
        if (tokens.Count != 1 || tokens[0].PositionLength != 1)
        {
            throw new QueryParseException(
                "A wildcard or range literal must analyse to at most one unit-length token.", 0);
        }

        return tokens[0].Text;
    }

    internal static Func<string, string> CreateSingleTokenNormaliser(IAnalyser analyser)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        return literal => NormaliseSingleTerm(analyser, literal);
    }

    internal static string NormaliseSingleTerm(IAnalyser analyser, string literal)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(literal);

        if (analyser is not ITermNormaliser normaliser)
        {
            throw new QueryParseException(
                "The configured analyser does not support one-to-one term normalisation required by wildcard and range queries.",
                0);
        }

        if (!normaliser.TryNormalise(literal.AsSpan(), out string normalised) || string.IsNullOrEmpty(normalised))
        {
            throw new QueryParseException(
                "A wildcard or range literal must normalise to exactly one non-empty term.",
                0);
        }

        return normalised;
    }

    internal static string NormaliseMultiTermPattern(string pattern, Func<string, string> normaliseLiteral)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(normaliseLiteral);

        var builder = new System.Text.StringBuilder(pattern.Length);
        var literal = new System.Text.StringBuilder(pattern.Length);

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;

            builder.Append(EscapeWildcardLiteral(normaliseLiteral(literal.ToString())));
            literal.Clear();
        }

        for (int i = 0; i < pattern.Length;)
        {
            char current = pattern[i];
            if (current == '\\')
            {
                if (i + 1 < pattern.Length)
                {
                    char escaped = pattern[i + 1];
                    if (escaped is '*' or '?' or '\\')
                    {
                        FlushLiteral();
                        builder.Append('\\').Append(escaped);
                    }
                    else
                    {
                        literal.Append(escaped);
                    }
                    i += 2;
                    continue;
                }

                literal.Append('\\');
                i++;
                continue;
            }

            if (current is '*' or '?')
            {
                FlushLiteral();
                builder.Append(current);
                i++;
                continue;
            }

            literal.Append(current);
            i++;
        }

        FlushLiteral();
        return builder.ToString();
    }

    private static string EscapeWildcardLiteral(string literal)
    {
        var escaped = new System.Text.StringBuilder(literal.Length);
        foreach (char character in literal)
        {
            if (character is '*' or '?' or '\\')
                escaped.Append('\\');
            escaped.Append(character);
        }
        return escaped.ToString();
    }

    private string AnalyseMultiTerm(string field, string pattern)
    {
        Func<string, string>? normaliseLiteral = ResolveFieldContext(field).MultiTermNormaliser;
        return normaliseLiteral is null
            ? AnalyseMultiTerm(pattern)
            : NormaliseMultiTermPattern(pattern, normaliseLiteral);
    }

    private string AnalyseRangeBound(string field, string term)
    {
        Func<string, string>? normaliseLiteral = ResolveFieldContext(field).MultiTermNormaliser;
        return normaliseLiteral is null
            ? AnalyseRangeBound(term)
            : normaliseLiteral(term);
    }

    private QueryFieldCompilationContext ResolveFieldContext(string field)
    {
        if (_fieldContextResolver is null)
            return new QueryFieldCompilationContext(field, Analyser, MultiTermNormaliser: null);
        if (_fieldContexts.TryGetValue(field, out QueryFieldCompilationContext? context))
            return context;

        context = _fieldContextResolver(field)
            ?? throw new QueryParseException("The field context resolver returned no context.", 0);
        if (!string.Equals(context.Field, field, StringComparison.Ordinal))
            throw new QueryParseException("The field context resolver returned a context for a different field.", 0);

        _fieldContexts.Add(field, context);
        return context;
    }

    /// <summary>Normalises a wildcard or prefix term while preserving its operators.</summary>
    protected virtual string AnalyseMultiTerm(string term) => term;

    /// <summary>Normalises one bounded term in a text range query.</summary>
    protected virtual string AnalyseRangeBound(string term) => term;

    private sealed class CapturingSink : Analysis.ISpanTokenSink
    {
        private readonly List<Analysis.Token> _tokens;
        private readonly QueryParser? _phraseOwner;

        public CapturingSink(List<Analysis.Token> tokens) => _tokens = tokens;

        public CapturingSink(List<Analysis.Token> tokens, QueryParser phraseOwner)
        {
            _tokens = tokens;
            _phraseOwner = phraseOwner;
        }

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Analysis.Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
        {
            _phraseOwner?.ConsumeAnalysedPhraseToken();
            _tokens.Add(new Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
        }

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type,
            int positionIncrement, int positionLength, byte[]? payload)
        {
            _phraseOwner?.ConsumeAnalysedPhraseToken();
            _tokens.Add(new Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload, positionLength));
        }
    }

    private List<QToken> Tokenize(string input)
    {
        var tokens = new List<QToken>();
        int i = 0;

        void AddToken(QToken token)
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
                case '+': AddToken(new QToken(QTokenType.Plus, "+", i)); i++; continue;
                case '-': AddToken(new QToken(QTokenType.Minus, "-", i)); i++; continue;
                case '(': AddToken(new QToken(QTokenType.LParen, "(", i)); i++; continue;
                case ')': AddToken(new QToken(QTokenType.RParen, ")", i)); i++; continue;
                case ':': AddToken(new QToken(QTokenType.Colon, ":", i)); i++; continue;
                case '~': AddToken(new QToken(QTokenType.Tilde, "~", i)); i++; continue;
                case '^': AddToken(new QToken(QTokenType.Caret, "^", i)); i++; continue;
                case '=': AddToken(new QToken(QTokenType.Equal, "=", i)); i++; continue;
                case '|': AddToken(new QToken(QTokenType.Pipe, "|", i)); i++; continue;
                case '[': AddToken(new QToken(QTokenType.OpenSquare, "[", i)); i++; continue;
                case ']': AddToken(new QToken(QTokenType.CloseSquare, "]", i)); i++; continue;
                case '{': AddToken(new QToken(QTokenType.OpenCurly, "{", i)); i++; continue;
                case '}': AddToken(new QToken(QTokenType.CloseCurly, "}", i)); i++; continue;
            }

            if (c == '/')
            {
                int slashOffset = i++;
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
                        i++;
                        closed = true;
                        break;
                    }
                    AppendPattern(input[i++]);
                }
                if (!closed)
                    throw new QueryParseException("Unmatched regular expression delimiter.", slashOffset);
                AddToken(new QToken(QTokenType.Regex, pattern.ToString(), slashOffset));
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
                AddToken(new QToken(QTokenType.Phrase, Unescape(phraseRaw), quoteOffset, phraseRaw));
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
                var type = !hasEscapes ? GetKeywordType(termValue) : QTokenType.Term;
                AddToken(new QToken(type, termValue, start, raw, hasUnescapedWildcard));
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

    private readonly record struct QToken(
        QTokenType Type,
        string Value,
        int Offset,
        string? RawValue = null,
        bool HasUnescapedWildcard = false)
    {
        public string Raw => RawValue ?? Value;
    }
    private readonly record struct ParsedSyntaxClause(
        QuerySyntax? Query,
        Occur Occur,
        QueryClauseState State = QueryClauseState.Parsed);

    private sealed class NoClauseQuery : Query
    {
        public static NoClauseQuery Instance { get; } = new();

        public override string Field => string.Empty;

        public override bool Equals(object? obj) => obj is NoClauseQuery;

        public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(NoClauseQuery)));
    }

    private sealed class QueryCompilationBudget(QueryParserOptions options)
    {
        private int _analysedPhraseTokenCount;
        private int _graphEdgesRead;
        private int _traversalSteps;
        private int _pathsEmitted;
        private int _compiledTerms;
        private int _generatedBooleanClauses;

        public string? TryConsumePhraseTokens(int count)
        {
            if (count > options.MaxPhraseTokens - _analysedPhraseTokenCount)
                return $"Analysed phrase token count exceeds the maximum of {options.MaxPhraseTokens}.";

            _analysedPhraseTokenCount += count;
            return null;
        }

        public string? TryReadGraphEdge()
        {
            if (_graphEdgesRead >= options.MaxGraphEdges)
                return $"Analysed phrase graph edge count exceeds the maximum of {options.MaxGraphEdges}.";

            _graphEdgesRead++;
            return null;
        }

        public string? TryTakeTraversalStep()
        {
            if (_traversalSteps >= options.MaxGraphTraversalSteps)
                return $"Analysed phrase graph traversal steps exceed the maximum of {options.MaxGraphTraversalSteps}.";

            _traversalSteps++;
            return null;
        }

        public string? TryEmitPath(int termCount)
        {
            if (_pathsEmitted >= options.MaxGraphPaths)
                return $"Analysed phrase graph exceeds the configured maximum of {options.MaxGraphPaths} paths.";
            string? termLimit = TryCompileTerms(termCount);
            if (termLimit is not null)
                return termLimit;

            _pathsEmitted++;
            return null;
        }

        public string? TryCompileTerms(int count)
        {
            if (count > options.MaxCompiledPhraseTerms - _compiledTerms)
                return $"Compiled phrase term count exceeds the maximum of {options.MaxCompiledPhraseTerms}.";

            _compiledTerms += count;
            return null;
        }

        public string? TryGenerateBooleanClauses(int count)
        {
            if (count > options.MaxCompiledPhraseClauses - _generatedBooleanClauses)
                return $"Compiled phrase query clause count exceeds the maximum of {options.MaxCompiledPhraseClauses}.";

            _generatedBooleanClauses += count;
            return null;
        }
    }

    private struct PhraseGraphTraversalFrame
    {
        public PhraseGraphTraversalFrame(int position, int pathLength)
        {
            Position = position;
            PathLength = pathLength;
        }

        public int Position { get; }
        public int NextEdgeIndex { get; set; }
        public int PathLength { get; }
    }
}

internal abstract record QuerySyntax;
internal enum QueryClauseState
{
    SyntaxMissing,
    Parsed,
    AnalysedEmpty,
    RecoveredError
}

internal sealed record EmptyQuerySyntax : QuerySyntax;
internal sealed record AnalysedEmptyQuerySyntax : QuerySyntax;
internal sealed record RecoveredQuerySyntax : QuerySyntax;
internal sealed record GroupQuerySyntax(QuerySyntax Inner) : QuerySyntax;
internal sealed record UnanalysedTermQuerySyntax(string Field, string Term) : QuerySyntax;
internal sealed record UnanalysedFuzzyQuerySyntax(string Field, string Term, int MaxEdits, int ModifierOffset) : QuerySyntax;
internal sealed record TermQuerySyntax(string Field, string Term) : QuerySyntax;
internal sealed record FuzzyQuerySyntax(string Field, string Term, int MaxEdits, int ModifierOffset) : QuerySyntax;
internal sealed record MultiTermQuerySyntax(string Field, string Pattern, string Term) : QuerySyntax;
internal sealed record PhraseQuerySyntax(
    string Field,
    string Text,
    int Slop,
    PhraseQuerySyntaxExpansion? Expansion = null,
    string? RawText = null) : QuerySyntax;
internal sealed record PhraseGraphPlan(
    int StartPosition,
    int EndPosition,
    int[] StartPositions,
    IReadOnlyDictionary<int, Analysis.TokenGraph.TokenEdge[]> EdgesByStart);
internal sealed record PhraseQuerySyntaxExpansion(
    string[]? DirectTerms,
    PhraseGraphPlan? Graph,
    int PathCount,
    int CompiledTermCount)
{
    public bool IsNoClause => DirectTerms is null && Graph is null;
}
internal sealed record RegexpQuerySyntax(string Field, string Pattern) : QuerySyntax;
internal sealed record TermRangeQuerySyntax(string Field, string? LowerTerm, string? UpperTerm, bool IncludeLower, bool IncludeUpper) : QuerySyntax;
internal sealed record FieldExistsQuerySyntax(string Field) : QuerySyntax;
internal sealed record BoostQuerySyntax(QuerySyntax Inner, float Boost, bool ConstantScore) : QuerySyntax;
internal sealed record BooleanQuerySyntax(IReadOnlyList<QuerySyntaxClause> Clauses) : QuerySyntax;
internal sealed record DisjunctionMaxQuerySyntax(IReadOnlyList<QuerySyntax> Clauses) : QuerySyntax;
internal readonly record struct QuerySyntaxClause(
    QuerySyntax Query,
    Occur Occur,
    QueryClauseState State = QueryClauseState.Parsed);
internal sealed record QueryFieldCompilationContext(
    string Field,
    IAnalyser QueryAnalyser,
    Func<string, string>? MultiTermNormaliser);

internal sealed class QueryParseLimitException(string message) : Exception(message);

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
