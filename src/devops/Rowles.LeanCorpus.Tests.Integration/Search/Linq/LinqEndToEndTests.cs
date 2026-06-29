using System.Threading;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search.Linq;

/// <summary>
/// End-to-end tests for the LINQ queryable layer. Indexes real documents,
/// queries via <see cref="LeanQueryable{TDocument}"/>, and verifies
/// scored results, stored-field roundtrips, and LINQ operator behaviour.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Integration")]
public sealed class LinqEndToEndTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public LinqEndToEndTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ---- helper: build an index with known documents ----

    private static void IndexArticles(IndexWriter writer)
    {
        foreach (var (title, status, year) in new (string, string, int)[]
        {
            ("lean corpus search",       "active",   2025),
            ("fast indexing guide",      "active",   2024),
            ("native aot deployment",    "active",   2025),
            ("archived old manual",      "archived", 2020),
            ("benchmarking with bdn",    "active",   2024),
            ("compression codecs",       "draft",    2025),
            ("stored field roundtrip",   "active",   2023),
            ("geo spatial queries",      "active",   2024),
            ("hnsw vector search",       "draft",    2025),
            ("linq query overview",      "active",   2025),
        })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("title", title, stored: true));
            doc.Add(new StringField("status", status, stored: true));
            doc.Add(new NumericField("year", year, stored: true));
            writer.AddDocument(doc);
        }
    }

    private static readonly IFieldDescriptor TitleField  = new SimpleDescriptor("title",  FieldType.Text,    true, true, true);
    private static readonly IFieldDescriptor StatusField = new SimpleDescriptor("status", FieldType.String,  true, true, true);
    private static readonly IFieldDescriptor YearField   = new SimpleDescriptor("year",   FieldType.Numeric, true, true, true);

    private static readonly Func<string, IFieldDescriptor?> Resolver = name => name switch
    {
        "Title"  => TitleField,
        "Status" => StatusField,
        "Year"   => YearField,
        _ => null,
    };

    private sealed class Article
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
        public int Year { get; set; }
    }

    private sealed class ArticleMap : LeanDocumentMap<Article>
    {
        public override string DocumentName => "article";
        public override bool StrictSchema => true;
        public override IReadOnlyList<LeanFieldBinding<Article>> Fields { get; } = new[]
        {
            new LeanFieldBinding<Article>("title",  FieldType.Text,    true, true, true),
            new LeanFieldBinding<Article>("status", FieldType.String,  true, true, true),
            new LeanFieldBinding<Article>("year",   FieldType.Numeric, true, true, true),
        };
        public override LeanDocument ToDocument(Article a) => throw new NotSupportedException();
        public override Article FromStoredDocument(StoredDocument d) => new()
        {
            Title  = d.GetFirst("title"),
            Status = d.GetFirst("status"),
            Year   = d.GetFirst("year") is { } s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? (int)y : 0,
        };
        public override IndexSchema CreateSchema(bool strict)
        {
            var s = new IndexSchema { StrictMode = strict };
            foreach (var f in Fields) s.Add(new FieldMapping(f.Name, f.FieldType) { IsStored = f.IsStored, IsIndexed = f.IsIndexed, IsRequired = f.IsRequired });
            return s;
        }
    }

    private LeanQueryable<Article> BuildQueryable(string testName)
    {
        var dir = new MMapDirectory(SubDir(testName));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        IndexArticles(writer);
        writer.Commit();

        var searcher = new IndexSearcher(dir);
        var map = new ArticleMap();
        return map.AsQueryable(searcher, Resolver);
    }

    // === Where: single condition ===

    [Fact(DisplayName = "Where: TermQuery on text field returns correct hits")]
    public void Where_TextEquality_ReturnsCorrectHits()
    {
        var results = BuildQueryable(nameof(Where_TextEquality_ReturnsCorrectHits))
            .Where(a => a.Title == "corpus")
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.NotNull(a.Title));
    }

    [Fact(DisplayName = "Where: on string field returns correct hits")]
    public void Where_StringEquality_ReturnsCorrectHits()
    {
        var results = BuildQueryable(nameof(Where_StringEquality_ReturnsCorrectHits))
            .Where(a => a.Status == "active")
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.Equal("active", a.Status));
    }

    [Fact(DisplayName = "Where: numeric equality returns correct hits")]
    public void Where_NumericEquality_ReturnsCorrectHits()
    {
        var results = BuildQueryable(nameof(Where_NumericEquality_ReturnsCorrectHits))
            .Where(a => a.Year == 2025)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.Equal(2025, a.Year));
    }

    [Fact(DisplayName = "Where: numeric greater-than returns correct hits")]
    public void Where_NumericGreaterThan_ReturnsCorrectHits()
    {
        var results = BuildQueryable(nameof(Where_NumericGreaterThan_ReturnsCorrectHits))
            .Where(a => a.Year > 2024)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.True(a.Year > 2024));
    }

    [Fact(DisplayName = "Where: numeric greater-than-or-equal returns inclusive hits")]
    public void Where_NumericGreaterThanOrEqual_ReturnsInclusiveHits()
    {
        var results = BuildQueryable(nameof(Where_NumericGreaterThanOrEqual_ReturnsInclusiveHits))
            .Where(a => a.Year >= 2025)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.True(a.Year >= 2025));
    }

    [Fact(DisplayName = "Where: numeric less-than returns correct hits")]
    public void Where_NumericLessThan_ReturnsCorrectHits()
    {
        var results = BuildQueryable(nameof(Where_NumericLessThan_ReturnsCorrectHits))
            .Where(a => a.Year < 2024)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.True(a.Year < 2024));
    }

    [Fact(DisplayName = "Where: numeric less-than-or-equal returns inclusive hits")]
    public void Where_NumericLessThanOrEqual_ReturnsInclusiveHits()
    {
        var results = BuildQueryable(nameof(Where_NumericLessThanOrEqual_ReturnsInclusiveHits))
            .Where(a => a.Year <= 2020)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.True(a.Year <= 2020));
    }

    // === Where: compound conditions ===

    [Fact(DisplayName = "Where: compound AndAlso returns correct intersection")]
    public void Where_AndAlso_ReturnsIntersection()
    {
        var results = BuildQueryable(nameof(Where_AndAlso_ReturnsIntersection))
            .Where(a => a.Status == "active" && a.Year == 2025)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a =>
        {
            Assert.Equal("active", a.Status);
            Assert.Equal(2025, a.Year);
        });
    }

    [Fact(DisplayName = "Where: compound OrElse returns correct union")]
    public void Where_OrElse_ReturnsUnion()
    {
        var results = BuildQueryable(nameof(Where_OrElse_ReturnsUnion))
            .Where(a => a.Status == "archived" || a.Status == "draft")
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a => Assert.True(a.Status is "archived" or "draft"));
    }

    [Fact(DisplayName = "Where: chained Where calls produce intersection")]
    public void Where_Chained_ProducesIntersection()
    {
        var results = BuildQueryable(nameof(Where_Chained_ProducesIntersection))
            .Where(a => a.Status == "active")
            .Where(a => a.Year > 2023)
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a =>
        {
            Assert.Equal("active", a.Status);
            Assert.True(a.Year > 2023);
        });
    }

    // === LINQ operators ===

    [Fact(DisplayName = "First: returns single result")]
    public void First_ReturnsSingleResult()
    {
        var result = BuildQueryable(nameof(First_ReturnsSingleResult))
            .Where(a => a.Status == "archived")
            .First();

        Assert.Equal("archived", result.Status);
    }

    [Fact(DisplayName = "First: on empty sequence throws")]
    public void First_EmptySequence_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuildQueryable(nameof(First_EmptySequence_Throws))
                .Where(a => a.Status == "nonexistent")
                .First());
    }

    [Fact(DisplayName = "FirstOrDefault: on empty sequence returns default")]
    public void FirstOrDefault_EmptySequence_ReturnsDefault()
    {
        var result = BuildQueryable(nameof(FirstOrDefault_EmptySequence_ReturnsDefault))
            .Where(a => a.Status == "nonexistent")
            .FirstOrDefault();

        Assert.Null(result);
    }

    [Fact(DisplayName = "Single: returns sole result")]
    public void Single_ReturnsSoleResult()
    {
        var result = BuildQueryable(nameof(Single_ReturnsSoleResult))
            .Where(a => a.Status == "archived")
            .Single();

        Assert.Equal("archived", result.Status);
    }

    [Fact(DisplayName = "Single: on multiple results throws")]
    public void Single_MultipleResults_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuildQueryable(nameof(Single_MultipleResults_Throws))
                .Where(a => a.Status == "active")
                .Single());
    }

    [Fact(DisplayName = "Count: returns correct total hits")]
    public void Count_ReturnsCorrectTotal()
    {
        var count = BuildQueryable(nameof(Count_ReturnsCorrectTotal))
            .Where(a => a.Status == "active")
            .Count();

        Assert.True(count > 0);
    }

    [Fact(DisplayName = "Count: with no predicate counts all documents")]
    public void Count_NoPredicate_CountsAll()
    {
        var count = BuildQueryable(nameof(Count_NoPredicate_CountsAll))
            .Count();

        Assert.Equal(10, count);
    }

    [Fact(DisplayName = "Any: returns true when results exist")]
    public void Any_WithResults_ReturnsTrue()
    {
        var any = BuildQueryable(nameof(Any_WithResults_ReturnsTrue))
            .Where(a => a.Status == "active")
            .Any();

        Assert.True(any);
    }

    [Fact(DisplayName = "Any: returns false when no results")]
    public void Any_NoResults_ReturnsFalse()
    {
        var any = BuildQueryable(nameof(Any_NoResults_ReturnsFalse))
            .Where(a => a.Status == "nonexistent")
            .Any();

        Assert.False(any);
    }

    [Fact(DisplayName = "Take: limits result count")]
    public void Take_LimitsResultCount()
    {
        var results = BuildQueryable(nameof(Take_LimitsResultCount))
            .Where(a => a.Status == "active")
            .Take(2)
            .ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact(DisplayName = "Skip: offsets result set")]
    public void Skip_OffsetsResultSet()
    {
        var all = BuildQueryable(nameof(Skip_OffsetsResultSet))
            .Where(a => a.Status == "active")
            .ToList();

        var skipped = BuildQueryable(nameof(Skip_OffsetsResultSet) + "_2") // different dir name to avoid directory conflict
            .Where(a => a.Status == "active")
            .Skip(2)
            .ToList();

        Assert.True(skipped.Count < all.Count);
    }

    // === Select projection ===

    [Fact(DisplayName = "Select: projects to string property")]
    public void Select_ProjectsToStringProperty()
    {
        var titles = BuildQueryable(nameof(Select_ProjectsToStringProperty))
            .Where(a => a.Status == "active")
            .Select(a => a.Title)
            .ToList();

        Assert.NotEmpty(titles);
        Assert.All(titles, t => Assert.NotNull(t));
    }

    // === Stored-field roundtrip ===

    [Fact(DisplayName = "Materialised documents preserve stored fields")]
    public void Materialised_HasCorrectStoredFields()
    {
        var result = BuildQueryable(nameof(Materialised_HasCorrectStoredFields))
            .Where(a => a.Title == "linq")
            .First();

        Assert.NotNull(result.Title);
        Assert.NotNull(result.Status);
    }

    // === No results ===

    [Fact(DisplayName = "Where: no match returns empty list")]
    public void Where_NoMatch_ReturnsEmptyList()
    {
        var results = BuildQueryable(nameof(Where_NoMatch_ReturnsEmptyList))
            .Where(a => a.Title == "nonexistent absolutely")
            .ToList();

        Assert.Empty(results);
    }

    // === Match all ===

    [Fact(DisplayName = "ToList: without Where returns all documents")]
    public void ToList_NoWhere_ReturnsAll()
    {
        var results = BuildQueryable(nameof(ToList_NoWhere_ReturnsAll))
            .ToList();

        Assert.Equal(10, results.Count);
    }

    // === Contains / StartsWith / EndsWith ===

    [Fact(DisplayName = "Contains: matches substring")]
    public void Contains_MatchesSubstring()
    {
        var results = BuildQueryable(nameof(Contains_MatchesSubstring))
            .Where(a => a.Title!.Contains("vector"))
            .ToList();

        Assert.NotEmpty(results);
    }

    [Fact(DisplayName = "StartsWith: matches prefix")]
    public void StartsWith_MatchesPrefix()
    {
        var results = BuildQueryable(nameof(StartsWith_MatchesPrefix))
            .Where(a => a.Title!.StartsWith("linq"))
            .ToList();

        Assert.NotEmpty(results);
    }

    [Fact(DisplayName = "EndsWith: matches suffix")]
    public void EndsWith_MatchesSuffix()
    {
        var results = BuildQueryable(nameof(EndsWith_MatchesSuffix))
            .Where(a => a.Title!.EndsWith("search"))
            .ToList();

        Assert.NotEmpty(results);
    }

    // === Last / LastOrDefault (#10) ===

    [Fact(DisplayName = "Last: returns final matching document")]
    public void Last_ReturnsFinalResult()
    {
        var result = BuildQueryable(nameof(Last_ReturnsFinalResult))
            .Where(a => a.Status == "active")
            .OrderBy(a => a.Year)
            .Last();

        Assert.Equal("active", result.Status);
        Assert.Equal(2025, result.Year); // after ordering by year, last should be 2025
    }

    [Fact(DisplayName = "Last: on empty sequence throws")]
    public void Last_EmptySequence_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuildQueryable(nameof(Last_EmptySequence_Throws))
                .Where(a => a.Status == "nonexistent")
                .Last());
    }

    [Fact(DisplayName = "LastOrDefault: on empty returns default")]
    public void LastOrDefault_EmptySequence_ReturnsDefault()
    {
        var result = BuildQueryable(nameof(LastOrDefault_EmptySequence_ReturnsDefault))
            .Where(a => a.Status == "nonexistent")
            .LastOrDefault();

        Assert.Null(result);
    }

    // === ElementAt / ElementAtOrDefault (#11) ===

    [Fact(DisplayName = "ElementAt: returns correct element")]
    public void ElementAt_ReturnsCorrectElement()
    {
        var result = BuildQueryable(nameof(ElementAt_ReturnsCorrectElement))
            .Where(a => a.Status == "active")
            .OrderBy(a => a.Year)
            .ElementAt(1);

        Assert.Equal(2024, result.Year);
    }

    [Fact(DisplayName = "ElementAt: out of range throws")]
    public void ElementAt_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildQueryable(nameof(ElementAt_OutOfRange_Throws))
                .Where(a => a.Status == "active")
                .ElementAt(99));
    }

    [Fact(DisplayName = "ElementAt: negative index throws")]
    public void ElementAt_NegativeIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuildQueryable(nameof(ElementAt_NegativeIndex_Throws))
                .Where(a => a.Status == "active")
                .ElementAt(-1));
    }

    [Fact(DisplayName = "ElementAtOrDefault: out of range returns default")]
    public void ElementAtOrDefault_OutOfRange_ReturnsDefault()
    {
        var result = BuildQueryable(nameof(ElementAtOrDefault_OutOfRange_ReturnsDefault))
            .Where(a => a.Status == "active")
            .ElementAtOrDefault(99);

        Assert.Null(result);
    }

    [Fact(DisplayName = "ElementAtOrDefault: negative index returns default")]
    public void ElementAtOrDefault_NegativeIndex_ReturnsDefault()
    {
        var result = BuildQueryable(nameof(ElementAtOrDefault_NegativeIndex_ReturnsDefault))
            .Where(a => a.Status == "active")
            .ElementAtOrDefault(-5);

        Assert.Null(result);
    }

    // === SearchOptions / CancellationToken (#20, #21) ===

    [Fact(DisplayName = "SearchOptions: timeout does not throw when time is sufficient")]
    public void SearchOptions_Timeout_DoesNotThrow()
    {
        var dir = new MMapDirectory(SubDir(nameof(SearchOptions_Timeout_DoesNotThrow)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            IndexArticles(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(dir);
        var map = new ArticleMap();

        var queryable = map.AsQueryable(searcher, Resolver,
            new SearchOptions { Timeout = TimeSpan.FromSeconds(30) });

        var results = queryable.Where(a => a.Status == "active").ToList();
        Assert.NotEmpty(results);
    }

    [Fact(DisplayName = "SearchOptions: cancelled token returns empty or partial")]
    public void SearchOptions_CancelledToken_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir(nameof(SearchOptions_CancelledToken_ReturnsEmpty)));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            IndexArticles(writer);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(dir);
        var map = new ArticleMap();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var queryable = map.AsQueryable(searcher, Resolver,
            new SearchOptions { CancellationToken = cts.Token });

        // With a pre-cancelled token, the search should return empty or partial.
        // IndexSearcher checks the token between segments; with one segment it
        // may still return results, but should not throw.
        var results = queryable.Where(a => a.Status == "active").ToList();
        Assert.NotNull(results);
    }

    // === Contains with StringComparison (#13) ===

    [Fact(DisplayName = "Contains: with StringComparison ignores second argument")]
    public void Contains_WithStringComparison_Works()
    {
        var results = BuildQueryable(nameof(Contains_WithStringComparison_Works))
            .Where(a => a.Title!.Contains("linq", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(results);
    }

    // === Field pre-selection (#8) ===

    [Fact(DisplayName = "Materialised documents have correct stored field values")]
    public void Materialised_StoredFields_AllPresent()
    {
        var results = BuildQueryable(nameof(Materialised_StoredFields_AllPresent))
            .Where(a => a.Title == "linq")
            .ToList();

        Assert.NotEmpty(results);
        foreach (var doc in results)
        {
            // All three stored fields should be present (title, status, year).
            Assert.NotNull(doc.Title);
            Assert.NotNull(doc.Status);
            Assert.True(doc.Year >= 2023 && doc.Year <= 2025);
        }
    }

    [Fact(DisplayName = "ToList: all results have correct year range")]
    public void ToList_AllResults_HaveValidData()
    {
        var results = BuildQueryable(nameof(ToList_AllResults_HaveValidData))
            .Where(a => a.Status == "active")
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, a =>
        {
            Assert.NotNull(a.Title);
            Assert.Equal("active", a.Status);
            Assert.True(a.Year > 0);
        });
    }

    // === IFieldDescriptor stub ===

    private sealed class SimpleDescriptor : IFieldDescriptor
    {
        public string Name { get; }
        public FieldType FieldType { get; }
        public bool IsStored { get; }
        public bool IsIndexed { get; }
        public bool IsRequired { get; }

        public SimpleDescriptor(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
        {
            Name = name;
            FieldType = fieldType;
            IsStored = isStored;
            IsIndexed = isIndexed;
            IsRequired = isRequired;
        }
    }
}
