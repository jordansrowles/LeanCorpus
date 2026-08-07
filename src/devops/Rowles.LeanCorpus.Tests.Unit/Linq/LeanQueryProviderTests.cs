using System.Linq.Expressions;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Linq;

[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class LeanQueryProviderTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public LeanQueryProviderTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "LeanQueryProvider: creates document and projected queryables")]
    public void CreateQuery_PreservesProviderAndElementTypes()
    {
        using var context = BuildContext(nameof(CreateQuery_PreservesProviderAndElementTypes));
        var provider = Assert.IsType<LeanQueryProvider<Article>>(context.Queryable.Provider);

        var documentQuery = provider.CreateQuery<Article>(context.Queryable.Expression);
        var projectedExpression = context.Queryable.Select(article => article.Year).Expression;
        var projectedQuery = provider.CreateQuery<int>(projectedExpression);
        var nonGenericQuery = provider.CreateQuery(context.Queryable.Expression);

        Assert.IsType<LeanQueryable<Article>>(documentQuery);
        Assert.Equal(typeof(Article), documentQuery.ElementType);
        Assert.Same(provider, documentQuery.Provider);
        Assert.Equal(typeof(int), projectedQuery.ElementType);
        Assert.Same(provider, projectedQuery.Provider);
        Assert.Equal(typeof(Article), nonGenericQuery.ElementType);
    }

    [Fact(DisplayName = "LeanQueryProvider: executes the root expression through both provider overloads")]
    public void Execute_RootExpressionMaterialisesDocuments()
    {
        using var context = BuildContext(nameof(Execute_RootExpressionMaterialisesDocuments));
        var provider = Assert.IsType<LeanQueryProvider<Article>>(context.Queryable.Provider);

        var untyped = Assert.IsType<List<Article>>(provider.Execute(context.Queryable.Expression));
        var typed = provider.Execute<IEnumerable<Article>>(context.Queryable.Expression).ToList();

        Assert.Equal(10, untyped.Count);
        Assert.Equal(10, typed.Count);
        Assert.All(typed, article => Assert.NotNull(article.Title));
    }

    [Fact(DisplayName = "LeanQueryProvider: supports Count Any and LongCount predicates")]
    public void CountAnyAndLongCount_UseProviderExecution()
    {
        using var context = BuildContext(nameof(CountAnyAndLongCount_UseProviderExecution));
        var query = context.Queryable;

        Assert.Equal(10, query.Count());
        Assert.Equal(7, query.Count(article => article.Status == "active"));
        Assert.True(query.Any(article => article.Status == "draft"));
        Assert.False(query.Any(article => article.Status == "missing"));
        Assert.Equal(10L, query.LongCount());
    }

    [Fact(DisplayName = "LeanQueryProvider: executes projected Select Take and Skip terminals")]
    public void ProjectedTerminals_SelectTakeAndSkip_ReturnExpectedValues()
    {
        using var context = BuildContext(nameof(ProjectedTerminals_SelectTakeAndSkip_ReturnExpectedValues));
        var query = context.Queryable;

        var titles = query.Select(article => article.Title!).ToList();
        var firstYears = query.Select(article => article.Year).Take(2).ToList();
        var finalYears = query.Select(article => article.Year).Skip(8).ToList();

        Assert.Equal(10, titles.Count);
        Assert.All(titles, title => Assert.False(string.IsNullOrWhiteSpace(title)));
        Assert.Equal(2, firstYears.Count);
        Assert.Equal(2, finalYears.Count);
    }

    [Fact(DisplayName = "LeanQueryProvider: projected scalar terminals handle empty and non-empty sequences")]
    public void ProjectedScalarTerminals_ReturnExpectedValues()
    {
        using var context = BuildContext(nameof(ProjectedScalarTerminals_ReturnExpectedValues));
        var query = context.Queryable;

        var first = query.Where(article => article.Status == "active")
            .Select(article => article.Title!)
            .First();
        var firstOrDefault = query.Where(article => article.Status == "missing")
            .Select(article => article.Title!)
            .FirstOrDefault();
        var single = query.Where(article => article.Status == "archived")
            .Select(article => article.Year)
            .Single();
        var singleOrDefault = query.Where(article => article.Status == "missing")
            .Select(article => article.Year)
            .SingleOrDefault();
        var last = query.Where(article => article.Status == "archived")
            .Select(article => article.Title!)
            .Last();
        var lastOrDefault = query.Where(article => article.Status == "missing")
            .Select(article => article.Title!)
            .LastOrDefault();
        var elementAt = query.Where(article => article.Status == "active")
            .Select(article => article.Year)
            .ElementAt(1);
        var elementAtOrDefault = query.Where(article => article.Status == "missing")
            .Select(article => article.Year)
            .ElementAtOrDefault(1);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Null(firstOrDefault);
        Assert.Equal(2020, single);
        Assert.Equal(0, singleOrDefault);
        Assert.Equal("archived old manual", last);
        Assert.Null(lastOrDefault);
        Assert.InRange(elementAt, 2023, 2025);
        Assert.Equal(0, elementAtOrDefault);
    }

    [Fact(DisplayName = "LeanQueryProvider: executes root and typed projections as arrays")]
    public void ExecuteToArray_RootAndTypedProjection_ReturnArrays()
    {
        using var context = BuildContext(nameof(ExecuteToArray_RootAndTypedProjection_ReturnArrays));
        var provider = Assert.IsType<LeanQueryProvider<Article>>(context.Queryable.Provider);

        var rootArray = provider.Execute<Article[]>(CreateToArrayExpression<Article>(context.Queryable.Expression));
        var yearQuery = context.Queryable.Select(article => article.Year);
        var yearArray = provider.Execute<int[]>(CreateToArrayExpression<int>(yearQuery.Expression));

        Assert.Equal(10, rootArray.Length);
        Assert.Equal(10, yearArray.Length);
        Assert.All(rootArray, article => Assert.NotNull(article.Status));
        Assert.All(yearArray, year => Assert.InRange(year, 2020, 2025));
    }

    [Fact(DisplayName = "LeanQueryProvider: rejects unsupported projected array element types")]
    public void ExecuteToArray_UnsupportedProjection_Throws()
    {
        using var context = BuildContext(nameof(ExecuteToArray_UnsupportedProjection_Throws));
        var provider = Assert.IsType<LeanQueryProvider<Article>>(context.Queryable.Provider);
        var projection = context.Queryable.Select(article => new ArticleSummary(article.Title!, article.Year));

        var exception = Assert.Throws<NotSupportedException>(() =>
            provider.Execute<ArticleSummary[]>(CreateToArrayExpression<ArticleSummary>(projection.Expression)));

        Assert.Contains("AOT-compatible", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "LeanQueryProvider: applies descending ordering with search options")]
    public void OrderByDescending_WithSearchOptions_SortsMaterialisedDocuments()
    {
        using var context = BuildContext(
            nameof(OrderByDescending_WithSearchOptions_SortsMaterialisedDocuments),
            new SearchOptions { Timeout = TimeSpan.FromSeconds(30) });

        var results = context.Queryable
            .OrderByDescending(article => article.Year)
            .Take(5)
            .ToList();

        Assert.Equal(5, results.Count);
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Year >= results[i].Year);
    }

    [Fact(DisplayName = "LeanQueryProvider: resolves captured predicates and chained queryables")]
    public void CapturedPredicateAndQueryable_ProduceCombinedResults()
    {
        using var context = BuildContext(nameof(CapturedPredicateAndQueryable_ProduceCombinedResults));
        Expression<Func<Article, bool>> predicate = article => article.Status == "active";

        var capturedResults = context.Queryable.Where(predicate).ToList();
        var chainedResults = context.Queryable
            .Where(article => article.Status == "active")
            .Where(article => article.Year == 2025)
            .ToList();

        Assert.Equal(7, capturedResults.Count);
        Assert.NotEmpty(chainedResults);
        Assert.All(chainedResults, article =>
        {
            Assert.Equal("active", article.Status);
            Assert.Equal(2025, article.Year);
        });
    }

    [Fact(DisplayName = "LeanQueryProvider: rejects unsupported terminals and unmapped sort fields")]
    public void UnsupportedTerminalAndSortField_ThrowNotSupportedException()
    {
        using var context = BuildContext(nameof(UnsupportedTerminalAndSortField_ThrowNotSupportedException));

        var terminalException = Assert.Throws<NotSupportedException>(() =>
            context.Queryable.Sum(article => article.Year));
        var sortException = Assert.Throws<NotSupportedException>(() =>
            context.Queryable.OrderBy(article => article.Unmapped).ToList());

        Assert.Contains("Sum", terminalException.Message, StringComparison.Ordinal);
        Assert.Contains("Unmapped", sortException.Message, StringComparison.Ordinal);
    }

    private QueryContext BuildContext(string testName, SearchOptions? searchOptions = null)
    {
        var path = System.IO.Path.Combine(_fixture.Path, testName);
        System.IO.Directory.CreateDirectory(path);

        var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            IndexArticles(writer);
            writer.Commit();
        }

        var searcher = new IndexSearcher(directory);
        var queryable = new ArticleMap().AsQueryable(searcher, Resolver, searchOptions!);
        return new QueryContext(directory, searcher, queryable);
    }

    private static Expression CreateToArrayExpression<T>(Expression source)
    {
        return Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.ToArray),
            new[] { typeof(T) },
            source);
    }

    private static void IndexArticles(IndexWriter writer)
    {
        foreach (var (title, status, year) in new (string, string, int)[]
        {
            ("lean corpus search", "active", 2025),
            ("fast indexing guide", "active", 2024),
            ("native aot deployment", "active", 2025),
            ("archived old manual", "archived", 2020),
            ("benchmarking with bdn", "active", 2024),
            ("compression codecs", "draft", 2025),
            ("stored field roundtrip", "active", 2023),
            ("geo spatial queries", "active", 2024),
            ("hnsw vector search", "draft", 2025),
            ("linq query overview", "active", 2025),
        })
        {
            var document = new LeanDocument();
            document.Add(new TextField("title", title, stored: true));
            document.Add(new StringField("status", status, stored: true));
            document.Add(new NumericField("year", year, stored: true));
            writer.AddDocument(document);
        }
    }

    private static readonly IFieldDescriptor TitleField =
        new SimpleDescriptor("title", FieldType.Text, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor StatusField =
        new SimpleDescriptor("status", FieldType.String, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor YearField =
        new SimpleDescriptor("year", FieldType.Numeric, isStored: true, isIndexed: true, isRequired: true);

    private static readonly Func<string, IFieldDescriptor?> Resolver = name => name switch
    {
        "Title" => TitleField,
        "Status" => StatusField,
        "Year" => YearField,
        _ => null,
    };

    private sealed class QueryContext : IDisposable
    {
        public QueryContext(
            MMapDirectory directory,
            IndexSearcher searcher,
            LeanQueryable<Article> queryable)
        {
            Directory = directory;
            Searcher = searcher;
            Queryable = queryable;
        }

        public MMapDirectory Directory { get; }
        public IndexSearcher Searcher { get; }
        public LeanQueryable<Article> Queryable { get; }

        public void Dispose()
        {
            Searcher.Dispose();
            Directory.Dispose();
        }
    }

    private sealed class Article
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
        public int Year { get; set; }
        public string? Unmapped { get; set; }
    }

    private sealed record ArticleSummary(string Title, int Year);

    private sealed class ArticleMap : LeanDocumentMap<Article>
    {
        public override string DocumentName => "article";
        public override bool StrictSchema => true;
        public override IReadOnlyList<LeanFieldBinding<Article>> Fields { get; } = new[]
        {
            new LeanFieldBinding<Article>("title", FieldType.Text, isStored: true, isIndexed: true, isRequired: true),
            new LeanFieldBinding<Article>("status", FieldType.String, isStored: true, isIndexed: true, isRequired: true),
            new LeanFieldBinding<Article>("year", FieldType.Numeric, isStored: true, isIndexed: true, isRequired: true),
        };

        public override LeanDocument ToDocument(Article value) => throw new NotSupportedException();

        public override Article FromStoredDocument(StoredDocument document) => new()
        {
            Title = document.GetFirst("title"),
            Status = document.GetFirst("status"),
            Year = document.GetFirst("year") is { } value &&
                double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var year)
                ? (int)year
                : 0,
        };

        public override IndexSchema CreateSchema(bool strict)
        {
            var schema = new IndexSchema { StrictMode = strict };
            foreach (var field in Fields)
            {
                schema.Add(new FieldMapping(field.Name, field.FieldType)
                {
                    IsStored = field.IsStored,
                    IsIndexed = field.IsIndexed,
                    IsRequired = field.IsRequired,
                });
            }

            return schema;
        }
    }

    private sealed class SimpleDescriptor : IFieldDescriptor
    {
        public SimpleDescriptor(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
        {
            Name = name;
            FieldType = fieldType;
            IsStored = isStored;
            IsIndexed = isIndexed;
            IsRequired = isRequired;
        }

        public string Name { get; }
        public FieldType FieldType { get; }
        public bool IsStored { get; }
        public bool IsIndexed { get; }
        public bool IsRequired { get; }
    }
}
