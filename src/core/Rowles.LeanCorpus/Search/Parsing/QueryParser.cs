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
/// </remarks>
public class QueryParser
{
    private const int MaximumAnalysedPhraseTokenCount = 16_384;
    private const int MaximumPhraseGraphEdgeCount = 8_192;
    private const int MaximumPhraseGraphTraversalSteps = 65_536;
    private const int MaximumCompiledPhraseClauseCount = 512;

    private readonly string _defaultField;
    private readonly IAnalyser _analyser;
    private readonly Func<string, QueryFieldCompilationContext>? _fieldContextResolver;
    private readonly Dictionary<string, QueryFieldCompilationContext> _fieldContexts = new(StringComparer.Ordinal);
    private readonly bool _lenient;
    private readonly int _maxGraphPaths;
    private int _depth;
    private int _maxDepth = 64;
    private int _maxSyntaxNodes = int.MaxValue;
    private int _syntaxNodeCount;
    private int _analysedPhraseTokenCount;
    private int _phraseGraphEdgeCount;
    private int _phraseGraphTraversalSteps;
    private int _compiledPhraseClauseCount;
    private bool _parseLimitsAreComplexity;
    private bool _graphPathLimitIsComplexity;

    /// <summary>Gets the analyser used to build query terms.</summary>
    protected IAnalyser Analyser => _analyser;

    /// <summary>Initialises a new <see cref="QueryParser"/> with the given default field and analyser.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="lenient">
    /// When <see langword="true"/>, syntax errors are tolerated and the parser returns the best-effort
    /// result built from valid tokens. When <see langword="false"/> (default), syntax errors throw
    /// <see cref="QueryParseException"/>.
    /// </param>
    /// <param name="maxGraphPaths">The maximum complete analysed phrase paths permitted before parsing fails.</param>
    public QueryParser(string defaultField, IAnalyser analyser, bool lenient = false, int maxGraphPaths = 256)
        : this(defaultField, analyser, fieldContextResolver: null, lenient, maxGraphPaths)
    {
    }

