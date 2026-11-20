using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;
using static Rowles.LeanCorpus.Tests.Core.Search.QueryCompositionCorpus;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Issue #121 connected defects: block-join child Booleans and the general Boolean fallback
/// must apply the same Must, Should, MustNot and minimum-Should rules.
/// </summary>
/// <remarks>
/// Corpus blocks: p1 has children {red} and {blue}; p2 has children {green} and {green, large}.
/// </remarks>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class BlockJoinBooleanSemanticsTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MustAndOptionalChild_DoesNotWidenParents(bool multiSegment)
    {
        using var index = Open($"must-optional-{multiSegment}", multiSegment);
        var searcher = index.Searcher;

        var mustRedShouldGreen = Bool((new TermQuery("sku", "red"), Occur.Must), (new TermQuery("sku", "green"), Occur.Should));
        AssertIds(searcher, new BlockJoinQuery(mustRedShouldGreen), "p1");

        // The same child query nested in a parent Boolean.
        AssertIds(searcher, Bool((new TermQuery("type", "parent"), Occur.Must), (new BlockJoinQuery(mustRedShouldGreen), Occur.Must)), "p1");

        var mustGreenMustNotLarge = Bool((new TermQuery("sku", "green"), Occur.Must), (new TermQuery("sku", "large"), Occur.MustNot));
        AssertIds(searcher, new BlockJoinQuery(mustGreenMustNotLarge), "p2");

        var mustRedMustLarge = Bool((new TermQuery("sku", "red"), Occur.Must), (new TermQuery("sku", "large"), Occur.Must));
        AssertIds(searcher, new BlockJoinQuery(mustRedMustLarge));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildMinimumShouldMatch_CountsChildrenNotParents(bool multiSegment)
    {
        using var index = Open($"child-minimum-{multiSegment}", multiSegment);
        var searcher = index.Searcher;

        // p1 has a red child and a blue child; no single child matches both.
        var redOrBlueMinimumTwo = new BooleanQuery.Builder()
            .Add(new TermQuery("sku", "red"), Occur.Should)
            .Add(new TermQuery("sku", "blue"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();
        AssertIds(searcher, new BlockJoinQuery(redOrBlueMinimumTwo));

        // One p2 child carries both green and large.
        var greenOrLargeMinimumTwo = new BooleanQuery.Builder()
            .Add(new TermQuery("sku", "green"), Occur.Should)
            .Add(new TermQuery("sku", "large"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();
        AssertIds(searcher, new BlockJoinQuery(greenOrLargeMinimumTwo), "p2");

        // Should-only children with no explicit minimum require one match.
        var redOrGreen = Bool((new TermQuery("sku", "red"), Occur.Should), (new TermQuery("sku", "green"), Occur.Should));
        AssertIds(searcher, new BlockJoinQuery(redOrGreen), "p1", "p2");

        // With Must present, an explicit minimum counts only Should clauses.
        var childMustMinimumOne = new BooleanQuery.Builder()
            .Add(new TermQuery("type", "child"), Occur.Must)
            .Add(new TermQuery("sku", "large"), Occur.Should)
            .Add(new TermQuery("sku", "absent"), Occur.Should)
            .SetMinimumNumberShouldMatch(1)
            .Build();
        AssertIds(searcher, new BlockJoinQuery(childMustMinimumOne), "p2");

        AssertIds(searcher, new BlockJoinQuery(Bool((new TermQuery("sku", "red"), Occur.MustNot))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BooleanFallback_MinimumShouldMatch_CountsOnlyShouldClauses(bool multiSegment)
    {
        using var index = Open($"fallback-minimum-{multiSegment}", multiSegment);
        var searcher = index.Searcher;
        var fuzzyApple = new FuzzyQuery("title", "appel", maxEdits: 2);
        var isParent = new TermQuery("type", "parent");

        var mustParentShouldFuzzy = new BooleanQuery.Builder()
            .Add(isParent, Occur.Must)
            .Add(fuzzyApple, Occur.Should)
            .SetMinimumNumberShouldMatch(1)
            .Build();
        var hits = searcher.Search(mustParentShouldFuzzy, 10);
        Assert.Equal(["p1"], Ids(searcher, hits));
        float parentScore = searcher.Search(isParent, 10).ScoreDocs.Single(doc => doc.DocId == hits.ScoreDocs[0].DocId).Score;
        float fuzzyScore = searcher.Search(fuzzyApple, 10).ScoreDocs.Single().Score;
        Assert.Equal(parentScore + fuzzyScore, hits.ScoreDocs[0].Score, 4);

        // A second Must clause must not satisfy the Should minimum.
        var twoMustAbsentShould = new BooleanQuery.Builder()
            .Add(isParent, Occur.Must)
            .Add(new ConstantScoreQuery(new TermQuery("type", "parent")), Occur.Must)
            .Add(new TermQuery("title", "absent"), Occur.Should)
            .SetMinimumNumberShouldMatch(1)
            .Build();
        AssertIds(searcher, twoMustAbsentShould);

        // Without Must, a Should match is still required by default.
        var shouldOnly = Bool((fuzzyApple, Occur.Should), (new ConstantScoreQuery(new TermQuery("title", "pear")), Occur.Should));
        AssertIds(searcher, shouldOnly, "p1", "p2");

        // An explicit minimum of two counts only Should clauses.
        var shouldMinimumTwo = new BooleanQuery.Builder()
            .Add(new ConstantScoreQuery(new TermQuery("title", "vegan")), Occur.Should)
            .Add(new PhraseQuery("title", "breakfast", "plate"), Occur.Should)
            .Add(new TermQuery("title", "absent"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();
        AssertIds(searcher, shouldMinimumTwo, "r3");

        var mustRecipeMinimumTwo = new BooleanQuery.Builder()
            .Add(new TermQuery("type", "recipe"), Occur.Must)
            .Add(new ConstantScoreQuery(new TermQuery("title", "vegan")), Occur.Should)
            .Add(new PhraseQuery("title", "breakfast", "plate"), Occur.Should)
            .SetMinimumNumberShouldMatch(2)
            .Build();
        AssertIds(searcher, mustRecipeMinimumTwo, "r3");

        // Nested, and with MustNot: the inner minimum is still honoured.
        AssertIds(searcher, Bool((mustParentShouldFuzzy, Occur.Must)), "p1");
        AssertIds(searcher, Bool((mustRecipeMinimumTwo, Occur.Must), (new TermQuery("tag_id", "vegan"), Occur.MustNot)));

        // Pure-negative Booleans match nothing, as before.
        AssertIds(searcher, Bool((new ConstantScoreQuery(new TermQuery("type", "child")), Occur.MustNot)));
    }

    [Fact]
    public void Deletions_OnlyLiveChildrenMatchAndOnlyLiveParentsReturn()
    {
        string path = SubDir("deletions");
        using (var directory = new MMapDirectory(path))
        {
            using var writer = new IndexWriter(directory, new IndexWriterConfig());
            writer.AddDocumentBlock([Child("red"), Child("blue"), Parent("p1", "red apple", [1f, 0f])]);
            writer.AddDocumentBlock([Child("red", "large"), Child("green"), Parent("p2", "green pear", [0f, 1f])]);
            writer.AddDocumentBlock([Child("red"), Parent("p3", "red cherry", [1f, 1f])]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("sku", "large"));
            writer.DeleteDocuments(new TermQuery("id", "p3"));
            writer.Commit();
        }

        using var dir = new MMapDirectory(path);
        using var searcher = new IndexSearcher(dir);
        var redMustShouldBlue = Bool((new TermQuery("sku", "red"), Occur.Must), (new TermQuery("sku", "blue"), Occur.Should));
        AssertIds(searcher, new BlockJoinQuery(redMustShouldBlue), "p1");
        AssertIds(searcher, Bool((new BlockJoinQuery(new TermQuery("sku", "red")), Occur.Should)), "p1");
        AssertIds(searcher, Bool((new TermQuery("type", "parent"), Occur.Must), (new BlockJoinQuery(new TermQuery("sku", "red")), Occur.MustNot)), "p2");
    }

    private OpenIndex Open(string name, bool multiSegment)
    {
        var directory = new MMapDirectory(SubDir(name));
        Write(directory, multiSegment);
        return new OpenIndex(directory);
    }

    private static void AssertIds(IndexSearcher searcher, Query query, params string[] expected)
        => Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), Ids(searcher, searcher.Search(query, 100)));

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class OpenIndex(MMapDirectory directory) : IDisposable
    {
        public IndexSearcher Searcher { get; } = new(directory);

        public void Dispose()
        {
            Searcher.Dispose();
            directory.Dispose();
        }
    }
}
