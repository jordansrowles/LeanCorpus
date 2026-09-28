namespace Rowles.LeanCorpus.Search.Parsing;

internal sealed class QueryCompiler
{
    private readonly QueryParser _parser;
    private readonly QueryParserOptions _options;
    private readonly bool _parseLimitsAreComplexity;
    private readonly Dictionary<string, QueryFieldCompilationContext> _fieldContexts = new(StringComparer.Ordinal);
    private readonly QuerySyntaxBudget _syntaxBudget;
    private bool _graphPathLimitIsComplexity;
    private readonly QueryCompilationBudget _queryCompilationBudget;

    internal QuerySyntaxBudget SyntaxBudget => _syntaxBudget;

    internal QueryCompiler(QueryParser parser, QueryParserOptions options, bool parseLimitsAreComplexity)
    {
        _parser = parser;
        _options = options;
        _parseLimitsAreComplexity = parseLimitsAreComplexity;
        _syntaxBudget = new QuerySyntaxBudget(options, parseLimitsAreComplexity);
        _queryCompilationBudget = new QueryCompilationBudget(options);
    }

    internal QuerySyntax LowerAnalysedSyntax(QuerySyntax syntax)
    {
        _syntaxBudget.BeginQueryClauseCounting();
        try
        {
            return LowerAnalysedSyntaxCore(syntax);
        }
        finally
        {
            _syntaxBudget.EndQueryClauseCounting();
        }
    }

