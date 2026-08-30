using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Integration coverage for explicit numeric and date facet ranges.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class NumericAndDateRangeFacetTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public NumericAndDateRangeFacetTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Numeric Range Facets: Boundaries, Overlap, Missing And Paging")]
    public void NumericRangeFacets_CoverBoundariesOverlapMissingAndPaging()
    {
        var directoryPath = SubDir(nameof(NumericRangeFacets_CoverBoundariesOverlapMissingAndPaging));
        using (var writer = CreateWriter(directoryPath))
        {
            AddNumericDocument(writer, "multi", 5, 6);
            AddNumericDocument(writer, "upper-edge", 10);
            AddNumericDocument(writer, "exact-edge", 20);
            AddNumericDocument(writer, "no-match", 100);
            AddNumericDocument(writer, "missing");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new NumericRangeFacetRequest(
                "value",
                [
                    new NumericRange("below-ten", upperBound: 10),
                    new NumericRange("ten-to-twenty", 10, 20),
                    new NumericRange("exact-twenty", 20, 20, includeLower: true, includeUpper: true),
                    new NumericRange("wide", 0, 30, includeUpper: true),
                    new NumericRange("empty", 30, 40)
                ],
                order: FacetBucketOrder.ValueAscending,
                includeMissing: true)]);

        var facet = Assert.Single(facets);
        Assert.Equal(6, facet.TotalBucketCount);
        Assert.Equal(1, Bucket(facet, "below-ten").Count);
        Assert.Equal(1, Bucket(facet, "ten-to-twenty").Count);
        Assert.Equal(1, Bucket(facet, "exact-twenty").Count);
        Assert.Equal(3, Bucket(facet, "wide").Count);
        Assert.Equal(0, Bucket(facet, "empty").Count);
        Assert.Equal(1, Assert.Single(facet.Buckets, bucket => bucket.IsMissing).Count);
        Assert.Equal(1, facet.MissingCount);

        var (_, pagedFacets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new NumericRangeFacetRequest(
                "value",
                [
                    new NumericRange("zulu", upperBound: 10),
                    new NumericRange("alpha", 10, 20),
                    new NumericRange("middle", 30, 40)
                ],
                offset: 1,
                limit: 1,
                order: FacetBucketOrder.ValueAscending)]);

        var paged = Assert.Single(pagedFacets);
        Assert.Equal(3, paged.TotalBucketCount);
        Assert.Equal("middle", Assert.Single(paged.Buckets).Value);
    }

    [Fact(DisplayName = "Numeric Range Facets: Validation And Exact Int64 Precision")]
    public void NumericRangeFacets_ValidateRangesAndPreserveInt64Precision()
    {
        Assert.Throws<ArgumentException>(() => new NumericRange("reversed", 10, 1));
        Assert.Throws<ArgumentException>(() => new NumericRange("empty", 5, 5));
        Assert.Throws<ArgumentException>(() => new NumericRange(" ", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumericRange("infinite", double.NegativeInfinity, 1));
        Assert.Throws<ArgumentException>(() => new Int64Range("reversed", 10, 1));

        var directoryPath = SubDir(nameof(NumericRangeFacets_ValidateRangesAndPreserveInt64Precision));
        const long first = 9_007_199_254_740_993L;
        const long second = 9_007_199_254_740_994L;
        using (var writer = CreateWriter(directoryPath))
        {
            AddInt64Document(writer, "first", first);
            AddInt64Document(writer, "second", second);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new Int64RangeFacetRequest(
                "value",
                [
                    new Int64Range("first-only", first, first, includeLower: true, includeUpper: true),
                    new Int64Range("second-only", second, second, includeLower: true, includeUpper: true),
                    new Int64Range("all", first, null)
                ],
                order: FacetBucketOrder.ValueAscending)]);

        var facet = Assert.Single(facets);
        Assert.Equal(3, facet.TotalBucketCount);
        Assert.Equal(1, Bucket(facet, "first-only").Count);
        Assert.Equal(1, Bucket(facet, "second-only").Count);
        Assert.Equal(2, Bucket(facet, "all").Count);
    }

    [Fact(DisplayName = "Date Range Facets: UTC Offsets Boundaries PreEpoch Missing And MultiValue")]
    public void DateRangeFacets_UseUtcUnixMillisecondsAndDocumentCounts()
    {
        var directoryPath = SubDir(nameof(DateRangeFacets_UseUtcUnixMillisecondsAndDocumentCounts));
        var january = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var february = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var equivalentJanuary = new DateTimeOffset(2025, 12, 31, 19, 0, 0, TimeSpan.FromHours(-5));
        using (var writer = CreateWriter(directoryPath))
        {
            AddDateDocument(writer, "january-multi", january, january.AddDays(1));
            AddDateDocument(writer, "equivalent-offset", equivalentJanuary);
            AddDateDocument(writer, "february", february);
            AddDateDocument(writer, "before-epoch", DateTimeOffset.UnixEpoch.AddDays(-1));
            AddDateDocument(writer, "after-ranges", february.AddMonths(2));
            AddDateDocument(writer, "missing");
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(directoryPath));
        var (_, facets) = searcher.SearchWithFacetRequests(
            new TermQuery("body", "common"),
            1,
            [new DateRangeFacetRequest(
                "date",
                [
                    new DateRange("before-january", upperBound: january),
                    new DateRange("january", january, february),
                    new DateRange("on-february", february, february, includeLower: true, includeUpper: true),
                    new DateRange("after-february", february, null)
                ],
                order: FacetBucketOrder.ValueAscending,
                includeMissing: true)]);

        var facet = Assert.Single(facets);
        Assert.Equal(5, facet.TotalBucketCount);
        Assert.Equal(1, Bucket(facet, "before-january").Count);
        Assert.Equal(2, Bucket(facet, "january").Count);
        Assert.Equal(1, Bucket(facet, "on-february").Count);
        Assert.Equal(2, Bucket(facet, "after-february").Count);
        Assert.Equal(1, Assert.Single(facet.Buckets, bucket => bucket.IsMissing).Count);
        Assert.Equal(1, facet.MissingCount);

        Assert.Throws<ArgumentException>(() => new DateRange("reversed", february, january));
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriter CreateWriter(string path)
        => new(
            new MMapDirectory(path),
            new IndexWriterConfig { MaxBufferedDocs = 1, MergePolicy = NoMergePolicy.Instance });

    private static void AddNumericDocument(IndexWriter writer, string id, params double[] values)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "common"));
        foreach (double value in values)
            document.Add(new NumericField("value", value, stored: false));
        writer.AddDocument(document);
    }

    private static void AddInt64Document(IndexWriter writer, string id, long value)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "common"));
        document.Add(new Int64Field("value", value, stored: false));
        writer.AddDocument(document);
    }

    private static void AddDateDocument(IndexWriter writer, string id, params DateTimeOffset[] values)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", "common"));
        foreach (DateTimeOffset value in values)
            document.Add(new Int64Field("date", value.ToUnixTimeMilliseconds(), stored: false));
        writer.AddDocument(document);
    }

    private static FacetBucket Bucket(FacetResult facet, string label)
        => Assert.Single(facet.Buckets, bucket => !bucket.IsMissing && bucket.Value == label);
}
