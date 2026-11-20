using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;
using System.Text;
using static Rowles.LeanCorpus.Tests.Core.Search.QueryCompositionCorpus;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Issue #121: every executable query must keep its standalone matches when used as a Boolean
/// clause, and queries that cannot be composed must fail loudly instead of matching nothing.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class BooleanQueryCompositionTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const float ScoreTolerance = 1e-4f;

    private enum CompositionPolicy
    {
        Nested,
        TopLevelOnly,
    }

    // Seeded from the 53-type inventory reviewed on 20 November 2026. A new public query type
    // must be classified here, so an unclassified type fails rather than silently passing.
    private static readonly Dictionary<string, CompositionPolicy> Policies = new(StringComparer.Ordinal)
    {
        ["BinaryPointInSetQuery"] = CompositionPolicy.Nested,
        ["BinaryRangeQuery"] = CompositionPolicy.Nested,
        ["BlockJoinQuery"] = CompositionPolicy.Nested,
        ["BooleanQuery"] = CompositionPolicy.Nested,
        ["CombinedFieldsQuery"] = CompositionPolicy.Nested,
        ["ConstantScoreQuery"] = CompositionPolicy.Nested,
        ["DisjunctionMaxQuery"] = CompositionPolicy.Nested,
        ["DrillDownQuery"] = CompositionPolicy.Nested,
        ["FieldExistsQuery"] = CompositionPolicy.Nested,
        ["FieldMaskingSpanQuery"] = CompositionPolicy.Nested,
        ["FunctionQuery"] = CompositionPolicy.Nested,
        ["FunctionScoreQuery"] = CompositionPolicy.Nested,
        ["FuzzyQuery"] = CompositionPolicy.Nested,
        ["GeoBoundingBoxQuery"] = CompositionPolicy.Nested,
        ["GeoDistanceQuery"] = CompositionPolicy.Nested,
        ["GeoShapeQuery"] = CompositionPolicy.Nested,
        ["InetAddressPointInSetQuery"] = CompositionPolicy.Nested,
        ["InetAddressRangeQuery"] = CompositionPolicy.Nested,
        ["Int32PointInSetQuery"] = CompositionPolicy.Nested,
        ["Int32RangeQuery"] = CompositionPolicy.Nested,
        ["Int64PointInSetQuery"] = CompositionPolicy.Nested,
        ["Int64RangeQuery"] = CompositionPolicy.Nested,
        ["IntervalsQuery"] = CompositionPolicy.Nested,
        ["MatchAllDocsQuery"] = CompositionPolicy.Nested,
        ["MatchNoDocsQuery"] = CompositionPolicy.Nested,
        ["MoreLikeThisQuery"] = CompositionPolicy.TopLevelOnly,
        ["MultiPhraseQuery"] = CompositionPolicy.Nested,
        ["PhraseQuery"] = CompositionPolicy.Nested,
        ["PointInSetQuery"] = CompositionPolicy.Nested,
        ["PrefixQuery"] = CompositionPolicy.Nested,
        ["RangeQuery"] = CompositionPolicy.Nested,
        ["RegexpQuery"] = CompositionPolicy.Nested,
        ["RrfQuery"] = CompositionPolicy.TopLevelOnly,
        ["SinglePointInSetQuery"] = CompositionPolicy.Nested,
        ["SingleRangeQuery"] = CompositionPolicy.Nested,
        ["SpanContainingQuery"] = CompositionPolicy.Nested,
        ["SpanFirstQuery"] = CompositionPolicy.Nested,
        ["SpanMultiTermQueryWrapper"] = CompositionPolicy.Nested,
        ["SpanNearQuery"] = CompositionPolicy.Nested,
        ["SpanNotQuery"] = CompositionPolicy.Nested,
        ["SpanOrQuery"] = CompositionPolicy.Nested,
        ["SpanTermQuery"] = CompositionPolicy.Nested,
        ["SpanWithinQuery"] = CompositionPolicy.Nested,
        ["SynonymQuery"] = CompositionPolicy.Nested,
        ["TermInSetQuery"] = CompositionPolicy.Nested,
        ["TermQuery"] = CompositionPolicy.Nested,
        ["TermRangeQuery"] = CompositionPolicy.Nested,
        ["TermsQuery"] = CompositionPolicy.Nested,
        ["VectorQuery"] = CompositionPolicy.Nested,
        ["WildcardQuery"] = CompositionPolicy.Nested,
        ["XYBoundingBoxQuery"] = CompositionPolicy.Nested,
        ["XYDistanceQuery"] = CompositionPolicy.Nested,
        ["XYShapeQuery"] = CompositionPolicy.Nested,
    };

    // Positive fixtures: each returns at least one live document on the shared corpus.
    private static readonly Dictionary<string, Func<Query>> Families = new(StringComparer.Ordinal)
    {
        ["Term"] = () => new TermQuery("title", "vegan"),
        ["Phrase"] = () => new PhraseQuery("title", "vegan", "bowl"),
        ["MultiPhrase"] = () => new MultiPhraseQuery("title", [["vegan"], ["bowl", "breakfast"]]),
        ["TermInSet"] = () => new TermInSetQuery("tag_id", "vegan", "fruehstueck"),
        ["Terms"] = () => new TermsQuery("tag_id", Encoding.UTF8.GetBytes("vegan")),
        ["PointInSet"] = () => new PointInSetQuery("number", 1d),
        ["Int64PointInSet"] = () => new Int64PointInSetQuery("integer", 1L),
        ["Int32PointInSetRewrite"] = () => new Int32PointInSetQuery("integer", 1),
        ["SinglePointInSetRewrite"] = () => new SinglePointInSetQuery("number", 1f),
        ["Intervals"] = () => new IntervalsQuery(new IntervalsTermSource("title", "vegan")),
        ["GeoBoundingBox"] = () => new GeoBoundingBoxQuery("geo", 0, 2, 0, 2),
        ["GeoDistance"] = () => new GeoDistanceQuery("geo", 1, 1, 1000),
        ["XYBoundingBox"] = () => new XYBoundingBoxQuery("xy", new XYRectangle(0, 0, 2, 2)),
        ["XYDistance"] = () => new XYDistanceQuery("xy", new XYPoint(1, 1), 1),
        ["FieldExists"] = () => new FieldExistsQuery("title"),
        ["CombinedFields"] = () => new CombinedFieldsQuery(["title"], ["vegan"]),
        ["SpanTerm"] = () => new SpanTermQuery("title", "vegan"),
        ["SpanNear"] = () => new SpanNearQuery([new SpanTermQuery("title", "breakfast"), new SpanTermQuery("title", "plate")], 0),
        ["SpanOr"] = () => new SpanOrQuery(new SpanTermQuery("title", "bowl"), new SpanTermQuery("title", "toast")),
        ["SpanNot"] = () => new SpanNotQuery(new SpanTermQuery("title", "plate"), new SpanTermQuery("title", "vegan")),
        ["SpanFirst"] = () => new SpanFirstQuery(new SpanTermQuery("title", "breakfast"), 1),
        ["SpanMultiTerm"] = () => new SpanMultiTermQueryWrapper(new PrefixQuery("title", "veg")),
        ["Range"] = () => new RangeQuery("number", 0, 2),
        ["Int64Range"] = () => new Int64RangeQuery("integer", 4, 6),
        ["Int32RangeRewrite"] = () => new Int32RangeQuery("integer", 0, 2),
        ["SingleRangeRewrite"] = () => new SingleRangeQuery("number", 8, 10),
        ["Prefix"] = () => new PrefixQuery("title", "breakf"),
        ["Wildcard"] = () => new WildcardQuery("title", "to*t"),
        ["Regexp"] = () => new RegexpQuery("title", "pl.te"),
        ["Fuzzy"] = () => new FuzzyQuery("title", "appel", maxEdits: 2),
        ["TermRange"] = () => new TermRangeQuery("id", "r1", "r2"),
        ["ConstantScore"] = () => new ConstantScoreQuery(new TermQuery("tag_id", "vegan"), 2f),
        ["DisjunctionMax"] = () => new DisjunctionMaxQuery().Add(new TermQuery("title", "toast")).Add(new TermQuery("title", "bowl")),
        ["Synonym"] = () => new SynonymQuery("title", "bowl", "toast"),
        ["FunctionScore"] = () => new FunctionScoreQuery(new TermQuery("title", "plate"), "number"),
        ["MatchAll"] = () => new MatchAllDocsQuery(),
        ["NestedBoolean"] = () => Bool((new TermQuery("type", "recipe"), Occur.Must), (new PhraseQuery("title", "breakfast", "plate"), Occur.Must)),
        ["BlockJoin"] = () => new BlockJoinQuery(new TermQuery("sku", "red")),
        ["BlockJoinBooleanChild"] = () => new BlockJoinQuery(Bool((new TermQuery("type", "child"), Occur.Must), (new TermQuery("sku", "green"), Occur.Must))),
        ["Vector"] = () => new VectorQuery("vec", [1f, 0f], topK: 10),
        ["VectorWithFilter"] = () => new VectorQuery("vec", [1f, 0f], topK: 10, filter: new TermQuery("title", "pear")),
        ["BoostedPhrase"] = () => new PhraseQuery("title", "breakfast", "plate") { Boost = 3f },
        ["RewriteToTerm"] = () => new RewritingQuery(new TermQuery("title", "toast")),
        ["WeightControl"] = () => new FunctionQuery(DoubleValuesSource.FromDoubleField("number")),
    };

    public static TheoryData<string, bool> FamilyCases()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in Families.Keys)
        {
            data.Add(name, false);
            data.Add(name, true);
        }
        return data;
    }

    [Fact]
    public void PublicQueryTypes_HaveExplicitCompositionPolicy()
    {
        var exported = typeof(Query).Assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsSubclassOf(typeof(Query)))
            .Select(type => type.Name)
            .ToArray();

        var unclassified = exported.Where(name => !Policies.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        Assert.True(unclassified.Length == 0,
            "Classify these public query types in BooleanQueryCompositionTests.Policies: " + string.Join(", ", unclassified));

        var removed = Policies.Keys.Except(exported, StringComparer.Ordinal).ToArray();
        Assert.True(removed.Length == 0, "Remove stale policy rows: " + string.Join(", ", removed));
    }

    [Theory]
    [MemberData(nameof(FamilyCases))]
    public void Clause_MustShouldMustNot_PreservesIdsAndScores(string family, bool multiSegment)
    {
        using var directory = new MMapDirectory(SubDir($"family-{family}-{multiSegment}"));
        Write(directory, multiSegment);
        using var searcher = new IndexSearcher(directory);
        if (multiSegment)
            Assert.True(searcher.GetSegmentReaders().Count > 1);

        Query query = Families[family]();
        var alone = Scores(searcher.Search(query, 1_000));
        Assert.True(alone.Count > 0, $"{family} fixture must match at least one document on its own.");

        var asMust = Scores(searcher.Search(Bool((query, Occur.Must)), 1_000));
        var asShould = Scores(searcher.Search(Bool((query, Occur.Should)), 1_000));
        AssertSameScores(alone, asMust, $"{family} as Must");
        AssertSameScores(alone, asShould, $"{family} as Should");

        var live = searcher.Search(new MatchAllDocsQuery(), 1_000).ScoreDocs.Select(static doc => doc.DocId).ToHashSet();
        var expectedComplement = live.Except(alone.Keys).Order().ToArray();
        var complement = searcher.Search(Bool((new MatchAllDocsQuery(), Occur.Must), (query, Occur.MustNot)), 1_000)
            .ScoreDocs.Select(static doc => doc.DocId).Order().ToArray();
        Assert.Equal(expectedComplement, complement);

        // A boosted wrapper scales every clause score; summed scores are preserved.
        var boostedQuery = Bool((query, Occur.Must));
        boostedQuery.Boost = 2f;
        var boosted = Scores(searcher.Search(boostedQuery, 1_000));
        AssertSameScores(alone.ToDictionary(static pair => pair.Key, static pair => pair.Value * 2f), boosted, $"{family} boosted");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReporterProgramme_AllCasesPass(bool multiSegment)
    {
        using var directory = new MMapDirectory(SubDir($"reporter-{multiSegment}"));
        Write(directory, multiSegment);
        using var searcher = new IndexSearcher(directory);

        Query childRed = Bool((new TermQuery("type", "child"), Occur.Must), (new TermQuery("sku", "red"), Occur.Must));
        Query isParent = new TermQuery("type", "parent");
        Query isRecipe = new TermQuery("type", "recipe");
        Query tagSet = new TermInSetQuery("tag_id", "vegan", "fruehstueck");
        Query veganBowl = new PhraseQuery("title", "vegan", "bowl");

        // Positive controls from the original report.
        AssertIds(searcher, isParent, "p1", "p2");
        AssertIds(searcher, Bool((isParent, Occur.Must), (new TermQuery("title", "apple"), Occur.Must)), "p1");
        AssertIds(searcher, new BlockJoinQuery(childRed), "p1");
        AssertIds(searcher, new FuzzyQuery("title", "appel", maxEdits: 2), "p1");
        AssertIds(searcher, tagSet, "r1", "r2", "r3");
        AssertIds(searcher, veganBowl, "r1");

        AssertIds(searcher, Bool((new BlockJoinQuery(childRed), Occur.Must)), "p1");
        AssertIds(searcher, Bool((isParent, Occur.Must), (new BlockJoinQuery(childRed), Occur.Must)), "p1");
        AssertIds(searcher, Bool((new BlockJoinQuery(childRed), Occur.Should)), "p1");
        AssertIds(searcher, Bool((isParent, Occur.Must), (new BlockJoinQuery(childRed), Occur.MustNot)), "p2");
        AssertIds(searcher, Bool((new FuzzyQuery("title", "appel", maxEdits: 2), Occur.Must)), "p1");
        AssertIds(searcher, Bool((isParent, Occur.Must), (new FuzzyQuery("title", "appel", maxEdits: 2), Occur.Must)), "p1");
        AssertIds(searcher, Bool((new FuzzyQuery("title", "appel", maxEdits: 2), Occur.Should)), "p1");
        AssertIds(searcher, Bool((tagSet, Occur.Must)), "r1", "r2", "r3");
        AssertIds(searcher, Bool((isRecipe, Occur.Must), (tagSet, Occur.Must)), "r1", "r2", "r3");
        AssertIds(searcher, Bool((tagSet, Occur.Should)), "r1", "r2", "r3");
        AssertIds(searcher, Bool((isRecipe, Occur.Must), (tagSet, Occur.MustNot)), "r4");
        AssertIds(searcher, Bool((veganBowl, Occur.Must)), "r1");
        AssertIds(searcher, Bool((isRecipe, Occur.Must), (veganBowl, Occur.Must)), "r1");
        AssertIds(searcher, new VectorQuery("vec", [1f, 0f], 1, 0, 1, null), "p1");
        AssertIds(searcher, new VectorQuery("vec", [1f, 0f], 1, 0, 1, new TermQuery("title", "pear")), "p2");
        AssertIds(searcher, Bool((isParent, Occur.Must), (new VectorQuery("vec", [1f, 0f], 1, 0, 1, null), Occur.Must)), "p1");

        // The reporter's workaround: term clauses as Should inside a sub-Boolean.
        var sub = Bool((new TermQuery("tag_id", "vegan"), Occur.Should), (new TermQuery("tag_id", "fruehstueck"), Occur.Should));
        AssertIds(searcher, Bool((isRecipe, Occur.Must), (sub, Occur.Must)), "r1", "r2", "r3");
    }

    public static TheoryData<string> UnsupportedCases() =>
    [
        "NestedRrf",
        "NestedMoreLikeThis",
        "RrfAfterZeroHitMust",
        "RrfInsideConstantScore",
        "RrfInsideVectorFilter",
        "RrfInsideBlockJoinChild",
        "CustomUnknownClause",
    ];

    private static Query UnsupportedQuery(string name)
    {
        var rrf = new RrfQuery().Add(new TermQuery("title", "vegan"));
        return name switch
        {
            "NestedRrf" => Bool((rrf, Occur.Must)),
            "NestedMoreLikeThis" => Bool((new MoreLikeThisQuery(0, ["title"]), Occur.Should)),
            "RrfAfterZeroHitMust" => Bool((new TermQuery("title", "absent"), Occur.Must), (rrf, Occur.Must)),
            "RrfInsideConstantScore" => new ConstantScoreQuery(Bool((rrf, Occur.Should))),
            "RrfInsideVectorFilter" => new VectorQuery("vec", [1f, 0f], topK: 1, filter: rrf),
            "RrfInsideBlockJoinChild" => new BlockJoinQuery(Bool((rrf, Occur.Must))),
            "CustomUnknownClause" => Bool((new TermQuery("title", "absent"), Occur.Must), (new UnknownQuery(), Occur.Must)),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public void UnsupportedClause_ThrowsBeforeShortCircuit(string name)
    {
        using var directory = new MMapDirectory(SubDir($"unsupported-{name}"));
        Write(directory, multiSegment: true);
        using var searcher = new IndexSearcher(directory);
        Query query = UnsupportedQuery(name);

        Assert.Throws<NotSupportedException>(() => searcher.Search(query, 10));
        Assert.Throws<NotSupportedException>(() => searcher.Count(query));
        Assert.Throws<NotSupportedException>(() => searcher.Search(query, 10, CancellationToken.None));
        Assert.Throws<NotSupportedException>(() => searcher.Search(query, 10, new SearchOptions()));
        Assert.Throws<NotSupportedException>(() => searcher.SearchStreaming(query).ToList());
        Assert.Throws<NotSupportedException>(() => searcher.Search(query, 10, SortField.DocId));
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public void UnsupportedClause_ThrowsOnEmptyIndex(string name)
    {
        using var directory = new MMapDirectory(SubDir($"unsupported-empty-{name}"));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            writer.Commit();
        using var searcher = new IndexSearcher(directory);
        Query query = UnsupportedQuery(name);

        Assert.Throws<NotSupportedException>(() => searcher.Search(query, 10));
        Assert.Throws<NotSupportedException>(() => searcher.Count(query));
    }

    [Fact]
    public void TopLevelWholeIndexQueries_RemainSupported()
    {
        using var directory = new MMapDirectory(SubDir("top-level"));
        Write(directory, multiSegment: true);
        using var searcher = new IndexSearcher(directory);

        var rrf = new RrfQuery()
            .Add(new TermQuery("title", "vegan"))
            .Add(Bool((new PhraseQuery("title", "breakfast", "plate"), Occur.Must)));
        Assert.Equal(["r1", "r2", "r3"], Ids(searcher, searcher.Search(rrf, 10)));

        int vegan = searcher.Search(new TermQuery("id", "r1"), 1).ScoreDocs[0].DocId;
        var mlt = new MoreLikeThisQuery(vegan, ["title"], new MoreLikeThisParameters { MinTermFreq = 1, MinDocFreq = 1 });
        searcher.Search(mlt, 10);

        Assert.Throws<NotSupportedException>(() => searcher.Search(new UnknownQuery(), 10));
    }

    private static void AssertIds(IndexSearcher searcher, Query query, params string[] expected)
        => Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), Ids(searcher, searcher.Search(query, 100)));

    private static Dictionary<int, float> Scores(TopDocs docs)
        => docs.ScoreDocs.ToDictionary(static doc => doc.DocId, static doc => doc.Score);

    private static void AssertSameScores(Dictionary<int, float> expected, Dictionary<int, float> actual, string label)
    {
        Assert.True(expected.Keys.Order().SequenceEqual(actual.Keys.Order()),
            $"{label}: expected [{string.Join(',', expected.Keys.Order())}] but got [{string.Join(',', actual.Keys.Order())}]");
        foreach (var (docId, score) in expected)
        {
            float tolerance = ScoreTolerance * Math.Max(1f, Math.Abs(score));
            Assert.True(Math.Abs(score - actual[docId]) <= tolerance,
                $"{label}: doc {docId} expected score {score} but got {actual[docId]}");
        }
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A custom query with neither a rewrite nor a weight: not executable.</summary>
    private sealed class UnknownQuery : Query
    {
        public override string Field => "title";
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    /// <summary>A custom query that lowers itself to a built-in query.</summary>
    private sealed class RewritingQuery(Query target) : Query
    {
        public override string Field => target.Field;
        public override Query Rewrite() => target;
        public override bool Equals(object? obj) => obj is RewritingQuery other && other.Rewrite().Equals(target);
        public override int GetHashCode() => target.GetHashCode();
    }
}
