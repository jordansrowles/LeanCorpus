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

    [Fact(DisplayName = "Numeric aggregation states: Cardinality And Percentiles Consume Numeric Values")]
    public void ApproximateStates_ConsumeNumericValues()
    {
        var cardinality = Assert.IsType<CardinalityAggregationState>(
            NumericAggregationStateFactory.Create(new AggregationRequest("cardinality", "value", AggregationType.Cardinality)));
        var digest = Assert.IsType<TDigestPercentilesAggregationState>(
            NumericAggregationStateFactory.Create(new AggregationRequest("digest", "value", AggregationType.TDigestPercentiles) { Percentiles = [50, 99] }));
        var hdr = Assert.IsType<HdrPercentilesAggregationState>(
            NumericAggregationStateFactory.Create(new AggregationRequest("hdr", "latency", AggregationType.HdrPercentiles) { HdrHighestTrackableValue = 10_000, Percentiles = [50, 99] }));

        cardinality.Collect(NumericDocumentValues.Multiple(new long[] { 1, 1, 2, 3 }));
        digest.Collect(NumericDocumentValues.Multiple(new double[] { 1, 2, 3, 4, 5 }));
        hdr.Collect(NumericDocumentValues.Multiple(new long[] { 1, 10, 100, 1_000 }));

        Assert.InRange(((CardinalityAggregationResult)cardinality.Finish()).EstimatedCardinality, 2.5, 3.5);
        Assert.Equal([50d, 99d], ((PercentileAggregationResult)digest.Finish()).Percentiles.Select(value => value.Percentile));
        Assert.Equal(4, ((PercentileAggregationResult)hdr.Finish()).Count);
    }
}
