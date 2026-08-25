namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Unit tests for <see cref="AggregationResult"/> computed properties and factory.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class AggregationResultTests
{
    [Fact(DisplayName = "AggregationResult.Avg: Count Zero Returns 0.0")]
    public void Avg_CountZero_ReturnsZero()
    {
        var r = new AggregationResult { Name = "totals", Field = "price", Count = 0, Sum = 100.0 };
        Assert.Equal(0.0, r.Avg);
    }

    [Fact(DisplayName = "AggregationResult.Avg: Count Greater Than Zero Returns Sum Divided By Count")]
    public void Avg_CountPositive_ReturnsSumDividedByCount()
    {
        var r = new AggregationResult { Name = "totals", Field = "price", Count = 4, Sum = 100.0 };
        Assert.Equal(25.0, r.Avg, precision: 10);
    }

    [Fact(DisplayName = "AggregationResult.Empty: Returns Zero Counts And Expected Names")]
    public void Empty_ReturnsZeroCountsAndExpectedNames()
    {
        var r = AggregationResult.Empty("totals", "price");
        Assert.Equal("totals", r.Name);
        Assert.Equal("price", r.Field);
        Assert.Equal(0, r.Count);
        Assert.Equal(0.0, r.Sum);
        Assert.Equal(0.0, r.Min);
        Assert.Equal(0.0, r.Max);
        Assert.Equal(0.0, r.Avg);
    }
}
