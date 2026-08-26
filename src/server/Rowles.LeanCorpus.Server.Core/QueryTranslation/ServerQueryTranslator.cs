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
            int clauseCount = 0;
            int clauseLimit = Math.Min(options.MaximumBooleanClauses, maximumBooleanClauses ?? options.MaximumBooleanClauses);
            query = Translate(definition, schema, options, defaultField, clauseLimit, depth: 0, ref clauseCount);
            return true;
        }
        catch (QueryTranslationException exception)
        {
            failure = new ApiFailure(exception.Code, exception.Message);
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            failure = new ApiFailure("invalid_query", exception.Message);
            return false;
        }
    }

    private static Query Translate(QueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options, string? defaultField, int clauseLimit, int depth, ref int clauseCount)
    {
        if (definition is null)
            throw new QueryTranslationException("invalid_query", "A query definition is required.");
        if (depth > options.MaximumQueryDepth)
            throw new QueryTranslationException("query_too_complex", "The query exceeds the configured nesting depth.");
        clauseCount++;
        if (clauseCount > clauseLimit)
            throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");

        return definition switch
        {
            QueryStringDefinition queryString => TranslateQueryString(queryString, schema, defaultField),
            TermQueryDefinition term => new TermQuery(ValidateField(term.Field, schema, allowText: true), term.Value),
            PhraseQueryDefinition phrase => TranslatePhrase(phrase, schema),
            PrefixQueryDefinition prefix => TranslatePrefix(prefix, schema),
            WildcardQueryDefinition wildcard => TranslateWildcard(wildcard, schema, options),
            RegexpQueryDefinition regexp => TranslateRegexp(regexp, schema, options),
            BooleanQueryDefinition boolean => TranslateBoolean(boolean, schema, options, defaultField, clauseLimit, depth, ref clauseCount),
            SpanNearQueryDefinition span => TranslateSpan(span, schema, options, clauseLimit, depth, ref clauseCount),
            VectorQueryDefinition vector => TranslateVector(vector, schema, options, defaultField, clauseLimit, depth, ref clauseCount),
            _ => throw new QueryTranslationException("unsupported_query", $"Query type '{definition.GetType().Name}' is not supported.")
        };
    }

    private static Query TranslateQueryString(QueryStringDefinition definition, CompiledIndexSchema schema, string? defaultField)
    {
        if (string.IsNullOrWhiteSpace(definition.Text))
            throw new QueryTranslationException("invalid_query", "Query-string text is required.");
        string field = definition.DefaultField ?? defaultField ?? schema.Fields.Values.FirstOrDefault(item => item.Source.Type == Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text)?.Source.Name
            ?? throw new QueryTranslationException("invalid_query", "A default text field is required for query-string searches.");
        CompiledFieldDefinition fieldDefinition = GetField(field, schema);
        if (fieldDefinition.Source.Type != Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Text)
            throw new QueryTranslationException("invalid_query_field", $"Query-string field '{field}' must be a text field.");
        return new QueryParser(field, fieldDefinition.Analyser ?? new StandardAnalyser()).Parse(definition.Text);
    }

    private static Query TranslatePhrase(PhraseQueryDefinition definition, CompiledIndexSchema schema)
    {
        ValidateTextField(definition.Field, schema);
        if (definition.Terms is null || definition.Terms.Count == 0)
            throw new QueryTranslationException("invalid_query", "Phrase queries require at least one term.");
        if (definition.Slop < 0)
            throw new QueryTranslationException("invalid_query", "Phrase slop cannot be negative.");
        return new PhraseQuery(definition.Field, definition.Slop, definition.Terms.ToArray());
    }

    private static Query TranslatePrefix(PrefixQueryDefinition definition, CompiledIndexSchema schema)
    {
        ValidateField(definition.Field, schema, allowText: true);
        if (string.IsNullOrEmpty(definition.Prefix))
            throw new QueryTranslationException("invalid_query", "Prefix queries require a non-empty prefix.");
        return new PrefixQuery(definition.Field, definition.Prefix);
    }

    private static Query TranslateWildcard(WildcardQueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options)
    {
        ValidateField(definition.Field, schema, allowText: true);
        if (string.IsNullOrEmpty(definition.Pattern) || definition.Pattern.Length > options.MaximumWildcardExpansions)
            throw new QueryTranslationException("query_too_complex", "The wildcard pattern exceeds the configured limit.");
        return new WildcardQuery(definition.Field, definition.Pattern);
    }

    private static Query TranslateRegexp(RegexpQueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options)
    {
        ValidateField(definition.Field, schema, allowText: true);
        if (string.IsNullOrEmpty(definition.Pattern) || definition.Pattern.Length > options.MaximumRegexpComplexity)
            throw new QueryTranslationException("query_too_complex", "The regular expression exceeds the configured complexity limit.");
        return new RegexpQuery(definition.Field, definition.Pattern);
    }

    private static Query TranslateBoolean(BooleanQueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options, string? defaultField, int clauseLimit, int depth, ref int clauseCount)
    {
        int clauseTotal = (definition.Must?.Count ?? 0) + (definition.Should?.Count ?? 0) + (definition.MustNot?.Count ?? 0);
        if (clauseTotal == 0)
            throw new QueryTranslationException("invalid_query", "Boolean queries require at least one clause.");
        if (clauseTotal > clauseLimit)
            throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");
        BooleanQuery.Builder builder = new();
        AddClauses(builder, definition.Must, Occur.Must, schema, options, defaultField, clauseLimit, depth, ref clauseCount);
        AddClauses(builder, definition.Should, Occur.Should, schema, options, defaultField, clauseLimit, depth, ref clauseCount);
        AddClauses(builder, definition.MustNot, Occur.MustNot, schema, options, defaultField, clauseLimit, depth, ref clauseCount);
        if (definition.MinimumShouldMatch is int minimum)
        {
            if (minimum < 0)
                throw new QueryTranslationException("invalid_query", "MinimumShouldMatch cannot be negative.");
            builder.SetMinimumNumberShouldMatch(minimum);
        }
        return builder.Build();
    }

    private static void AddClauses(BooleanQuery.Builder builder, IReadOnlyList<QueryDefinition>? clauses, Occur occur, CompiledIndexSchema schema, ServerCoreOptions options, string? defaultField, int clauseLimit, int depth, ref int clauseCount)
    {
        if (clauses is null)
            return;
        foreach (QueryDefinition clause in clauses)
            builder.Add(Translate(clause, schema, options, defaultField, clauseLimit, depth + 1, ref clauseCount), occur);
    }

    private static Query TranslateSpan(SpanNearQueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options, int clauseLimit, int depth, ref int clauseCount)
    {
        if (definition.Clauses is null || definition.Clauses.Count == 0 || definition.Slop < 0)
            throw new QueryTranslationException("invalid_query", "SpanNear requires clauses and a non-negative slop.");
        SpanQuery[] clauses = new SpanQuery[definition.Clauses.Count];
        string? field = null;
        for (int i = 0; i < definition.Clauses.Count; i++)
        {
            if (definition.Clauses[i] is not TermQueryDefinition term)
                throw new QueryTranslationException("unsupported_query", "SpanNear currently supports term clauses only.");
            ValidateTextField(term.Field, schema);
            field ??= term.Field;
            if (!string.Equals(field, term.Field, StringComparison.Ordinal))
                throw new QueryTranslationException("invalid_query", "All SpanNear clauses must use the same field.");
            clauses[i] = new SpanTermQuery(term.Field, term.Value);
            clauseCount++;
            if (clauseCount > clauseLimit)
                throw new QueryTranslationException("query_too_complex", "The query exceeds the configured Boolean clause limit.");
        }
        return new SpanNearQuery(clauses, definition.Slop, definition.InOrder);
    }

    private static Query TranslateVector(VectorQueryDefinition definition, CompiledIndexSchema schema, ServerCoreOptions options, string? defaultField, int clauseLimit, int depth, ref int clauseCount)
    {
        CompiledFieldDefinition field = GetField(definition.Field, schema);
        if (field.Source.Type != Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType.Vector)
            throw new QueryTranslationException("invalid_query_field", $"Vector field '{definition.Field}' is not a vector field.");
        if (definition.Vector is null || definition.Vector.Count != field.Source.VectorDimensions || definition.Vector.Any(component => !float.IsFinite(component)))
            throw new QueryTranslationException("invalid_vector", $"Vector queries require exactly {field.Source.VectorDimensions} finite values.");
        if (definition.CandidateCount is < 1 or > 100_000)
            throw new QueryTranslationException("invalid_query", "Vector candidate count must be between 1 and 100000.");
        Query? filter = definition.Filter is null ? null : Translate(definition.Filter, schema, options, defaultField, clauseLimit, depth + 1, ref clauseCount);
        return new VectorQuery(definition.Field, definition.Vector.ToArray(), definition.CandidateCount, filter: filter);
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
