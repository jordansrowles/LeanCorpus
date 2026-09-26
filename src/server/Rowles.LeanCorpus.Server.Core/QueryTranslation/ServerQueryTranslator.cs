using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Runtime;

namespace Rowles.LeanCorpus.Server.Core.QueryTranslation;

internal static class ServerQueryTranslator
{
    internal static bool TryTranslate(
        QueryDefinition definition,
        CompiledIndexSchema schema,
        ServerCoreOptions options,
        string? defaultField,
        int? maximumBooleanClauses,
        out Query? query,
        out ApiFailure? failure)
    {
        query = null;
        failure = null;
        try
        {
            int clauseLimit = Math.Min(options.MaximumBooleanClauses, maximumBooleanClauses ?? options.MaximumBooleanClauses);
            QueryCompilationContext context = new(schema, options, defaultField, clauseLimit);

            // Validate the complete definition and every parsed text tree before
            // the first executable Query object is constructed.
            context.Validate(definition, depth: 0);
            query = context.Compile(definition);
            return true;
        }
        catch (QueryTranslationException exception)
        {
            failure = new ApiFailure(exception.Code, exception.Message);
            return false;
        }
        catch (QueryParseLimitException exception)
        {
            failure = new ApiFailure("query_too_complex", exception.Message);
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            failure = new ApiFailure("invalid_query", exception.Message);
            return false;
        }
    }

