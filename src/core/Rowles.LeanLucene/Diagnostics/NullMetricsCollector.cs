namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// No-op metrics collector — the default when no metrics are configured.
/// </summary>
public sealed class NullMetricsCollector : IMetricsCollector
{
    /// <summary>Gets the shared singleton instance of the no-op collector.</summary>
    public static readonly NullMetricsCollector Instance = new();

    /// <inheritdoc/>
    public void RecordSearchLatency(TimeSpan elapsed) { }

    /// <inheritdoc/>
    public void RecordCacheHit() { }

    /// <inheritdoc/>
    public void RecordCacheMiss() { }

    /// <inheritdoc/>
    public void RecordFlush(TimeSpan elapsed) { }

    /// <inheritdoc/>
    public void RecordMerge(TimeSpan elapsed, int segmentsMerged) { }

    /// <inheritdoc/>
    public void RecordCommit(TimeSpan elapsed) { }

    /// <inheritdoc/>
    public MetricsSnapshot GetSnapshot() => MetricsSnapshot.Empty;
}
