namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Unit tests for <see cref="AggregationRequest"/> null-guard and property branches.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class AggregationRequestTests
{
    [Fact(DisplayName = "AggregationRequest: Null Name Throws ArgumentNullException")]
    public void NullName_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new AggregationRequest(null!, "price"));

    [Fact(DisplayName = "AggregationRequest: Null Field Throws ArgumentNullException")]
    public void NullField_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new AggregationRequest("totals", null!));

    [Fact(DisplayName = "AggregationRequest: Default Type Is Stats")]
    public void DefaultType_IsStats()
    {
        var r = new AggregationRequest("totals", "price");
        Assert.Equal(AggregationType.Stats, r.Type);
    }

    [Fact(DisplayName = "AggregationRequest: Histogram Type Stored Correctly")]
    public void HistogramType_StoredCorrectly()
    {
        var r = new AggregationRequest("buckets", "price", AggregationType.Histogram);
        Assert.Equal(AggregationType.Histogram, r.Type);
    }

    [Fact(DisplayName = "AggregationRequest: HistogramInterval Has Default Value")]
    public void HistogramInterval_HasDefaultValue()
    {
        var r = new AggregationRequest("buckets", "price");
        Assert.Equal(10.0, r.HistogramInterval);
    }
}
