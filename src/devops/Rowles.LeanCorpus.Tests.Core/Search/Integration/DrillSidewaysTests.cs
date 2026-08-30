using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Integration coverage for drill-sideways facet scopes.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class DrillSidewaysTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public DrillSidewaysTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Drill Sideways: Selected Dimensions Exclude Their Own Constraint")]
    public void DrillSideways_SelectedDimensionsExcludeTheirOwnConstraint()
    {
        var directoryPath = SubDir(nameof(DrillSideways_SelectedDimensionsExcludeTheirOwnConstraint));
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            AddProduct(writer, "apple-red", "Apple", "Red", "Phones", new FacetPath("Technology", "Phones", "Apple"));
            AddProduct(writer, "apple-white", "Apple", "White", "Phones", new FacetPath("Technology", "Phones", "Apple"));
            AddProduct(writer, "samsung-red", "Samsung", "Red", "Tablets", new FacetPath("Technology", "Tablets", "Samsung"));
            AddProduct(writer, "samsung-black", "Samsung", "Black", "Tablets", null);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var query = new DrillDownQuery(
            new TermQuery("body", "phone"),
            new DrillDownSelection("brand", "Apple"),
            new DrillDownSelection("colour", "Red"));

        var (results, facets) = searcher.SearchWithDrillSideways(
            query,
            10,
            new FacetRequest("brand", offset: 1, limit: 1, order: FacetBucketOrder.ValueAscending),
            new FacetRequest("colour", order: FacetBucketOrder.ValueAscending),
            new FacetRequest("category", order: FacetBucketOrder.ValueAscending),
            new HierarchicalFacetRequest("hierarchy", includeMissing: true));

        Assert.Equal(1, results.TotalHits);
        Assert.Equal("apple-red", searcher.GetStoredFields(results.ScoreDocs[0].DocId)["id"][0]);

        var brand = facets.Single(facet => facet.FieldName == "brand");
        Assert.Equal(2, brand.TotalBucketCount);
        Assert.Equal(["Samsung"], brand.Buckets.Select(bucket => bucket.Value));

        var colour = facets.Single(facet => facet.FieldName == "colour");
        Assert.Equal(["Red", "White"], colour.Buckets.Select(bucket => bucket.Value));
        Assert.All(colour.Buckets, bucket => Assert.Equal(1, bucket.Count));

        var category = facets.Single(facet => facet.FieldName == "category");
        Assert.Equal(["Phones"], category.Buckets.Select(bucket => bucket.Value));
        Assert.Equal(1, category.Buckets[0].Count);

        var hierarchy = facets.Single(facet => facet.FieldName == "hierarchy");
        Assert.Equal(1, hierarchy.TotalBucketCount);
        Assert.Equal("Technology", Assert.Single(hierarchy.Buckets).Value);
        Assert.Equal(1, hierarchy.Buckets[0].Count);
        Assert.Equal(0, hierarchy.MissingCount);
    }

    [Fact(DisplayName = "Drill Sideways: Zero Hit Combinations Keep Alternatives")]
    public void DrillSideways_ZeroHitCombinationsKeepAlternatives()
    {
        var directoryPath = SubDir(nameof(DrillSideways_ZeroHitCombinationsKeepAlternatives));
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            AddProduct(writer, "apple-red", "Apple", "Red", "Phones", null);
            AddProduct(writer, "apple-white", "Apple", "White", "Phones", null);
            AddProduct(writer, "samsung-black", "Samsung", "Black", "Tablets", null);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var query = new DrillDownQuery(
            new TermQuery("body", "phone"),
            new DrillDownSelection("brand", "Apple"),
            new DrillDownSelection("colour", "Black"));

        var (results, facets) = searcher.SearchWithDrillSideways(
            query,
            10,
            new FacetRequest("brand", order: FacetBucketOrder.ValueAscending),
            new FacetRequest("colour", order: FacetBucketOrder.ValueAscending));

        Assert.Equal(0, results.TotalHits);
        var brand = facets.Single(facet => facet.FieldName == "brand");
        Assert.Equal(["Samsung"], brand.Buckets.Select(bucket => bucket.Value));
        var colour = facets.Single(facet => facet.FieldName == "colour");
        Assert.Equal(["Red", "White"], colour.Buckets.Select(bucket => bucket.Value));
    }

    [Fact(DisplayName = "Drill Sideways: Selected Hierarchy Uses Base Scope And Missing Bucket")]
    public void DrillSideways_SelectedHierarchyUsesBaseScopeAndMissingBucket()
    {
        var directoryPath = SubDir(nameof(DrillSideways_SelectedHierarchyUsesBaseScopeAndMissingBucket));
        using (var writer = new IndexWriter(
            new MMapDirectory(directoryPath),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            AddProduct(writer, "apple-red", "Apple", "Red", "Phones", new FacetPath("Technology", "Phones", "Apple"));
            AddProduct(writer, "apple-white", "Apple", "White", "Phones", new FacetPath("Technology", "Phones", "Apple"));
            AddProduct(writer, "samsung-red", "Samsung", "Red", "Tablets", new FacetPath("Technology", "Tablets", "Samsung"));
            AddProduct(writer, "samsung-black", "Samsung", "Black", "Tablets", null);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var query = new DrillDownQuery(
            new TermQuery("body", "phone"),
            new DrillDownSelection("hierarchy", new FacetPath("Technology", "Phones", "Apple")));

        var (results, facets) = searcher.SearchWithDrillSideways(
            query,
            10,
            new HierarchicalFacetRequest("hierarchy", includeMissing: true));

        Assert.Equal(2, results.TotalHits);
        var hierarchy = Assert.Single(facets);
        Assert.Equal(2, hierarchy.TotalBucketCount);
        var technology = Assert.Single(hierarchy.Buckets, bucket => !bucket.IsMissing);
        var missing = Assert.Single(hierarchy.Buckets, bucket => bucket.IsMissing);
        Assert.Equal("Technology", technology.Value);
        Assert.Equal(3, technology.Count);
        Assert.Equal(1, missing.Count);
        Assert.Equal(1, hierarchy.MissingCount);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AddProduct(
        IndexWriter writer,
        string id,
        string brand,
        string colour,
        string category,
        FacetPath? hierarchy)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "phone"));
        document.Add(new StringField("brand", brand));
        document.Add(new StringField("colour", colour));
        document.Add(new StringField("category", category));
        if (hierarchy is not null)
            FacetPathIndexer.AddToDocument(document, "hierarchy", hierarchy);
        writer.AddDocument(document);
    }
}
