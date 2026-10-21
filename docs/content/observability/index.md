# Observability

Use this section to measure search and indexing behaviour in a running application. Start with metrics, then add tracing and slow-query diagnostics as needed.

LeanCorpus has built-in instrumentation for metrics, tracing, and diagnostics. You can monitor index and search performance without external agents or sidecars.

## Metrics

[`IMetricsCollector`](01-metrics.md) is the metrics interface. Two implementations ship in the box:

- **`DefaultMetricsCollector`**: In-process counters with `Interlocked` updates. Call `GetSnapshot()` for search count, average latency, cache hit rate, flush and merge statistics, HNSW node visits, and a latency histogram with 8 buckets from sub-millisecond to 1+ second.

- **`MeterMetricsCollector`**: Publishes through `System.Diagnostics.Metrics` under the `Rowles.LeanCorpus` meter name. Compatible with OpenTelemetry's OTLP metrics exporter, Prometheus, and any `MeterListener`-based collector.

Both `IndexWriterConfig` and `IndexSearcherConfig` accept an `IMetricsCollector`. Pass the same collector instance to both for a unified view.

## Distributed tracing

[OpenTelemetry integration](04-opentelemetry.md) exports search, commit, flush, and merge spans. Add the `Rowles.LeanCorpus` activity source to your OTLP pipeline. Each span includes:

- Query type and parsed query string
- Segment count and total documents searched
- Hit count and top-N requested
- Elapsed wall-clock time and CPU time

Spans nest correctly: a `Search` span contains child spans for segment-level postings enumeration, scoring, and collection.

## Slow query log

The [slow query log](02-slow-query-log.md) writes queries exceeding a configurable threshold to a background consumer. No disk I/O on the search hot path. Configure the threshold and output path in `IndexSearcherConfig`. Logs include the query text, execution time, hit count, and segment-level breakdown.

## Search analytics

[`SearchAnalytics`](03-search-analytics.md) tracks query frequency, zero-results queries, and latency distributions. Designed for feeding into dashboards or alerting pipelines rather than real-time throttling.

## Aspire dashboard

The [Aspire dashboard](05-aspire-dashboard.md) provides a local visualisation of traces, metrics, and structured logs. Run `aspire-dashboard -s false` alongside your application, point the OTLP exporter at `localhost:4317`, and all LeanCorpus telemetry appears in the dashboard with no additional configuration.

A complete telemetry example is at `src/examples/Rowles.LeanCorpus.Example.Telemetry`.

## Index diagnostics

[Index size and statistics](06-index-size-and-statistics.md) covers per-segment disk use and the collection statistics persisted for scoring.
