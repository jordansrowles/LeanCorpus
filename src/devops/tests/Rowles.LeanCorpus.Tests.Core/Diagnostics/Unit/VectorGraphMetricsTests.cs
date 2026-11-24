namespace Rowles.LeanCorpus.Tests.Core.Diagnostics;

/// <summary>Built-in collectors expose vector graph fallbacks, wasted traversal work and refused builds (ADR052).</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Diagnostics)]
public sealed class VectorGraphMetricsTests
{
    [Theory(DisplayName = "Metrics: Collectors Count Wasted Traversals, Predicted Fallbacks And Skipped Builds")]
    [InlineData(false)]
    [InlineData(true)]
    public void Collectors_CountWastedTraversalsPredictedFallbacksAndSkippedBuilds(bool meter)
    {
        using var disposable = meter ? new MeterMetricsCollector() : null;
        IMetricsCollector metrics = disposable ?? (IMetricsCollector)new DefaultMetricsCollector();

        metrics.RecordVectorSearchRoute(VectorSearchRoute.Graph);
        metrics.RecordVectorSearchRoute(VectorSearchRoute.ExactBudgetPredicted);
        metrics.RecordVectorSearchRoute(VectorSearchRoute.ExactBudgetExceeded);
        metrics.RecordVectorGraphWastedTraversal(TimeSpan.FromMilliseconds(2), 1_500);
        metrics.RecordVectorGraphWastedTraversal(TimeSpan.FromMilliseconds(1), 500);
        metrics.RecordVectorGraphBuildSkipped(VectorGraphBuildSkipReason.Unreachable);
        metrics.RecordVectorGraphBuildSkipped(VectorGraphBuildSkipReason.TooLarge);

        var snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.VectorGraphRouteCount);
        Assert.Equal(2, snapshot.VectorGraphFallbackCount);
        Assert.Equal(2, snapshot.VectorGraphWastedTraversalCount);
        Assert.Equal(2_000, snapshot.VectorGraphWastedEvaluations);
        Assert.Equal(3.0, snapshot.VectorGraphWastedTotalMs, 3);
        Assert.Equal(2, snapshot.VectorGraphBuildSkippedCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordVectorGraphWastedTraversal(TimeSpan.Zero, -1));
    }
}