    private sealed class QueryCompilationContext(
        CompiledIndexSchema schema,
        ServerCoreOptions options,
        string? defaultField,
        int clauseLimit)
    {
        private readonly QueryCompilationBudget _budget = new(options.MaximumQueryDepth, clauseLimit);
        private readonly Dictionary<QueryStringDefinition, QueryStringPlan> _textPlans = new(ReferenceEqualityComparer.Instance);

        internal void Validate(QueryDefinition? definition, int depth)
        {
            if (definition is null)
                throw new QueryTranslationException("invalid_query", "A query definition is required.");

            if (definition is QueryStringDefinition queryString)
            {
                ValidateQueryString(queryString, depth);
                return;
            }

            _budget.CountClause(depth);
            switch (definition)
            {
                case TermQueryDefinition term:
                    ValidateField(term.Field, schema, allowText: true);
                    break;

                case PhraseQueryDefinition phrase:
                    ValidateTextField(phrase.Field, schema);
                    if (phrase.Terms is null || phrase.Terms.Count == 0)
                        throw new QueryTranslationException("invalid_query", "Phrase queries require at least one term.");
                    if (phrase.Slop < 0)
                        throw new QueryTranslationException("invalid_query", "Phrase slop cannot be negative.");
                    break;

                case PrefixQueryDefinition prefix:
                    ValidateField(prefix.Field, schema, allowText: true);
                    if (string.IsNullOrEmpty(prefix.Prefix))
                        throw new QueryTranslationException("invalid_query", "Prefix queries require a non-empty prefix.");
                    break;

                case WildcardQueryDefinition wildcard:
                    ValidateField(wildcard.Field, schema, allowText: true);
                    if (string.IsNullOrEmpty(wildcard.Pattern) || wildcard.Pattern.Length > options.MaximumWildcardExpansions)
                        throw new QueryTranslationException("query_too_complex", "The wildcard pattern exceeds the configured limit.");
                    break;

                case RegexpQueryDefinition regexp:
                    ValidateField(regexp.Field, schema, allowText: true);
                    if (string.IsNullOrEmpty(regexp.Pattern) || regexp.Pattern.Length > options.MaximumRegexpComplexity)
                        throw new QueryTranslationException("query_too_complex", "The regular expression exceeds the configured complexity limit.");
                    break;

                case BooleanQueryDefinition boolean:
                    ValidateBoolean(boolean, depth);
                    break;

                case SpanNearQueryDefinition span:
                    ValidateSpan(span, depth);
                    break;

                case VectorQueryDefinition vector:
                    ValidateVector(vector, depth);
                    break;

                default:
                    throw new QueryTranslationException("unsupported_query", $"Query type '{definition.GetType().Name}' is not supported.");
            }
        }

        internal Query Compile(QueryDefinition definition) => definition switch
        {
            QueryStringDefinition queryString => CompileQueryString(queryString),
            TermQueryDefinition term => new TermQuery(term.Field, term.Value),
            PhraseQueryDefinition phrase => new PhraseQuery(phrase.Field, phrase.Slop, phrase.Terms.ToArray()),
            PrefixQueryDefinition prefix => new PrefixQuery(prefix.Field, prefix.Prefix),
            WildcardQueryDefinition wildcard => new WildcardQuery(wildcard.Field, wildcard.Pattern),
            RegexpQueryDefinition regexp => new RegexpQuery(regexp.Field, regexp.Pattern),
            BooleanQueryDefinition boolean => CompileBoolean(boolean),
            SpanNearQueryDefinition span => CompileSpan(span),
            VectorQueryDefinition vector => CompileVector(vector),
            _ => throw new InvalidOperationException($"Unsupported query type '{definition.GetType().Name}'.")
        };

        private void ValidateQueryString(QueryStringDefinition definition, int depth)
        {
            if (string.IsNullOrWhiteSpace(definition.Text))
                throw new QueryTranslationException("invalid_query", "Query-string text is required.");

            string field = ResolveDefaultField(definition, schema, defaultField);
            CompiledFieldDefinition fieldDefinition = GetField(field, schema);
            if (fieldDefinition.Source.Type != Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text)
                throw new QueryTranslationException("invalid_query_field", $"Query-string field '{field}' must be a text field.");

            if (!_textPlans.TryGetValue(definition, out QueryStringPlan? plan))
            {
                int parserDepth = Math.Min(options.MaximumQueryDepth, 64) + 1;
                int maximumTokens = GetParserTokenLimit(clauseLimit, Math.Min(options.MaximumQueryDepth, 64));
                IAnalyser defaultAnalyser = fieldDefinition.Analyser ?? new StandardAnalyser();
                QueryParser parser = new(
                    field,
                    defaultAnalyser,
                    fieldName => ResolveFieldContext(fieldName, field, defaultAnalyser),
                    maxGraphPaths: Math.Min(256, clauseLimit));
                QuerySyntax syntax = parser.ParseSyntax(definition.Text, parserDepth, clauseLimit, maximumTokens, limitsAreComplexity: true);
                plan = new QueryStringPlan(parser, syntax);
                _textPlans.Add(definition, plan);
            }

            ValidateSyntax(plan.Syntax, depth);
            QuerySyntax preparedSyntax = plan.Parser.PrepareSyntax(
                plan.Syntax,
                (count, generatedDepth) =>
                {
                    _budget.CheckDepth(depth + generatedDepth);
                    _budget.CountGeneratedClauses(count);
                },
                graphPathLimitIsComplexity: true);
            _textPlans[definition] = plan with { Syntax = preparedSyntax };
        }

        private void ValidateSyntax(QuerySyntax syntax, int depth)
        {
            switch (syntax)
            {
                case EmptyQuerySyntax:
                    return;

                case GroupQuerySyntax group:
                    _budget.CheckDepth(depth + 1);
                    ValidateSyntax(group.Inner, depth + 1);
                    return;

                case BooleanQuerySyntax boolean:
                    _budget.CountClause(depth);
                    foreach (QuerySyntaxClause clause in boolean.Clauses)
                        ValidateSyntax(clause.Query, depth + 1);
                    return;

                case DisjunctionMaxQuerySyntax disjunction:
                    _budget.CountClause(depth);
                    foreach (QuerySyntax clause in disjunction.Clauses)
                        ValidateSyntax(clause, depth + 1);
                    return;

                case BoostQuerySyntax boost when boost.ConstantScore:
                    _budget.CountClause(depth);
                    ValidateSyntax(boost.Inner, depth + 1);
                    return;

                case BoostQuerySyntax boost:
                    ValidateSyntax(boost.Inner, depth);
                    return;

                case TermQuerySyntax term:
                    _budget.CountClause(depth);
                    ValidateField(term.Field, schema, allowText: true);
                    return;

                case FuzzyQuerySyntax fuzzy:
                    _budget.CountClause(depth);
                    ValidateField(fuzzy.Field, schema, allowText: true);
                    if (fuzzy.MaxEdits is < 0 or > 2)
                        throw new QueryTranslationException("query_too_complex", "Fuzzy query edit distance must be between 0 and 2.");
                    return;

                case MultiTermQuerySyntax multiTerm:
                    _budget.CountClause(depth);
                    ValidateField(multiTerm.Field, schema, allowText: true);
                    if (multiTerm.Term.Length > options.MaximumWildcardExpansions)
                        throw new QueryTranslationException("query_too_complex", "The wildcard pattern exceeds the configured limit.");
                    return;

                case PhraseQuerySyntax phrase:
                    _budget.CountClause(depth);
                    ValidateTextField(phrase.Field, schema);
                    return;

                case RegexpQuerySyntax regexp:
                    _budget.CountClause(depth);
                    ValidateField(regexp.Field, schema, allowText: true);
                    if (regexp.Pattern.Length == 0 || regexp.Pattern.Length > options.MaximumRegexpComplexity)
                        throw new QueryTranslationException("query_too_complex", "The regular expression exceeds the configured complexity limit.");
                    return;

                case TermRangeQuerySyntax range:
                    _budget.CountClause(depth);
                    ValidateField(range.Field, schema, allowText: true);
                    return;

                case FieldExistsQuerySyntax exists:
                    _budget.CountClause(depth);
                    ValidateExistsField(exists.Field, schema);
                    return;

                default:
                    throw new QueryTranslationException("unsupported_query", $"Query syntax '{syntax.GetType().Name}' is not supported.");
            }
        }

        private void ValidateBoolean(BooleanQueryDefinition definition, int depth)
        {
            int clauseTotal = (definition.Must?.Count ?? 0) + (definition.Should?.Count ?? 0) + (definition.MustNot?.Count ?? 0);
            if (clauseTotal == 0)
                throw new QueryTranslationException("invalid_query", "Boolean queries require at least one clause.");
            if (clauseTotal > clauseLimit)
                throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");

            ValidateClauses(definition.Must, depth);
            ValidateClauses(definition.Should, depth);
            ValidateClauses(definition.MustNot, depth);

            if (definition.MinimumShouldMatch is int minimum)
            {
                if (minimum < 0)
                    throw new QueryTranslationException("invalid_query", "MinimumShouldMatch cannot be negative.");
            }
        }

        private void ValidateClauses(IReadOnlyList<QueryDefinition>? clauses, int depth)
        {
            if (clauses is null)
                return;
            foreach (QueryDefinition clause in clauses)
                Validate(clause, depth + 1);
        }

        private void ValidateSpan(SpanNearQueryDefinition definition, int depth)
        {
            if (definition.Clauses is null || definition.Clauses.Count == 0 || definition.Slop < 0)
                throw new QueryTranslationException("invalid_query", "SpanNear requires clauses and a non-negative slop.");

            string? field = null;
            foreach (QueryDefinition clause in definition.Clauses)
            {
                if (clause is not TermQueryDefinition term)
                    throw new QueryTranslationException("unsupported_query", "SpanNear currently supports term clauses only.");
                _budget.CountClause(depth + 1);
                ValidateTextField(term.Field, schema);
                field ??= term.Field;
                if (!string.Equals(field, term.Field, StringComparison.Ordinal))
                    throw new QueryTranslationException("invalid_query", "All SpanNear clauses must use the same field.");
            }
        }

        private void ValidateVector(VectorQueryDefinition definition, int depth)
        {
            CompiledFieldDefinition field = GetField(definition.Field, schema);
            if (field.Source.Type != Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Vector)
                throw new QueryTranslationException("invalid_query_field", $"Vector field '{definition.Field}' is not a vector field.");
            if (definition.Vector is null || definition.Vector.Count != field.Source.VectorDimensions || definition.Vector.Any(component => !float.IsFinite(component)))
                throw new QueryTranslationException("invalid_vector", $"Vector queries require exactly {field.Source.VectorDimensions} finite values.");
            if (definition.CandidateCount is < 1 or > 100_000)
                throw new QueryTranslationException("invalid_query", "Vector candidate count must be between 1 and 100000.");
            if (definition.Filter is not null)
                Validate(definition.Filter, depth + 1);
        }

        private Query CompileQueryString(QueryStringDefinition definition)
        {
            QueryStringPlan plan = _textPlans[definition];
            return plan.Parser.CompileSyntax(plan.Syntax);
        }

        private Query CompileBoolean(BooleanQueryDefinition definition)
        {
            BooleanQuery.Builder builder = new();
            AddClauses(builder, definition.Must, Occur.Must);
            AddClauses(builder, definition.Should, Occur.Should);
            AddClauses(builder, definition.MustNot, Occur.MustNot);
            if (definition.MinimumShouldMatch is int minimum)
                builder.SetMinimumNumberShouldMatch(minimum);
            return builder.Build();
        }

        private void AddClauses(BooleanQuery.Builder builder, IReadOnlyList<QueryDefinition>? clauses, Occur occur)
        {
            if (clauses is null)
                return;
            foreach (QueryDefinition clause in clauses)
                builder.Add(Compile(clause), occur);
        }

        private static Query CompileSpan(SpanNearQueryDefinition definition)
        {
            SpanQuery[] clauses = new SpanQuery[definition.Clauses.Count];
            for (int i = 0; i < definition.Clauses.Count; i++)
            {
                TermQueryDefinition term = (TermQueryDefinition)definition.Clauses[i];
                clauses[i] = new SpanTermQuery(term.Field, term.Value);
            }
            return new SpanNearQuery(clauses, definition.Slop, definition.InOrder);
        }

        private Query CompileVector(VectorQueryDefinition definition)
        {
            Query? filter = definition.Filter is null ? null : Compile(definition.Filter);
            return new VectorQuery(definition.Field, definition.Vector!.ToArray(), definition.CandidateCount, filter: filter);
        }

        private static string ResolveDefaultField(QueryStringDefinition definition, CompiledIndexSchema schema, string? defaultField) =>
            definition.DefaultField ?? defaultField ?? schema.Fields.Values.FirstOrDefault(item => item.Source.Type == Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text)?.Source.Name
                ?? throw new QueryTranslationException("invalid_query", "A default text field is required for query-string searches.");

        private QueryFieldCompilationContext ResolveFieldContext(string field, string defaultField, IAnalyser defaultAnalyser)
        {
            IAnalyser analyser;
            if (string.Equals(field, defaultField, StringComparison.Ordinal))
            {
                analyser = defaultAnalyser;
            }
            else if (string.Equals(field, ServerDocumentMapper.DocumentIdField, StringComparison.Ordinal))
            {
                analyser = new KeywordAnalyser();
            }
            else
            {
                CompiledFieldDefinition definition = GetField(field, schema);
                analyser = definition.Analyser ?? new KeywordAnalyser();
            }

            return new QueryFieldCompilationContext(
                field,
                analyser,
                QueryParser.CreateSingleTokenNormaliser(analyser));
        }

        private static int GetParserTokenLimit(int maximumClauses, int maximumDepth)
        {
            long maximum = Math.Max(1L, 8L * maximumClauses + 2L * maximumDepth + 4L);
            return (int)Math.Min(int.MaxValue, maximum);
        }
    }