    internal QueryParser(
        string defaultField,
        IAnalyser analyser,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        bool lenient = false,
        int maxGraphPaths = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGraphPaths, 1);
        ArgumentNullException.ThrowIfNull(defaultField);
        ArgumentNullException.ThrowIfNull(analyser);
        _defaultField = defaultField;
        _analyser = analyser;
        _fieldContextResolver = fieldContextResolver;
        _lenient = lenient;
        _maxGraphPaths = maxGraphPaths;
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
        return CompileSyntax(ParseSyntax(queryString, maximumDepth: 64, maximumClauses: int.MaxValue, maximumTokens: int.MaxValue));
    }

    internal QuerySyntax ParseSyntax(
        string queryString,
        int maximumDepth,
        int maximumClauses,
        int maximumTokens,
        bool limitsAreComplexity = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumClauses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);
        if (string.IsNullOrWhiteSpace(queryString))
            return new EmptyQuerySyntax();

        _depth = 0;
        _syntaxNodeCount = 0;
        _parseLimitsAreComplexity = limitsAreComplexity;
        ResetPhraseGraphBudget();
        _maxDepth = maximumDepth;
        _maxSyntaxNodes = maximumClauses;
        var tokens = Tokenize(queryString, _lenient, maximumTokens);
        int pos = 0;
        QuerySyntax? syntax;
        if (_lenient)
        {
            try { syntax = ParseExpression(tokens, ref pos).Query; }
            catch (QueryParseException) { syntax = null; }
        }
        else
        {
            syntax = ParseExpression(tokens, ref pos).Query;
            if (pos < tokens.Count)
            {
                var tok = tokens[pos];
                throw new QueryParseException(
                    $"Unexpected token '{tok.Value}' at position {pos}.", tok.Offset);
            }
        }
        return syntax ?? new EmptyQuerySyntax();
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

    internal Query CompileSyntax(QuerySyntax syntax) => Compile(syntax) ?? new BooleanQuery.Builder().Build();

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
            if (next.Query is null)
            {
                if (_lenient)
                    break;
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
            if (_lenient)
                return default;
            throw new QueryParseException(
                "A required or prohibited operator must be followed by a query clause.",
                operatorOffset);
        }

        QuerySyntax? query;
        if (_lenient)
        {
            try { query = ParseClause(tokens, ref pos); }
            catch (QueryParseException) { return default; }
        }
        else
        {
            query = ParseClause(tokens, ref pos);
        }
        return new ParsedSyntaxClause(query, occur);
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
            else if (!_lenient)
                throw new QueryParseException("Unmatched opening parenthesis.", openOffset);
            return ApplyBoost(new GroupQuerySyntax(inner.Query!), tokens, ref pos);
        }

        // Quoted phrase
        if (tokens[pos].Type == QTokenType.Phrase)
        {
            var phrase = tokens[pos].Value;
            pos++;
            string field = _defaultField;

            int slop = ReadSlop(tokens, ref pos);
            return ApplyBoost(CreateSyntaxNode(new PhraseQuerySyntax(field, phrase, slop)), tokens, ref pos);
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
                        int slop = ReadSlop(tokens, ref pos);
                        var pq = CreateSyntaxNode(new PhraseQuerySyntax(field, phrase, slop));
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
            if (termToken.HasUnescapedWildcard)
            {
                var multiTerm = CreateSyntaxNode(new MultiTermQuerySyntax(field, termToken.Raw, term));
                return ApplyBoost(multiTerm, tokens, ref pos);
            }

            // Check for fuzzy ~ suffix
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Tilde)
            {
                int modifierOffset = tokens[pos].Offset;
                pos++;
                int maxEdits = 2;
                if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                    int.TryParse(tokens[pos].Value, out int edits))
                {
                    maxEdits = edits;
                    pos++;
                }
                var analysedTokens = AnalyseTerm(field, term);
                var fuzzy = LowerAnalysedTokens(field, term, analysedTokens, maxEdits, modifierOffset);
                return fuzzy is null ? null : ApplyBoost(fuzzy, tokens, ref pos);
            }

            // Regular term — analyse it
            var analysedTermTokens = AnalyseTerm(field, term);
            var loweredTerm = LowerAnalysedTokens(field, term, analysedTermTokens);
            return loweredTerm is null ? null : ApplyBoost(loweredTerm, tokens, ref pos);
        }

        if (_lenient) return null;
        throw new QueryParseException(
            $"Unexpected token '{tokens[pos].Value}' at position {pos}.", tokens[pos].Offset);
    }

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

    private PhraseQuerySyntaxExpansion CreatePhraseExpansion(string field, string phraseText)
    {
        var tokens = new List<Analysis.Token>();
        var sink = new CapturingSink(tokens, this);
        ResolveFieldContext(field).QueryAnalyser.Analyse(phraseText.AsSpan(), sink);
        return CreatePhraseExpansionFromTokens(phraseText, tokens, tokensAlreadyCounted: true);
    }

    private PhraseQuerySyntaxExpansion CreatePhraseExpansionFromTokens(
        string sourceText,
        IReadOnlyList<Analysis.Token> tokens,
        bool tokensAlreadyCounted)
    {
        if (tokens.Count == 0)
        {
            CountFallbackPhraseTokens(sourceText);
            return new PhraseQuerySyntaxExpansion(sourceText.Split(' '), []);
        }

        if (!tokensAlreadyCounted)
        {
            foreach (var _ in tokens)
                ConsumeAnalysedPhraseToken();
        }

        var graph = new Analysis.TokenGraph();
        foreach (var token in tokens)
        {
            if (_phraseGraphEdgeCount >= MaximumPhraseGraphEdgeCount)
                ThrowPhraseGraphLimitExceeded(
                    $"Analysed phrase graph edge count exceeds the maximum of {MaximumPhraseGraphEdgeCount}.");
            graph.Add(token);
            _phraseGraphEdgeCount++;
        }
        graph.ValidateOrdered();

        int start = graph.Edges.Min(static edge => edge.StartPosition);
        int end = graph.Edges.Max(static edge => edge.EndPosition);
        var byStart = graph.Edges.GroupBy(static edge => edge.StartPosition)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var paths = new List<PhraseQuerySyntaxPath[]>();
        var path = new List<Analysis.TokenGraph.TokenEdge>();
        var traversal = new List<PhraseGraphTraversalFrame> { new(start, pathLength: 0) };
        int compiledPhraseClauseCount = 0;

        while (traversal.Count > 0)
        {
            int frameIndex = traversal.Count - 1;
            PhraseGraphTraversalFrame frame = traversal[frameIndex];
            if (path.Count > frame.PathLength)
                path.RemoveRange(frame.PathLength, path.Count - frame.PathLength);

            if (frame.Position == end)
            {
                if (paths.Count >= _maxGraphPaths)
                {
                    string message = $"Analysed phrase graph exceeds the configured maximum of {_maxGraphPaths} paths.";
                    ThrowPhraseGraphLimitExceeded(message);
                }

                if (_compiledPhraseClauseCount + compiledPhraseClauseCount >= MaximumCompiledPhraseClauseCount)
                {
                    ThrowPhraseGraphLimitExceeded(
                        $"Compiled phrase query clause count exceeds the maximum of {MaximumCompiledPhraseClauseCount}.");
                }

                compiledPhraseClauseCount++;
                paths.Add(path.Select(edge => new PhraseQuerySyntaxPath(edge.Token.Text, edge.StartPosition - start)).ToArray());
                traversal.RemoveAt(frameIndex);
                continue;
            }

            if (!byStart.TryGetValue(frame.Position, out var nextEdges))
            {
                traversal.RemoveAt(frameIndex);
                continue;
            }

            if (frame.NextEdgeIndex >= nextEdges.Length)
            {
                traversal.RemoveAt(frameIndex);
                continue;
            }

            if (_phraseGraphTraversalSteps >= MaximumPhraseGraphTraversalSteps)
            {
                ThrowPhraseGraphLimitExceeded(
                    $"Analysed phrase graph traversal steps exceed the maximum of {MaximumPhraseGraphTraversalSteps}.");
            }

            Analysis.TokenGraph.TokenEdge edge = nextEdges[frame.NextEdgeIndex];
            frame.NextEdgeIndex++;
            traversal[frameIndex] = frame;
            _phraseGraphTraversalSteps++;
            path.Add(edge);
            traversal.Add(new PhraseGraphTraversalFrame(edge.EndPosition, path.Count));
        }

        if (paths.Count == 0)
            throw new QueryParseException("Analysed phrase token graph has no complete path.", 0);

        _compiledPhraseClauseCount += compiledPhraseClauseCount;
        return new PhraseQuerySyntaxExpansion(null, paths);
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

            var expansion = CreatePhraseExpansionFromTokens(sourceText, tokens, tokensAlreadyCounted: false);
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

    private void CountFallbackPhraseTokens(string phraseText)
    {
        int phraseTokenCount = 1;
        foreach (char character in phraseText)
        {
            if (character != ' ')
                continue;

            if (_analysedPhraseTokenCount + phraseTokenCount >= MaximumAnalysedPhraseTokenCount)
            {
                ThrowPhraseGraphLimitExceeded(
                    $"Analysed phrase token count exceeds the maximum of {MaximumAnalysedPhraseTokenCount}.");
            }

            phraseTokenCount++;
        }

        if (_analysedPhraseTokenCount > MaximumAnalysedPhraseTokenCount - phraseTokenCount)
        {
            ThrowPhraseGraphLimitExceeded(
                $"Analysed phrase token count exceeds the maximum of {MaximumAnalysedPhraseTokenCount}.");
        }

        _analysedPhraseTokenCount += phraseTokenCount;
    }

    private void ConsumeAnalysedPhraseToken()
    {
        if (_analysedPhraseTokenCount >= MaximumAnalysedPhraseTokenCount)
        {
            ThrowPhraseGraphLimitExceeded(
                $"Analysed phrase token count exceeds the maximum of {MaximumAnalysedPhraseTokenCount}.");
        }

        _analysedPhraseTokenCount++;
    }

    private void ThrowPhraseGraphLimitExceeded(string message)
    {
        if (_graphPathLimitIsComplexity || _parseLimitsAreComplexity)
            throw new QueryParseLimitException(message);

        throw new QueryParseException(message, 0);
    }

    private void ResetPhraseGraphBudget()
    {
        _analysedPhraseTokenCount = 0;
        _phraseGraphEdgeCount = 0;
        _phraseGraphTraversalSteps = 0;
        _compiledPhraseClauseCount = 0;
    }

    private static Query CompilePhraseExpansion(string field, int slop, PhraseQuerySyntaxExpansion expansion)
    {
        if (expansion.DirectTerms is not null)
            return new PhraseQuery(field, slop, expansion.DirectTerms);
        if (expansion.Paths.Count == 1)
        {
            PhraseQuerySyntaxPath[] path = expansion.Paths[0];
            return new PhraseQuery(field, path.Select(static item => item.Term).ToArray(), path.Select(static item => item.Position).ToArray(), slop);
        }

        var builder = new BooleanQuery.Builder();
        foreach (PhraseQuerySyntaxPath[] path in expansion.Paths)
        {
            builder.Add(new PhraseQuery(
                field,
                path.Select(static item => item.Term).ToArray(),
                path.Select(static item => item.Position).ToArray(),
                slop), Occur.Should);
        }
        return builder.Build();
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
        if (expansion.Paths.Count > 1)
            consumeAdditionalClauses(expansion.Paths.Count, depth + 1);
        return phrase with { Expansion = expansion };
    }

    private static int ReadSlop(List<QToken> tokens, ref int pos)
    {
        if (pos < tokens.Count && tokens[pos].Type == QTokenType.Tilde)
        {
            pos++;
            if (pos < tokens.Count && tokens[pos].Type == QTokenType.Term &&
                int.TryParse(tokens[pos].Value, out int slop))
            {
                pos++;
                return slop;
            }
        }
        return 0;
    }

    private QuerySyntax ApplyBoost(QuerySyntax query, List<QToken> tokens, ref int pos)
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
                BoostQuerySyntax syntax = new(query, boost, constantScore);
                return constantScore ? CreateSyntaxNode(syntax) : syntax;
            }
        }
        return query;
    }

    private T CreateSyntaxNode<T>(T syntax) where T : QuerySyntax
    {
        if (_syntaxNodeCount >= _maxSyntaxNodes)
            throw new QueryParseLimitException("The query exceeds the configured Boolean clause limit.");
        _syntaxNodeCount++;
        return syntax;
    }

    private Query? Compile(QuerySyntax syntax) => syntax switch
    {
        EmptyQuerySyntax => null,
        GroupQuerySyntax group => Compile(group.Inner),
        TermQuerySyntax term => new TermQuery(term.Field, term.Term),
        FuzzyQuerySyntax fuzzy => CompileFuzzy(fuzzy),
        MultiTermQuerySyntax multiTerm => CompileMultiTerm(multiTerm),
        PhraseQuerySyntax phrase => phrase.Expansion is null
            ? BuildPhraseQuery(phrase.Field, phrase.Text, phrase.Slop)
            : CompilePhraseExpansion(phrase.Field, phrase.Slop, phrase.Expansion),
        RegexpQuerySyntax regexp => CompileRegexp(regexp),
        TermRangeQuerySyntax range => CompileRange(range),
        FieldExistsQuerySyntax exists => new FieldExistsQuery(exists.Field),
        BoostQuerySyntax boost => CompileBoost(boost),
        BooleanQuerySyntax boolean => CompileBoolean(boolean),
        DisjunctionMaxQuerySyntax disjunction => CompileDisjunctionMax(disjunction),
        _ => throw new InvalidOperationException($"Unsupported query syntax '{syntax.GetType().Name}'.")
    };

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
        foreach (QuerySyntaxClause clause in syntax.Clauses)
        {
            Query? query = Compile(clause.Query);
            if (query is null)
                continue;
            builder.Add(query, clause.Occur);
            count++;
        }
        return count == 0 ? null : builder.Build();
    }

    private Query? CompileDisjunctionMax(DisjunctionMaxQuerySyntax syntax)
    {
        var disjunction = new DisjunctionMaxQuery.Builder();
        Query? only = null;
        int count = 0;
        foreach (QuerySyntax clause in syntax.Clauses)
        {
            Query? query = Compile(clause);
            if (query is null)
                continue;
            only = query;
            disjunction.Add(query);
            count++;
        }
        return count switch
        {
            0 => null,
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
        return literal =>
        {
            string analysed = AnalyseSingleToken(analyser, literal);
            return analysed.Length == 0 ? literal : analysed;
        };
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
            return new QueryFieldCompilationContext(field, _analyser, MultiTermNormaliser: null);
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

    private static List<QToken> Tokenize(string input, bool lenient, int maximumTokens)
    {
        var tokens = new List<QToken>();
        int i = 0;

        void AddToken(QToken token)
        {
            if (tokens.Count >= maximumTokens)
                throw new QueryParseLimitException("The query exceeds the configured parser token limit.");
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
                    if (lenient)
                    {
                        // Treat the unterminated phrase content as a plain term token.
                        string raw = input[start..];
                        AddToken(new QToken(QTokenType.Term, Unescape(raw), quoteOffset, raw,
                            HasUnescapedWildcard(raw.AsSpan())));
                        continue;
                    }
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

                string raw = input[start..i];
                string termValue = hasEscapes ? Unescape(raw.AsSpan()) : raw;
                var type = !hasEscapes ? GetKeywordType(termValue) : QTokenType.Term;
                AddToken(new QToken(type, termValue, start, raw, HasUnescapedWildcard(raw.AsSpan())));
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
    private readonly record struct ParsedSyntaxClause(QuerySyntax? Query, Occur Occur);

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
internal sealed record EmptyQuerySyntax : QuerySyntax;
internal sealed record GroupQuerySyntax(QuerySyntax Inner) : QuerySyntax;
internal sealed record TermQuerySyntax(string Field, string Term) : QuerySyntax;
internal sealed record FuzzyQuerySyntax(string Field, string Term, int MaxEdits, int ModifierOffset) : QuerySyntax;
internal sealed record MultiTermQuerySyntax(string Field, string Pattern, string Term) : QuerySyntax;
internal sealed record PhraseQuerySyntax(string Field, string Text, int Slop, PhraseQuerySyntaxExpansion? Expansion = null) : QuerySyntax;
internal sealed record PhraseQuerySyntaxExpansion(string[]? DirectTerms, IReadOnlyList<PhraseQuerySyntaxPath[]> Paths);
internal readonly record struct PhraseQuerySyntaxPath(string Term, int Position);
internal sealed record RegexpQuerySyntax(string Field, string Pattern) : QuerySyntax;
internal sealed record TermRangeQuerySyntax(string Field, string? LowerTerm, string? UpperTerm, bool IncludeLower, bool IncludeUpper) : QuerySyntax;
internal sealed record FieldExistsQuerySyntax(string Field) : QuerySyntax;
internal sealed record BoostQuerySyntax(QuerySyntax Inner, float Boost, bool ConstantScore) : QuerySyntax;
internal sealed record BooleanQuerySyntax(IReadOnlyList<QuerySyntaxClause> Clauses) : QuerySyntax;
internal sealed record DisjunctionMaxQuerySyntax(IReadOnlyList<QuerySyntax> Clauses) : QuerySyntax;
internal readonly record struct QuerySyntaxClause(QuerySyntax Query, Occur Occur);
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
