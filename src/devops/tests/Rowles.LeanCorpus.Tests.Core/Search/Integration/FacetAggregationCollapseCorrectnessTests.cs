using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Contains unit tests for Facet Aggregation Collapse Correctness.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class FacetAggregationCollapseCorrectnessTests : IDisposable
{
    private readonly string _dir;

    public FacetAggregationCollapseCorrectnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_exact_collectors_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// Verifies the Facets: Count All Matching Documents Not Only Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Facets: Count All Matching Documents Not Only Top N")]
    public void Facets_CountAllMatchingDocuments_NotOnlyTopN()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 12; i++)
            writer.AddDocument(MakeDocument("common", i < 11 ? "dominant" : "rare", i));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "group");

        var groupFacet = Assert.Single(facets);
        Assert.Equal(11, groupFacet.Buckets.Single(b => b.Value == "dominant").Count);
        Assert.Equal(1, groupFacet.Buckets.Single(b => b.Value == "rare").Count);
        Assert.Collection(
            groupFacet.Buckets,
            bucket => Assert.Equal("dominant", bucket.Value),
            bucket => Assert.Equal("rare", bucket.Value));
    }

    /// <summary>
    /// Verifies the Aggregations: Count All Matching Documents Not Only Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Aggregations: Count All Matching Documents Not Only Top N")]
    public void Aggregations_CountAllMatchingDocuments_NotOnlyTopN()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 12; i++)
            writer.AddDocument(MakeDocument("common", "all", i));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("body", "common"),
            1,
            new AggregationRequest("price_stats", "price"));

        Assert.Equal(12, aggregations[0].Count);
        Assert.Equal(66, aggregations[0].Sum);
    }

    /// <summary>
    /// Verifies multi-valued facets use sorted-set DocValues and do not require stored fields.
    /// </summary>
    [Fact(DisplayName = "Facets: Multi Valued String Fields Use Doc Values")]
    public void Facets_MultiValuedStringFields_UseDocValues()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "common"));
        doc1.Add(new StringField("tag", "red", stored: false));
        doc1.Add(new StringField("tag", "blue", stored: false));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "common"));
        doc2.Add(new StringField("tag", "red", stored: false));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "tag");

        var tagFacet = Assert.Single(facets);
        Assert.Equal(2, tagFacet.Buckets.Single(b => b.Value == "red").Count);
        Assert.Equal(1, tagFacet.Buckets.Single(b => b.Value == "blue").Count);
    }

    /// <summary>
    /// Verifies that each distinct value contributes once per matching document,
    /// including when a document repeats one value and another document is missing the field.
    /// </summary>
    [Fact(DisplayName = "Facets: Multi Valued Values Count Once Per Document With Missing")]
    public void Facets_MultiValuedValues_CountOncePerDocumentWithMissing()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());

        var valued = new LeanDocument();
        valued.Add(new TextField("body", "common"));
        valued.Add(new StringField("tag", "red", stored: false));
        valued.Add(new StringField("tag", "red", stored: false));
        valued.Add(new StringField("tag", "blue", stored: false));
        writer.AddDocument(valued);

        var missing = new LeanDocument();
        missing.Add(new TextField("body", "common"));
        writer.AddDocument(missing);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("tag", includeMissing: true)]);

        var facet = Assert.Single(facets);
        Assert.Equal(3, facet.TotalBucketCount);
        Assert.Equal(1, facet.Buckets.Single(bucket => bucket.Value == "red").Count);
        Assert.Equal(1, facet.Buckets.Single(bucket => bucket.Value == "blue").Count);
        Assert.Equal(1, facet.Buckets.Single(bucket => bucket.IsMissing).Count);
    }

    /// <summary>
    /// Verifies requested facet fields share one matching-document collection pass and
    /// repeated logical values from a document do not inflate a bucket count.
    /// </summary>
    [Fact(DisplayName = "Facets: Multiple Fields And Duplicate Values Count Once Per Document")]
    public void Facets_MultipleFieldsAndDuplicateValues_CountOncePerDocument()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "common"));
        doc.Add(new StringField("tag", "red", stored: false));
        doc.Add(new StringField("tag", "red", stored: false));
        doc.Add(new StringField("category", "books", stored: false));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "tag", "category");

        Assert.Equal(2, facets.Count);
        Assert.Equal(1, Assert.Single(facets.Single(facet => facet.FieldName == "tag").Buckets).Count);
        Assert.Equal(1, Assert.Single(facets.Single(facet => facet.FieldName == "category").Buckets).Count);
    }

    /// <summary>
    /// Verifies the legacy API omits empty facet results and accepts a repeated requested field.
    /// </summary>
    [Fact(DisplayName = "Facets: Legacy Empty Results And Repeated Fields Remain Compatible")]
    public void Facets_LegacyEmptyResultsAndRepeatedFields_RemainCompatible()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        writer.AddDocument(MakeDocument("common", "books", 10));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (_, noMatchFacets) = searcher.SearchWithFacets(new TermQuery("body", "absent"), 10, "group");
        var (_, missingFieldFacets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 10, "missing");
        var (_, repeatedFieldFacets) = searcher.SearchWithFacets(
            new TermQuery("body", "common"), 10, "group", "group");

        Assert.Empty(noMatchFacets);
        Assert.Empty(missingFieldFacets);
        Assert.Equal(1, Assert.Single(Assert.Single(repeatedFieldFacets).Buckets).Count);
    }

    /// <summary>
    /// Verifies missing detection is driven by real segment data and remains correct on
    /// the complete second-pass path used by non-term queries.
    /// </summary>
    [Fact(DisplayName = "Facets: Missing Values Collected Through Second Pass")]
    public void Facets_MissingValuesCollectedThroughSecondPass()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        writer.AddDocument(MakeDocument("common", "books", 10));

        var missingCategory = new LeanDocument();
        missingCategory.Add(new TextField("body", "common"));
        missingCategory.Add(new NumericField("price", 20));
        writer.AddDocument(missingCategory);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (results, facets) = searcher.SearchWithFacetRequests(
            new MatchAllDocsQuery(),
            1,
            [new FacetRequest("group", includeMissing: true)]);

        Assert.Equal(2, results.TotalHits);
        var facet = Assert.Single(facets);
        Assert.Equal(2, facet.Buckets.Count);
        Assert.Equal(1, facet.Buckets.Single(bucket => !bucket.IsMissing).Count);
        Assert.Equal(1, facet.Buckets.Single(bucket => bucket.IsMissing).Count);
        Assert.Equal(1, facet.MissingCount);
        Assert.Equal(2, facet.TotalBucketCount);
    }

    /// <summary>
    /// Verifies all advanced bucket ordering modes, deterministic ties and
    /// missing-bucket placement.
    /// </summary>
    [Fact(DisplayName = "Facets: Advanced Ordering Modes Are Deterministic")]
    public void Facets_AdvancedOrderingModes_AreDeterministic()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        AddFacetDocument(writer, "a");
        AddFacetDocument(writer, "a");
        AddFacetDocument(writer, "z");
        AddFacetDocument(writer, "z");
        AddFacetDocument(writer, "b");
        AddFacetDocument(writer, null);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        Assert.Equal(
            ["a", "z", "b", ""],
            GetFacetValues(searcher, FacetBucketOrder.CountDescending));
        Assert.Equal(
            ["b", "", "a", "z"],
            GetFacetValues(searcher, FacetBucketOrder.CountAscending));
        Assert.Equal(
            ["a", "b", "z", ""],
            GetFacetValues(searcher, FacetBucketOrder.ValueAscending));
        Assert.Equal(
            ["z", "b", "a", ""],
            GetFacetValues(searcher, FacetBucketOrder.ValueDescending));

        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", order: FacetBucketOrder.CountDescending, includeMissing: true)]);
        var facet = Assert.Single(facets);
        Assert.Equal(4, facet.TotalBucketCount);
        Assert.Equal(1, facet.MissingCount);
        Assert.True(facet.Buckets[^1].IsMissing);
    }

    /// <summary>
    /// Verifies offset and limit are applied after complete deterministic ordering.
    /// </summary>
    [Fact(DisplayName = "Facets: Offset And Limit Page Ordered Buckets")]
    public void Facets_OffsetAndLimit_PageOrderedBuckets()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        foreach (string value in new[] { "A", "B", "C", "D", "E" })
            AddFacetDocument(writer, value);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", offset: 2, limit: 2, order: FacetBucketOrder.ValueAscending)]);

        var facet = Assert.Single(facets);
        Assert.Equal(5, facet.TotalBucketCount);
        Assert.Equal(["C", "D"], facet.Buckets.Select(bucket => bucket.Value));

        var (_, emptyPage) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", offset: 50, limit: 2, order: FacetBucketOrder.ValueAscending)]);
        var emptyFacet = Assert.Single(emptyPage);
        Assert.Empty(emptyFacet.Buckets);
        Assert.Equal(5, emptyFacet.TotalBucketCount);

        var (_, zeroLimitPage) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", limit: 0, order: FacetBucketOrder.ValueAscending)]);
        var zeroLimitFacet = Assert.Single(zeroLimitPage);
        Assert.Empty(zeroLimitFacet.Buckets);
        Assert.Equal(5, zeroLimitFacet.TotalBucketCount);
    }

    /// <summary>
    /// Verifies an explicit empty string is a real value and remains distinct
    /// from the opt-in missing bucket across segments.
    /// </summary>
    [Fact(DisplayName = "Facets: Empty String Is Not Missing")]
    public void Facets_EmptyString_IsNotMissing()
    {
        using var writer = new IndexWriter(
            new MMapDirectory(_dir),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance });
        AddFacetDocument(writer, "");
        AddFacetDocument(writer, null);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", order: FacetBucketOrder.ValueAscending, includeMissing: true)]);

        var facet = Assert.Single(facets);
        Assert.Equal(2, facet.TotalBucketCount);
        var empty = facet.Buckets.Single(bucket => !bucket.IsMissing);
        Assert.Equal(string.Empty, empty.Value);
        Assert.Equal(1, empty.Count);
        Assert.Equal(1, facet.Buckets.Single(bucket => bucket.IsMissing).Count);
        Assert.Equal(1, facet.MissingCount);
    }

    /// <summary>
    /// Verifies high-cardinality collection retains total-count metadata while
    /// returning only the requested page.
    /// </summary>
    [Fact(DisplayName = "Facets: High Cardinality Paging Retains Total Count")]
    public void Facets_HighCardinalityPaging_RetainsTotalCount()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 128; i++)
            AddFacetDocument(writer, $"value-{i:D3}");
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", offset: 50, limit: 3, order: FacetBucketOrder.ValueAscending)]);

        var facet = Assert.Single(facets);
        Assert.Equal(128, facet.TotalBucketCount);
        Assert.Equal(["value-050", "value-051", "value-052"], facet.Buckets.Select(bucket => bucket.Value));
    }

    /// <summary>
    /// Verifies repeated numeric values contribute every sorted-numeric value to stats.
    /// </summary>
    [Fact(DisplayName = "Aggregations: Multi Valued Numeric Fields Use All Values")]
    public void Aggregations_MultiValuedNumericFields_UseAllValues()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "common"));
        doc1.Add(new NumericField("price", 10, stored: false));
        doc1.Add(new NumericField("price", 2, stored: false));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "common"));
        doc2.Add(new NumericField("price", 3, stored: false));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("body", "common"),
            1,
            new AggregationRequest("price_stats", "price"));

        Assert.Equal(3, aggregations[0].Count);
        Assert.Equal(2, aggregations[0].Min);
        Assert.Equal(10, aggregations[0].Max);
        Assert.Equal(15, aggregations[0].Sum);
        Assert.Equal(5, aggregations[0].Avg);
    }

    /// <summary>
    /// Verifies facet and aggregation collection includes matching documents in every segment.
    /// </summary>
    [Fact(DisplayName = "Facets And Aggregations: Collect Across Multiple Segments")]
    public void FacetsAndAggregations_CollectAcrossMultipleSegments()
    {
        using var writer = new IndexWriter(
            new MMapDirectory(_dir),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance });

        writer.AddDocument(MakeDocument("common", "first", 10));
        writer.Commit();
        writer.AddDocument(MakeDocument("common", "second", 20));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "group");
        var (_, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("body", "common"),
            1,
            new AggregationRequest("price_stats", "price"));

        var groupFacet = Assert.Single(facets);
        Assert.Equal(2, groupFacet.Buckets.Count);
        Assert.All(groupFacet.Buckets, bucket => Assert.Equal(1, bucket.Count));
        Assert.Equal(2, aggregations[0].Count);
        Assert.Equal(10, aggregations[0].Min);
        Assert.Equal(20, aggregations[0].Max);
        Assert.Equal(30, aggregations[0].Sum);
        Assert.Equal(15, aggregations[0].Avg);
    }

    [Fact(DisplayName = "Facets And Aggregations: Deleted Documents Stay Excluded After Force Merge")]
    public void FacetsAndAggregations_ExcludeDeletedDocumentsAfterForceMerge()
    {
        using (var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance }))
        {
            writer.AddDocument(MakeDocument("common", "kept", 10));
            var deleted = MakeDocument("common", "deleted", 1_000);
            deleted.Add(new StringField("id", "deleted"));
            writer.AddDocument(deleted);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "deleted"));
            writer.Commit();
            writer.ForceMerge(1);
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 10, "group");
        var (_, aggregations) = searcher.SearchWithAggregations(new TermQuery("body", "common"), 10, new AggregationRequest("price", "price"));

        Assert.Equal([("kept", 1)], Assert.Single(facets).Buckets.Select(static bucket => (bucket.Value, bucket.Count)).ToArray());
        Assert.Equal(1, aggregations[0].Count);
        Assert.Equal(10, aggregations[0].Sum);
    }

    /// <summary>
    /// Verifies the Collapse: Sees Groups Outside Original Over Fetch Window scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Sees Groups Outside Original Over Fetch Window")]
    public void Collapse_SeesGroupsOutsideOriginalOverFetchWindow()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 11; i++)
            writer.AddDocument(MakeDocument("common", "dominant", i));
        writer.AddDocument(MakeDocument("common", "rare", 99));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.SearchWithCollapse(
            new TermQuery("body", "common"),
            1,
            new CollapseField("group"));

        Assert.Equal(2, results.TotalHits);
        Assert.Single(results.ScoreDocs);
    }

    private static LeanDocument MakeDocument(string body, string group, double price)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("group", group));
        doc.Add(new NumericField("price", price));
        return doc;
    }

    private static void AddFacetDocument(IndexWriter writer, string? group)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "common"));
        if (group is not null)
            doc.Add(new StringField("group", group, stored: false));
        writer.AddDocument(doc);
    }

    private static string[] GetFacetValues(IndexSearcher searcher, FacetBucketOrder order)
    {
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new FacetRequest("group", order: order, includeMissing: true)]);
        return Assert.Single(facets).Buckets
            .Select(bucket => bucket.IsMissing ? string.Empty : bucket.Value)
            .ToArray();
    }
}