    private sealed record QueryStringPlan(QueryParser Parser, QuerySyntax Syntax);

    private sealed class QueryCompilationBudget(int maximumDepth, int maximumClauses)
    {
        private long _clauseCount;

        internal void CheckDepth(int depth)
        {
            if (depth > maximumDepth)
                throw new QueryTranslationException("query_too_complex", "The query exceeds the configured nesting depth.");
        }

        internal void CountClause(int depth)
        {
            CheckDepth(depth);
            _clauseCount++;
            if (_clauseCount > maximumClauses)
                throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");
        }

        internal void CountGeneratedClauses(int count)
        {
            if (count <= 0)
                return;
            _clauseCount += count;
            if (_clauseCount > maximumClauses)
                throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");
        }
    }

    private static string ValidateField(string field, CompiledIndexSchema schema, bool allowText)
    {
        if (string.Equals(field, ServerDocumentMapper.DocumentIdField, StringComparison.Ordinal))
            return field;
        CompiledFieldDefinition definition = GetField(field, schema);
        if (!definition.Source.Indexed
            || definition.Source.Type is Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Binary
                or Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Vector
                or Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Int64
                or Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Double
                or Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.DateTime
            || (!allowText && definition.Source.Type == Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text))
            throw new QueryTranslationException("invalid_query_field", $"Field '{field}' cannot be queried with this query type.");
        return field;
    }

