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
            MaximumWildcardPatternChars = 2,
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
    public void QueryStringRejectsExplicitFieldsThatAreNotTextFields()
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition("year:2025"),
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.False(translated);
        Assert.Null(query);
        Assert.Equal("invalid_query_field", failure?.Code);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void StructuredAndTextBooleanQueriesShareClauseBudget(int maximumBooleanClauses, bool expectedSuccess)
    {
        ServerCoreOptions options = new() { MaximumBooleanClauses = maximumBooleanClauses };
        QueryDefinition[] definitions =
        [
            new BooleanQueryDefinition(Should:
            [
                new TermQueryDefinition("title", "guide"),
                new TermQueryDefinition("title", "search")
            ]),
            new QueryStringDefinition("guide OR search")
        ];

        foreach (QueryDefinition definition in definitions)
        {
            bool translated = ServerQueryTranslator.TryTranslate(
                definition,
                CreateSchema(),
                options,
                defaultField: "title",
                maximumBooleanClauses: null,
                out var query,
                out var failure);

            Assert.Equal(expectedSuccess, translated);
            if (expectedSuccess)
            {
                Assert.NotNull(query);
                Assert.Null(failure);
            }
            else
            {
                Assert.Null(query);
                Assert.Equal("query_too_complex", failure?.Code);
            }
        }
    }

    [Fact]
    public void QueryStringHonoursInputTokenAndSyntaxNodeBudgets()
    {
        AssertQueryStringRejected("guide", new ServerCoreOptions { MaximumQueryInputChars = 4 });
        AssertQueryStringRejected("one OR two", new ServerCoreOptions { MaximumQueryTokens = 2 });
        AssertQueryStringRejected("guide", new ServerCoreOptions { MaximumQuerySyntaxNodes = 1 });
    }

    [Fact]
    public void QueryStringHonoursFuzzyPhraseAndPhraseGraphBudgets()
    {
        AssertQueryStringRejected("guide~2", new ServerCoreOptions { MaximumFuzzyEdits = 1 });
        AssertQueryStringRejected("\"guide\"~2", new ServerCoreOptions { MaximumPhraseSlop = 1 });
        AssertQueryStringRejected("\"guide search\"", new ServerCoreOptions { MaximumPhraseTokens = 1 });
        AssertQueryStringRejected("\"guide search\"", new ServerCoreOptions { MaximumPhraseGraphEdges = 1 });
    }

    [Fact]
    public void StructuredPhraseHonoursConfiguredSlopBudget()
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new PhraseQueryDefinition("title", ["guide"], 2),
            CreateSchema(),
            new ServerCoreOptions { MaximumPhraseSlop = 1 },
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.False(translated);
        Assert.Null(query);
        Assert.Equal("query_too_complex", failure?.Code);
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
    public void QueryStringUsesFullAnalysisAndNormalisesMultiTermLiterals()
    {
        var term = Assert.IsType<Rowles.LeanCorpus.Search.Queries.TermQuery>(TranslateQueryString("GUIDE"));
        var phrase = Assert.IsType<Rowles.LeanCorpus.Search.Queries.PhraseQuery>(TranslateQueryString("\"GUIDE SEARCH\""));
        var prefix = Assert.IsType<Rowles.LeanCorpus.Search.Queries.PrefixQuery>(TranslateQueryString("GUIDE*"));
        var wildcard = Assert.IsType<Rowles.LeanCorpus.Search.Queries.WildcardQuery>(TranslateQueryString("GU?DE"));
        var range = Assert.IsType<Rowles.LeanCorpus.Search.Queries.TermRangeQuery>(TranslateQueryString("[GUIDE TO WOLF]"));

        Assert.Equal("guide", term.Term);
        Assert.Equal(new[] { "guide", "search" }, phrase.Terms);
        Assert.Equal(new[] { 0, 1 }, phrase.Positions);
        Assert.Equal("guide", prefix.Prefix);
        Assert.Equal("gu?de", wildcard.Pattern);
        Assert.Equal("guide", range.LowerTerm);
        Assert.Equal("wolf", range.UpperTerm);
    }

    [Fact]
    public void QueryStringStopwordOperandIsLoweredAfterSyntaxRecognition()
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition("guide AND the"),
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.True(translated, failure?.Message);
        Assert.IsType<Rowles.LeanCorpus.Search.Queries.MatchNoDocsQuery>(query);
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

    [Fact]
    public void StructuredAndTextClausesShareOneCompilationBudget()
    {
        BooleanQueryDefinition definition = new(Should:
        [
            new TermQueryDefinition("title", "guide"),
            new QueryStringDefinition("search OR guide")
        ]);

        bool translated = ServerQueryTranslator.TryTranslate(
            definition,
            CreateSchema(),
            new ServerCoreOptions { MaximumBooleanClauses = 3 },
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.False(translated);
        Assert.Null(query);
        Assert.Equal("query_too_complex", failure?.Code);
    }

    [Fact]
    public void StructuredPhraseRejectsSlopAboveCoreMaximumDuringValidation()
    {
        PhraseQueryDefinition definition = new("title", ["guide"], 257);

        bool translated = ServerQueryTranslator.TryTranslate(
            definition,
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.False(translated);
        Assert.Null(query);
        Assert.Equal("invalid_query", failure?.Code);
    }

    private static void AssertQueryStringRejected(string text, ServerCoreOptions options)
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition(text),
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

    [Theory]
    [InlineData("gov.*ment")]
    [InlineData("^gov.*ment$")]
    [InlineData(".*ment.*")]
    public void RegexpDefinitionAndQueryStringTranslateToTheSameWholeTermRegexp(string pattern)
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new RegexpQueryDefinition("title", pattern),
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var definitionQuery,
            out var failure);
        var queryStringQuery = TranslateQueryString($"title:/{pattern}/");

        Assert.True(translated, failure?.Message);
        var fromDefinition = Assert.IsType<Rowles.LeanCorpus.Search.Queries.RegexpQuery>(definitionQuery);
        var fromQueryString = Assert.IsType<Rowles.LeanCorpus.Search.Queries.RegexpQuery>(queryStringQuery);
        Assert.Equal("title", fromDefinition.Field);
        Assert.Equal(pattern, fromDefinition.Pattern);
        Assert.Equal(pattern, fromQueryString.Pattern);
    }

    [Fact]
    public void RegexpDefinitionWithAnUnbalancedPatternIsRejected()
    {
        bool translated;
        try
        {
            translated = ServerQueryTranslator.TryTranslate(
                new RegexpQueryDefinition("title", "a)|(b"),
                CreateSchema(),
                new ServerCoreOptions(),
                defaultField: "title",
                maximumBooleanClauses: null,
                out _,
                out _);
        }
        catch (ArgumentException)
        {
            translated = false;
        }

        Assert.False(translated);
    }

    private static Rowles.LeanCorpus.Search.Query TranslateQueryString(string text)
    {
        bool translated = ServerQueryTranslator.TryTranslate(
            new QueryStringDefinition(text),
            CreateSchema(),
            new ServerCoreOptions(),
            defaultField: "title",
            maximumBooleanClauses: null,
            out var query,
            out var failure);

        Assert.True(translated, failure?.Message);
        Assert.Null(failure);
        return Assert.IsAssignableFrom<Rowles.LeanCorpus.Search.Query>(query);
    }

    [Theory]
    [InlineData("wildcard", false)]
    [InlineData("wildcard", true)]
    [InlineData("regexp", false)]
    [InlineData("regexp", true)]
    public void PatternAdmissionIsSeparateFromExecutionLimits(string kind, bool queryString)
    {
        var options = new ServerCoreOptions
        {
            MaximumWildcardPatternChars = 4,
            MaximumRegexpPatternChars = 4,
            MaximumWildcardExpansions = 8,
            MaximumRegexpExpansions = 8
        };
        foreach (string pattern in new[] { "*", "aaaaa*" })
        {
            string actual = kind == "regexp" ? pattern.Replace('*', '.') : pattern;
            QueryDefinition definition = queryString
                ? new QueryStringDefinition(kind == "regexp" ? $"/{actual}/" : actual)
                : kind == "regexp" ? new RegexpQueryDefinition("title", actual) : new WildcardQueryDefinition("title", actual);
            bool translated = ServerQueryTranslator.TryTranslate(definition, CreateSchema(), options, "title", null, out var query, out var failure);
            Assert.Equal(actual.Length <= 4, translated);
            if (!translated) Assert.Equal("query_too_complex", failure?.Code);
            else
            {
                int? limit = query switch
                {
                    Rowles.LeanCorpus.Search.Queries.WildcardQuery wildcard => wildcard.MaximumExpansions,
                    Rowles.LeanCorpus.Search.Queries.PrefixQuery prefix => prefix.MaximumExpansions,
                    Rowles.LeanCorpus.Search.Queries.RegexpQuery regexp => regexp.MaximumExpansions,
                    _ => null
                };
                Assert.Equal(8, limit);
            }
        }
    }

    [Theory]
    [InlineData("MaximumWildcardPatternChars")]
    [InlineData("MaximumRegexpPatternChars")]
    [InlineData("MaximumWildcardExpansions")]
    [InlineData("MaximumRegexpExpansions")]
    public void AllPatternAndExpansionLimitsMustBePositive(string property)
    {
        foreach (int value in new[] { 0, -1 })
        {
            var options = new ServerCoreOptions { DataRoot = "unused" };
            typeof(ServerCoreOptions).GetProperty(property)!.SetValue(options, value);
            Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        }
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
