using Rowles.LeanCorpus.Search.Aggregations;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Unit coverage for reusable numeric aggregation states.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class NumericAggregationExecutionTests
{
    [Fact(DisplayName = "Numeric aggregation states: Stats Preserve Exact Semantics")]
    public void StatsState_PreservesExactSemantics()
    {
        var state = Assert.IsType<StatsAggregationState>(
            NumericAggregationStateFactory.Create(new AggregationRequest("stats", "value")));

        state.Collect(NumericDocumentValues.Single(2L));
        state.Collect(NumericDocumentValues.Multiple(new long[] { 4, 6 }));

        var result = state.Finish();
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Min);
        Assert.Equal(6, result.Max);
        Assert.Equal(12, result.Sum);
        Assert.Equal(4, result.Avg);
    }

    [Fact(DisplayName = "Numeric aggregation states: Histogram Preserves Buckets")]
    public void HistogramState_PreservesBuckets()
    {
        var state = Assert.IsType<HistogramAggregationState>(
            NumericAggregationStateFactory.Create(new AggregationRequest("histogram", "value", AggregationType.Histogram)
            {
                HistogramInterval = 10
            }));

        state.Collect(NumericDocumentValues.Multiple(new double[] { 1, 11, 19 }));

        var result = state.Finish();
        Assert.Equal(3, result.Count);
        Assert.Equal([1L, 2L, 0L], result.Buckets!.Select(bucket => bucket.Count));
    }

    [Fact(DisplayName = "Numeric aggregation states: Merge Rejects Incompatible Configuration")]
    public void Merge_RejectsIncompatibleConfiguration()
    {
        var left = new StatsAggregationState(new AggregationRequest("left", "price"));
        var right = new StatsAggregationState(new AggregationRequest("right", "rating"));

        Assert.Throws<ArgumentException>(() => left.MergeFrom(right));
    }
}