    private static string ValidateTextField(string field, CompiledIndexSchema schema)
    {
        if (string.Equals(field, ServerDocumentMapper.DocumentIdField, StringComparison.Ordinal))
            throw new QueryTranslationException("invalid_query_field", "Span and phrase queries require a text field.");
        CompiledFieldDefinition definition = GetField(field, schema);
        if (!definition.Source.Indexed || definition.Source.Type != Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text)
            throw new QueryTranslationException("invalid_query_field", $"Field '{field}' must be an indexed text field.");
        return field;
    }

    private static string ValidateExistsField(string field, CompiledIndexSchema schema)
    {
        if (string.Equals(field, ServerDocumentMapper.DocumentIdField, StringComparison.Ordinal))
            return field;
        CompiledFieldDefinition definition = GetField(field, schema);
        if (!definition.Source.Indexed)
            throw new QueryTranslationException("invalid_query_field", $"Field '{field}' is not indexed and cannot be queried.");
        return field;
    }

    private static CompiledFieldDefinition GetField(string field, CompiledIndexSchema schema)
    {
        if (string.IsNullOrWhiteSpace(field) || !schema.Fields.TryGetValue(field, out CompiledFieldDefinition? definition))
            throw new QueryTranslationException("invalid_query_field", $"Field '{field}' is not present in the index schema.");
        return definition;
    }

    private sealed class QueryTranslationException(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
