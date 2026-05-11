# Metrics

Both `IndexWriterConfig` and `IndexSearcherConfig` accept an
<xref:Rowles.LeanCorpus.Diagnostics.IMetricsCollector>. The default
(`NullMetricsCollector.Instance`) records nothing.

## DefaultMetricsCollector

In-process collector with `Interlocked` counters. Pull a snapshot at any time:

```csharp
using Rowles.LeanCorpus.Diagnostics;

var metrics = new DefaultMetricsCollector();
var config = new IndexSearcherConfig { Metrics = metrics };

// ... run searches ...

MetricsSnapshot snap = metrics.GetSnapshot();
Console.WriteLine($"Searches: {snap.SearchCount}, avg: {snap.SearchAvgMs}ms");
Console.WriteLine($"Cache hit rate: {snap.CacheHitRate:P0}");
```

## Snapshot fields

`SearchCount`, `SearchTotalMs`, `SearchMaxMs`, `SearchAvgMs`, `CacheHits`,
`CacheMisses`, `CacheHitRate`, `FlushCount`, `FlushTotalMs`, `MergeCount`,
`MergeSegments`, `MergeTotalMs`, `CommitCount`, `CommitTotalMs`,
`HnswSearchCount`, `HnswSearchTotalMs`, `HnswNodesVisited`, `HnswBuildCount`,
`HnswBuildTotalMs`, `HnswNodesBuilt`, plus a `LatencyHistogram` with buckets at
`<1, <5, <10, <50, <100, <500, <1000, >=1000` ms.

## MeterMetricsCollector

Publishes the same data through `System.Diagnostics.Metrics` under the meter name
`Rowles.LeanCorpus`. See [OpenTelemetry](04-opentelemetry.md).

## Custom collectors

Implement `IMetricsCollector` to forward into your own pipeline (Prometheus,
StatsD, etc.).

## See also

- <xref:Rowles.LeanCorpus.Diagnostics.IMetricsCollector>
- <xref:Rowles.LeanCorpus.Diagnostics.MetricsSnapshot>