    internal QuerySyntax PrepareSyntax(
        QuerySyntax syntax,
        Action<int, int> consumeAdditionalClauses,
        bool graphPathLimitIsComplexity)
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
        Query? query = syntax is AnalysedEmptyQuerySyntax ? null : CompileQuery(syntax);
        return query switch
        {
            NoClauseQuery => new MatchNoDocsQuery(),
            null => new BooleanQuery.Builder().Build(),
            _ => query
        };
    }

    internal Query BuildPhraseQuery(string field, string phraseText, int slop, int sourceOffset = 0) =>
        CompilePhraseExpansion(field, slop, CreatePhraseExpansion(field, phraseText, sourceOffset), sourceOffset);

    internal QueryFieldCompilationContext ResolveFieldContext(string field)
    {
        Func<string, QueryFieldCompilationContext>? resolver = _parser.FieldContextResolverForCompilation;
        if (resolver is null)
            return new QueryFieldCompilationContext(field, _parser.CurrentAnalyserForCompilation, MultiTermNormaliser: null);
        if (_fieldContexts.TryGetValue(field, out QueryFieldCompilationContext? context))
            return context;

        context = resolver(field)
            ?? throw new QueryParseException("The field context resolver returned no context.", 0);
        if (!string.Equals(context.Field, field, StringComparison.Ordinal))
            throw new QueryParseException("The field context resolver returned a context for a different field.", 0);

        _fieldContexts.Add(field, context);
        return context;
    }

    internal string AnalyseComplexPhraseTerm(string field, ReadOnlySpan<char> term, int sourceOffset)
    {
        var tokens = new QueryAnalysisTokenBuffer(this, sourceOffset);
        ResolveFieldContext(field).QueryAnalyser.Analyse(
            term, tokens);
        if (tokens.Count != 1 || tokens[0].PositionLength != 1)
        {
            throw new QueryParseException(
                "Each term in a complex phrase alternative must analyse to exactly one linear token.",
                sourceOffset);
        }

        _ = CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: true, sourceOffset);
        return tokens[0].Text;
    }

    internal void ConsumeComplexPhraseClauses(int clauseCount, int alternativeClauseCount, int sourceOffset)
    {
        if (clauseCount < 0 || alternativeClauseCount < 0 || alternativeClauseCount > clauseCount)
            throw new ArgumentOutOfRangeException(nameof(clauseCount));

        _syntaxBudget.ConsumeQueryClauses(clauseCount, sourceOffset);
        if (alternativeClauseCount == 0)
            return;

        string? clauseLimit = GetQueryCompilationBudget().TryGenerateBooleanClauses(alternativeClauseCount);
        if (clauseLimit is not null)
            ThrowPhraseGraphLimitExceeded(clauseLimit, sourceOffset);
    }

    private T CreateSyntaxNode<T>(T syntax) where T : QuerySyntax =>
        _syntaxBudget.CreateSyntaxNode(syntax);

    private string AnalyseMultiTerm(string field, string pattern)
    {
        Func<string, string>? normaliseLiteral = ResolveFieldContext(field).MultiTermNormaliser;
        return normaliseLiteral is null
            ? _parser.AnalyseMultiTermLiteralForCompilation(pattern)
            : QueryParser.NormaliseMultiTermPattern(pattern, normaliseLiteral);
    }

    private string AnalyseRangeBound(string field, string term)
    {
        Func<string, string>? normaliseLiteral = ResolveFieldContext(field).MultiTermNormaliser;
        return normaliseLiteral is null
            ? _parser.AnalyseRangeBoundLiteralForCompilation(term)
            : normaliseLiteral(term);
    }

    private QuerySyntax LowerAnalysedSyntaxCore(QuerySyntax syntax)
    {
        try
        {
            QuerySyntax lowered = syntax switch
            {
                UnanalysedTermQuerySyntax term => LowerUnanalysedTermSyntax(
                    term.Field,
                    term.Term,
                    sourceOffset: term.SourceSpan.Start),
                UnanalysedFuzzyQuerySyntax fuzzy => LowerUnanalysedTermSyntax(
                    fuzzy.Field,
                    fuzzy.Term,
                    fuzzy.MaxEdits,
                    fuzzy.ModifierOffset,
                    fuzzy.SourceSpan.Start),
                GroupQuerySyntax group => LowerGroupSyntax(group),
                BooleanQuerySyntax boolean => LowerBooleanSyntax(boolean),
                DisjunctionMaxQuerySyntax disjunction => LowerDisjunctionSyntax(disjunction),
                BoostQuerySyntax boost => LowerBoostSyntax(boost),
                _ => CreateSyntaxNode(syntax)
            };
            return syntax.SourceSpan.Length == 0 ? lowered : lowered.WithSourceSpan(syntax.SourceSpan);
        }
        catch (QueryParseException exception) when (NeedsSourceOffset(exception, syntax.SourceSpan))
        {
            throw new QueryParseException(exception.Message, syntax.SourceSpan.Start);
        }
        catch (QueryParseLimitException exception) when (NeedsSourceOffset(exception, syntax.SourceSpan))
        {
            throw new QueryParseLimitException(exception.Message, syntax.SourceSpan.Start);
        }
    }

    private static bool NeedsSourceOffset(QueryParseException exception, QuerySourceSpan sourceSpan) =>
        sourceSpan.Length > 0 && (!exception.HasOffset || (exception.Offset == 0 && sourceSpan.Start != 0));

    private static bool NeedsSourceOffset(QueryParseLimitException exception, QuerySourceSpan sourceSpan) =>
        sourceSpan.Length > 0 &&
        (!exception.Offset.HasValue || (exception.Offset == 0 && sourceSpan.Start != 0));

    private QuerySyntax LowerUnanalysedTermSyntax(
        string field,
        string term,
        int? fuzzyMaxEdits = null,
        int modifierOffset = 0,
        int sourceOffset = 0)
    {
        QuerySyntax? lowered = LowerAnalysedTokens(
            field,
            term,
            _parser.AnalyseTermForCompilation(field, term),
            fuzzyMaxEdits,
            modifierOffset,
            sourceOffset);
        return lowered ?? CreateSyntaxNode(new AnalysedEmptyQuerySyntax());
    }

    private QuerySyntax LowerGroupSyntax(GroupQuerySyntax group)
    {
        QuerySyntax inner = LowerAnalysedSyntaxCore(group.Inner);
        return IsEmptySyntax(inner)
            ? inner
            : CreateSyntaxNode(group with { Inner = inner });
    }

    private QuerySyntax LowerBoostSyntax(BoostQuerySyntax boost)
    {
        QuerySyntax inner = LowerAnalysedSyntaxCore(boost.Inner);
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
            QuerySyntax loweredQuery = LowerAnalysedSyntaxCore(clause.Query);
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
            QuerySyntax loweredClause = LowerAnalysedSyntaxCore(clause);
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

    private PhraseQuerySyntaxExpansion CreatePhraseExpansion(string field, string phraseText, int sourceOffset)
    {
        var tokens = new QueryAnalysisTokenBuffer(this, sourceOffset);
        ResolveFieldContext(field).QueryAnalyser.Analyse(phraseText.AsSpan(), tokens);
        return CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: true, sourceOffset);
    }

    private PhraseQuerySyntaxExpansion CreatePhraseExpansionFromTokens(
        IReadOnlyList<Analysis.Token> tokens,
        bool tokensAlreadyCounted,
        int sourceOffset)
    {
        if (tokens.Count == 0)
            return new PhraseQuerySyntaxExpansion(null, null, 0, 0);

        if (!tokensAlreadyCounted)
        {
            foreach (var _ in tokens)
                ConsumeAnalysedPhraseToken(sourceOffset);
        }

        QueryCompilationBudget budget = GetQueryCompilationBudget();
        var graph = new Analysis.TokenGraph();
        for (int index = 0; index < tokens.Count; index++)
        {
            Analysis.Token token = tokens[index];
            string? edgeLimit = budget.TryReadGraphEdge();
            if (edgeLimit is not null)
                ThrowPhraseGraphLimitExceeded(edgeLimit, sourceOffset);
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
                ThrowPhraseGraphLimitExceeded(pathLimit, sourceOffset);

            pathCount++;
            compiledTermCount += path.Count;

            int newBooleanClauses = pathCount == 2 ? 2 : pathCount > 2 ? 1 : 0;
            if (newBooleanClauses > 0)
            {
                string? clauseLimit = budget.TryGenerateBooleanClauses(newBooleanClauses);
                if (clauseLimit is not null)
                    ThrowPhraseGraphLimitExceeded(clauseLimit, sourceOffset);
            }
        }, countTraversalSteps: true, sourceOffset);

        if (pathCount == 0)
            throw new QueryParseException("Analysed phrase token graph has no complete path.", sourceOffset);

        return new PhraseQuerySyntaxExpansion(null, graphPlan, pathCount, compiledTermCount);
    }

    private void EnumeratePhraseGraphPaths(
        PhraseGraphPlan graph,
        Action<IReadOnlyList<Analysis.TokenGraph.TokenEdge>> onCompletePath,
        bool countTraversalSteps,
        int sourceOffset = 0)
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
                    ThrowPhraseGraphLimitExceeded(traversalLimit, sourceOffset);
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
        int modifierOffset = 0,
        int sourceOffset = 0)
    {
        if (tokens.Count == 0)
            return null;

        bool hasGraphEdge = false;
        for (int index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].PositionLength != 1)
            {
                hasGraphEdge = true;
                break;
            }
        }

        if (hasGraphEdge)
        {
            if (fuzzyMaxEdits.HasValue)
            {
                throw new QueryParseException(
                    "A fuzzy query analyser must not emit multi-position graph edges.", modifierOffset);
            }

            var expansion = CreatePhraseExpansionFromTokens(tokens, tokensAlreadyCounted: false, sourceOffset);
            return CreateSyntaxNode(new PhraseQuerySyntax(field, sourceText, 0, expansion));
        }

        var graph = new Analysis.TokenGraph();
        for (int index = 0; index < tokens.Count; index++)
            graph.Add(tokens[index]);
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

    internal void ConsumeAnalysedPhraseToken(int sourceOffset)
    {
        string? tokenLimit = GetQueryCompilationBudget().TryConsumePhraseTokens(1);
        if (tokenLimit is not null)
            ThrowPhraseGraphLimitExceeded(tokenLimit, sourceOffset);
    }

    private void ThrowPhraseGraphLimitExceeded(string message, int sourceOffset)
    {
        ThrowQueryParseLimitExceeded(message, sourceOffset);
    }

    internal void ThrowQueryParseLimitExceeded(string message, int? offset = null)
    {
        if (_graphPathLimitIsComplexity || _parseLimitsAreComplexity)
            throw new QueryParseLimitException(message, offset);

        if (offset is int value)
            throw new QueryParseException(message, value);

        throw new QueryParseException(message);
    }

    private QueryCompilationBudget GetQueryCompilationBudget() =>
        _queryCompilationBudget;

    internal Query CompilePhraseExpansion(
        string field,
        int slop,
        PhraseQuerySyntaxExpansion expansion,
        int sourceOffset = 0)
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
        }, countTraversalSteps: false, sourceOffset);

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
        int sourceOffset = phrase.SourceSpan.Length == 0 ? 0 : phrase.SourceSpan.Start;
        PhraseQuerySyntaxExpansion expansion = phrase.Expansion ?? CreatePhraseExpansion(phrase.Field, phrase.Text, sourceOffset);
        if (expansion.PathCount > 1)
            consumeAdditionalClauses(expansion.PathCount, depth + 1);
        return phrase with { Expansion = expansion };
    }

    private Query? CompileQuery(QuerySyntax syntax)
    {
        try
        {
            return syntax switch
            {
                EmptyQuerySyntax => null,
                AnalysedEmptyQuerySyntax => NoClauseQuery.Instance,
                RecoveredQuerySyntax => null,
                GroupQuerySyntax group => CompileQuery(group.Inner),
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
        }
        catch (QueryParseException exception) when (NeedsSourceOffset(exception, syntax.SourceSpan))
        {
            throw new QueryParseException(exception.Message, syntax.SourceSpan.Start);
        }
        catch (QueryParseLimitException exception) when (NeedsSourceOffset(exception, syntax.SourceSpan))
        {
            throw new QueryParseLimitException(exception.Message, syntax.SourceSpan.Start);
        }
    }

    private Query CompilePhrase(PhraseQuerySyntax phrase)
    {
        int sourceOffset = phrase.SourceSpan.Length == 0 ? 0 : phrase.SourceSpan.Start;
        if (phrase.Expansion is not null)
            return CompilePhraseExpansion(phrase.Field, phrase.Slop, phrase.Expansion, sourceOffset);

        return _parser.BuildPhraseQueryForCompilation(
            phrase.Field,
            phrase.Text,
            phrase.RawText ?? phrase.Text,
            phrase.Slop,
            phrase.SourceSpan);
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
        Query? inner = CompileQuery(syntax.Inner);
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
            Query? query = CompileQuery(clause.Query);
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
            Query? query = CompileQuery(clause);
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
