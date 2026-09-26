using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.QueryTranslation;
using Rowles.LeanCorpus.Server.Core.Runtime;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class ServerQueryTranslatorTests
{
    [Theory]
    [InlineData("missing:guide", "invalid_query_field")]
    [InlineData("((guide))", "query_too_complex")]
    [InlineData("guide OR guide", "query_too_complex")]
    [InlineData("gui*", "query_too_complex")]
    [InlineData("/foo/", "query_too_complex")]
    public void QueryStringHonoursSchemaAndComplexityLimits(string text, string expectedFailureCode)
    {
        CompiledIndexSchema schema = CreateSchema();
        ServerCoreOptions options = new()
        {
            MaximumQueryDepth = 1,
            MaximumBooleanClauses = 2,
            MaximumWildcardExpansions = 2,
            MaximumRegexpComplexity = 2
        };

        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition(text),
            schema,
            options,
            defaultField: "title",
            maximumBooleanClauses: null,
            out _,
            out var failure);

        Assert.False(translated);
        Assert.Equal(expectedFailureCode, failure?.Code);
    }

    [Fact]
    public void FieldExistsCanTargetAnIndexedNonTextField()
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition("_exists_:year"),
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.True(translated, failure?.Message);
        Assert.Equal("year", Assert.IsType<Rowles.LeanCorpus.Search.Queries.FieldExistsQuery>(query).Field);
    }

    [Fact]
    public void QueryStringUsesAnalyserFromExplicitField()
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition("exactText:ABC-123"),
            CreateSchemaWithDifferentTextAnalysers(),
            new ServerCoreOptions(),
            defaultField: "body",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.True(translated, failure?.Message);
        var term = Assert.IsType<Rowles.LeanCorpus.Search.Queries.TermQuery>(query);
        Assert.Equal("exactText", term.Field);
        Assert.Equal("ABC-123", term.Term);
    }

    [Fact]
    public void StructuredQueriesUseTheCompilationBudgetBeforeQueryConstruction()
    {
        ServerCoreOptions options = new() { MaximumBooleanClauses = 2 };
        BooleanQueryDefinition definition = new(Should:
        [
            new TermQueryDefinition("title", "guide"),
            new TermQueryDefinition("title", "search")
        ]);

        bool translated = ServerQueryTranslator.TryTranslate(
            definition,
            CreateSchema(),
            options,
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.False(translated);
        Assert.Null(query);
        Assert.Equal("query_too_complex", failure?.Code);
    }

    private static CompiledIndexSchema CreateSchema() => CompiledIndexSchema.Create(
        new IndexSchema(
            [
                new IndexFieldDefinition("title", IndexFieldType.Text, Indexed: true, Stored: true),
                new IndexFieldDefinition("year", IndexFieldType.Int64, Indexed: true, Stored: true)
            ],
            new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "title", null));

    private static CompiledIndexSchema CreateSchemaWithDifferentTextAnalysers() => CompiledIndexSchema.Create(
        new IndexSchema(
            [
                new IndexFieldDefinition("body", IndexFieldType.Text, Indexed: true, Stored: true, Analyser: "standard"),
                new IndexFieldDefinition("exactText", IndexFieldType.Text, Indexed: true, Stored: true, Analyser: "keyword")
            ],
            new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "body", null));
}
