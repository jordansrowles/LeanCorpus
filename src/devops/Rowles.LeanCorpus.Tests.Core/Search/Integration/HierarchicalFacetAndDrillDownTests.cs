using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Integration coverage for hierarchical facets and drill-down queries.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class HierarchicalFacetAndDrillDownTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HierarchicalFacetAndDrillDownTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "FacetPath: Encoding Preserves Separator-Like Components")]
    public void FacetPath_EncodingPreservesSeparatorLikeComponents()
    {
        var path = new FacetPath(["Technology", "C# / .NET"]);

        Assert.Equal(["Technology", "C# / .NET"], path.Components);
        var indexedValues = path.ToIndexedValues();
        Assert.Equal(2, indexedValues.Count);
        Assert.All(indexedValues, value => Assert.True(FacetPathEncoder.IsEncodedPath(value)));
        Assert.True(FacetPathEncoder.TryGetImmediateChild(indexedValues[0], null, out string? rootChild));
        Assert.Equal("Technology", rootChild);
        Assert.True(FacetPathEncoder.TryGetImmediateChild(indexedValues[1], new FacetPath("Technology"), out string? child));
        Assert.Equal("C# / .NET", child);
    }

    [Fact(DisplayName = "Facets: Hierarchical Requests Return Immediate Children")]
    public void Facets_HierarchicalRequests_ReturnImmediateChildren()
    {
        var directoryPath = SubDir(nameof(Facets_HierarchicalRequests_ReturnImmediateChildren));
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            AddHierarchyDocument(writer, [new FacetPath("Technology", "Programming", "C#"), new FacetPath("Technology", "Programming", ".NET")]);
            AddHierarchyDocument(writer, [new FacetPath("Technology", "Programming", "Java")]);
            AddHierarchyDocument(writer, [new FacetPath("Technology", "Hardware", "CPU")]);
            AddHierarchyDocument(writer, [new FacetPath("Sports", "Football")]);
            AddHierarchyDocument(writer, []);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));

        var root = GetFacet(searcher, new HierarchicalFacetRequest("hierarchy", includeMissing: true));
        Assert.Equal(3, root.TotalBucketCount);
        Assert.Equal(3, BucketCount(root, "Technology"));
        Assert.Equal(1, BucketCount(root, "Sports"));
        Assert.Equal(1, root.Buckets.Single(bucket => bucket.IsMissing).Count);

        var technology = GetFacet(searcher, new HierarchicalFacetRequest(
            "hierarchy",
            new FacetPath("Technology"),
            order: FacetBucketOrder.ValueAscending,
            includeMissing: true));
        Assert.Equal(["Hardware", "Programming"], technology.Buckets.Where(bucket => !bucket.IsMissing).Select(bucket => bucket.Value));
        Assert.Equal(2, BucketCount(technology, "Programming"));
        Assert.Equal(1, BucketCount(technology, "Hardware"));
        Assert.Equal(1, technology.Buckets.Single(bucket => bucket.IsMissing).Count);

        var programming = GetFacet(searcher, new HierarchicalFacetRequest(
            "hierarchy",
            new FacetPath("Technology", "Programming")));
        Assert.Equal(3, programming.TotalBucketCount);
        Assert.Equal(1, BucketCount(programming, "C#"));
        Assert.Equal(1, BucketCount(programming, ".NET"));
        Assert.Equal(1, BucketCount(programming, "Java"));

        var programmingPage = GetFacet(searcher, new HierarchicalFacetRequest(
            "hierarchy",
            new FacetPath("Technology", "Programming"),
            order: FacetBucketOrder.ValueAscending,
            offset: 1,
            limit: 1));
        Assert.Equal(["C#"], programmingPage.Buckets.Select(bucket => bucket.Value));
    }

    [Fact(DisplayName = "DrillDownQuery: Combines Dimensions With And And Values With Or")]
    public void DrillDownQuery_CombineDimensionsWithAndAndValuesWithOr()
    {
        var directoryPath = SubDir(nameof(DrillDownQuery_CombineDimensionsWithAndAndValuesWithOr));
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            AddDrillDownDocument(writer, "one", "books", "en", new FacetPath("Technology", "Programming", "C#"));
            AddDrillDownDocument(writer, "two", "magazines", "en", new FacetPath("Technology", "Programming", "Java"));
            AddDrillDownDocument(writer, "three", "books", "fr", new FacetPath("Technology", "Design"));
            AddDrillDownDocument(writer, "four", "books", "en", null);
            AddDrillDownDocument(writer, "deleted", "books", "en", new FacetPath("Technology", "Programming", "C#"));
            writer.DeleteDocuments(new TermQuery("id", "deleted"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var baseQuery = new TermQuery("body", "phone");

        Assert.Equal(4, searcher.Search(baseQuery, 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(4, searcher.Search(new DrillDownQuery(baseQuery), 10, TestContext.Current.CancellationToken).TotalHits);

        var books = new DrillDownQuery(baseQuery, new DrillDownSelection("category", "books"));
        Assert.Equal(3, searcher.Search(books, 10, TestContext.Current.CancellationToken).TotalHits);

        var booksInEnglish = new DrillDownQuery(
            baseQuery,
            new DrillDownSelection("category", "books"),
            new DrillDownSelection("language", "en"));
        Assert.Equal(2, searcher.Search(booksInEnglish, 10, TestContext.Current.CancellationToken).TotalHits);

        var booksOrMagazines = new DrillDownQuery(
            baseQuery,
            new DrillDownSelection("category", "books"),
            new DrillDownSelection("category", "magazines"));
        Assert.Equal(4, searcher.Search(booksOrMagazines, 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(
            searcher.Search(booksOrMagazines, 10, TestContext.Current.CancellationToken).TotalHits,
            searcher.Search(new DrillDownQuery(
                baseQuery,
                new DrillDownSelection("category", "books"),
                new DrillDownSelection("category", "books"),
                new DrillDownSelection("category", "magazines")), 10, TestContext.Current.CancellationToken).TotalHits);

        var noMatch = new DrillDownQuery(baseQuery, new DrillDownSelection("category", "does-not-exist"));
        Assert.Equal(0, searcher.Search(noMatch, 10, TestContext.Current.CancellationToken).TotalHits);

        var hierarchy = new DrillDownQuery(
            baseQuery,
            new DrillDownSelection("hierarchy", new FacetPath("Technology", "Programming", "C#")));
        var hierarchyResults = searcher.Search(hierarchy, 10, TestContext.Current.CancellationToken);
        Assert.Equal(1, hierarchyResults.TotalHits);
        Assert.Equal("one", searcher.GetStoredFields(hierarchyResults.ScoreDocs[0].DocId)["id"][0]);

        var topN = searcher.Search(booksOrMagazines, 1, TestContext.Current.CancellationToken);
        Assert.Equal(4, topN.TotalHits);
        Assert.Single(topN.ScoreDocs);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static FacetResult GetFacet(IndexSearcher searcher, HierarchicalFacetRequest request)
    {
        var (_, facets) = searcher.SearchWithFacetRequests(new TermQuery("body", "common"), 1, [request]);
        return Assert.Single(facets);
    }

    private static int BucketCount(FacetResult result, string value)
        => result.Buckets.Single(bucket => !bucket.IsMissing && bucket.Value == value).Count;

    private static void AddHierarchyDocument(IndexWriter writer, IReadOnlyList<FacetPath> paths)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", "common"));
        foreach (var path in paths)
            FacetPathIndexer.AddToDocument(document, "hierarchy", path);
        writer.AddDocument(document);
    }

    private static void AddDrillDownDocument(
        IndexWriter writer,
        string id,
        string category,
        string language,
        FacetPath? hierarchy)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "phone"));
        document.Add(new StringField("category", category));
        document.Add(new StringField("language", language));
        if (hierarchy is not null)
            FacetPathIndexer.AddToDocument(document, "hierarchy", hierarchy);
        writer.AddDocument(document);
    }
}
