namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Unit tests for shared facet request and result contracts.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class FacetRequestTests
{
    [Fact(DisplayName = "FacetRequest: Defaults To Count Descending")]
    public void DefaultsToCountDescending()
    {
        var request = new FacetRequest("category");

        Assert.Equal("category", request.Field);
        Assert.Equal(0, request.Offset);
        Assert.Equal(int.MaxValue, request.Limit);
        Assert.Equal(FacetBucketOrder.CountDescending, request.Order);
        Assert.False(request.IncludeMissing);
    }

    [Theory(DisplayName = "FacetRequest: Accepts Every Bucket Order")]
    [InlineData(FacetBucketOrder.CountDescending)]
    [InlineData(FacetBucketOrder.CountAscending)]
    [InlineData(FacetBucketOrder.ValueAscending)]
    [InlineData(FacetBucketOrder.ValueDescending)]
    public void AcceptsEveryBucketOrder(FacetBucketOrder order)
        => Assert.Equal(order, new FacetRequest("category", order: order).Order);

    [Fact(DisplayName = "FacetRequest: Null Field Throws ArgumentNullException")]
    public void NullFieldThrows()
        => Assert.Throws<ArgumentNullException>(() => new FacetRequest(null!));

    [Fact(DisplayName = "FacetRequest: Negative Offset Throws ArgumentOutOfRangeException")]
    public void NegativeOffsetThrows()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FacetRequest("category", offset: -1));

    [Fact(DisplayName = "FacetRequest: Negative Limit Throws ArgumentOutOfRangeException")]
    public void NegativeLimitThrows()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FacetRequest("category", limit: -1));

    [Fact(DisplayName = "FacetRequest: Zero Limit Returns No Buckets")]
    public void ZeroLimitReturnsNoBuckets()
    {
        var buckets = new[] { new FacetBucket("books", 2) };

        Assert.Empty(FacetBucketHelpers.Page(buckets, offset: 0, limit: 0));
    }

    [Fact(DisplayName = "FacetBucket: Missing Marker Does Not Collide With Empty Value")]
    public void MissingMarkerDoesNotCollideWithEmptyValue()
    {
        var emptyValue = new FacetBucket(string.Empty, 3);
        var missing = FacetBucket.Missing(3);

        Assert.Equal(emptyValue.Value, missing.Value);
        Assert.False(emptyValue.IsMissing);
        Assert.True(missing.IsMissing);
        Assert.NotEqual(emptyValue, missing);
    }

    [Fact(DisplayName = "Facet Buckets: Count Ties Break By Ordinal Value")]
    public void CountTiesBreakByOrdinalValue()
    {
        var buckets = new List<FacetBucket>
        {
            new("zebra", 2),
            new("apple", 2),
            new("middle", 3)
        };

        buckets.Sort(FacetBucketHelpers.GetComparer(FacetBucketOrder.CountDescending));

        Assert.Equal(["middle", "apple", "zebra"], buckets.Select(bucket => bucket.Value));
    }

    [Fact(DisplayName = "FacetResult: Exposes Paging And Missing Metadata")]
    public void ResultExposesPagingAndMissingMetadata()
    {
        var result = new FacetResult(
            "category",
            [new FacetBucket("books", 2)],
            totalBucketCount: 4,
            missingCount: 3);

        Assert.Equal("category", result.FieldName);
        Assert.Equal(4, result.TotalBucketCount);
        Assert.Equal(3, result.MissingCount);
    }

    [Fact(DisplayName = "FacetsCollector: Requested Missing Values Are Retained Internally")]
    public void RequestedMissingValuesAreRetainedInternally()
    {
        var collector = new FacetsCollector([new FacetRequest("category", includeMissing: true)]);

        collector.CollectDocumentValue("category", documentId: 1, "books");
        collector.CollectDocumentValue("category", documentId: 1, "books");
        collector.CollectMissing("category", documentId: 2);
        collector.CollectMissing("category", documentId: 2);
        collector.CollectMissing("category", documentId: 3);

        var result = Assert.Single(collector.GetResults());
        Assert.Equal(1, Assert.Single(result.Buckets).Count);
        Assert.Equal(2, result.MissingCount);
    }
}
