using Xunit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

/// <summary>
/// Native AOT coverage for issue #121: nested clause dispatch, block-join child Booleans, the global
/// vector TopK cap and loud rejection of unsupported composition.
/// </summary>
public class QueryCompositionSmokeTests : IClassFixture<IndexSmokeFixture>
{
    private readonly IndexSmokeFixture _fixture;

    public QueryCompositionSmokeTests(IndexSmokeFixture fixture) => _fixture = fixture;

    [Fact]
    public void NestedClauses_KeepTheirMatches()
    {
        using var directory = Open("nested");
        using var searcher = new IndexSearcher(directory);

        var phrase = new BooleanQuery.Builder()
            .Add(new TermQuery("type", "recipe"), Occur.Must)
            .Add(new PhraseQuery("title", "vegan", "bowl"), Occur.Must)
            .Build();
        Assert.Equal(["r1"], Ids(searcher, searcher.Search(phrase, 10)));

        var terms = new BooleanQuery.Builder()
            .Add(new TermInSetQuery("tag", "vegan"), Occur.Must)
            .Build();
        Assert.Equal(["r1", "r2"], Ids(searcher, searcher.Search(terms, 10)));

        var childBoolean = new BooleanQuery.Builder()
            .Add(new TermQuery("sku", "red"), Occur.Must)
            .Add(new TermQuery("sku", "green"), Occur.Should)
            .Build();
        var blockJoin = new BooleanQuery.Builder()
            .Add(new TermQuery("type", "parent"), Occur.Must)
            .Add(new BlockJoinQuery(childBoolean), Occur.Must)
            .Build();
        Assert.Equal(["p1"], Ids(searcher, searcher.Search(blockJoin, 10)));
    }

    [Fact]
    public void VectorTopK_IsAGlobalCap()
    {
        using var directory = Open("vector");
        using var searcher = new IndexSearcher(directory);

        var nearest = new VectorQuery("vec", [1f, 0f], topK: 1);
        var top = searcher.Search(nearest, 100);
        Assert.Equal(1, top.TotalHits);
        Assert.Equal(["p1"], Ids(searcher, top));
        Assert.Equal(1, searcher.Count(nearest));

        var withSibling = new BooleanQuery.Builder()
            .Add(nearest, Occur.Must)
            .Add(new TermQuery("title", "pear"), Occur.Must)
            .Build();
        Assert.Empty(searcher.Search(withSibling, 100).ScoreDocs);
        Assert.Equal(["p2"], Ids(searcher, searcher.Search(
            new VectorQuery("vec", [1f, 0f], topK: 1, filter: new TermQuery("title", "pear")), 100)));
    }

    [Fact]
    public void UnsupportedNesting_Throws()
    {
        using var directory = Open("unsupported");
        using var searcher = new IndexSearcher(directory);

        var nestedRrf = new BooleanQuery.Builder()
            .Add(new TermQuery("title", "absent"), Occur.Must)
            .Add(new RrfQuery().Add(new TermQuery("title", "vegan")), Occur.Must)
            .Build();
        Assert.Throws<NotSupportedException>(() => searcher.Search(nestedRrf, 10));
    }

    private MMapDirectory Open(string name)
    {
        string path = Path.Combine(_fixture.RootPath, "composition-" + name);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        writer.AddDocumentBlock([Child("red"), Child("blue"), Parent("p1", "red apple", [1f, 0f])]);
        writer.Commit();
        writer.AddDocumentBlock([Child("green"), Parent("p2", "green pear", [0f, 1f])]);
        writer.AddDocument(Recipe("r1", "vegan bowl", "vegan"));
        writer.AddDocument(Recipe("r2", "vegan breakfast plate", "vegan"));
        writer.AddDocument(Recipe("r3", "plain toast", "plain"));
        writer.Commit();
        return directory;
    }

    private static string[] Ids(IndexSearcher searcher, TopDocs docs)
        => docs.ScoreDocs
            .Select(doc => searcher.GetStoredFields(doc.DocId).TryGetValue("id", out var values) ? values[0] : "?")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static LeanDocument Child(string sku)
    {
        var document = new LeanDocument();
        document.Add(new StringField("type", "child"));
        document.Add(new StringField("sku", sku));
        return document;
    }

    private static LeanDocument Parent(string id, string title, float[] vector)
    {
        var document = new LeanDocument();
        document.Add(new StringField("type", "parent"));
        document.Add(new StringField("id", id, stored: true));
        document.Add(new TextField("title", title, stored: true));
        document.Add(new VectorField("vec", vector));
        return document;
    }

    private static LeanDocument Recipe(string id, string title, string tag)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new StringField("type", "recipe"));
        document.Add(new TextField("title", title, stored: true));
        document.Add(new StringField("tag", tag));
        return document;
    }
}
