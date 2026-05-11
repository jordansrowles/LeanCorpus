# OpenTelemetry

LeanCorpus exposes activities and metrics through the standard BCL APIs. Your app
can export them to any OpenTelemetry backend, including Aspire, Jaeger and
Prometheus.

## Source names

| Kind | Name |
|---|---|
| `ActivitySource` | `Rowles.LeanCorpus` |
| `Meter` | `Rowles.LeanCorpus` |

## Activities

| Activity | Tags |
|---|---|
| `leancorpus.search` | `query.type`, `search.total_hits`, `search.cache_hit` |
| `leancorpus.index.commit` | `index.commit_generation`, `index.segment_count` |
| `leancorpus.index.flush` | `index.segment_id`, `index.doc_count` |
| `leancorpus.index.merge` | `index.segments_merged` |

Activities are only allocated when a listener is attached.

## Metric instruments

`leancorpus.search.duration`, `leancorpus.search.count`, `leancorpus.cache.hits`,
`leancorpus.cache.misses`, `leancorpus.index.flush.duration`,
`leancorpus.index.merge.duration`, `leancorpus.index.merge.segments`,
`leancorpus.index.commit.duration`, `leancorpus.hnsw.search.duration`,
`leancorpus.hnsw.search.nodes_visited`, `leancorpus.hnsw.build.duration`,
`leancorpus.hnsw.build.nodes`.

## Wire MeterMetricsCollector

```csharp
using Rowles.LeanCorpus.Diagnostics;

var collector = new MeterMetricsCollector();

var writerConfig   = new IndexWriterConfig    { Metrics = collector };
var searcherConfig = new IndexSearcherConfig  { Metrics = collector };
```

In hosted apps, pass the DI-managed `IMeterFactory` into `MeterMetricsCollector`.

## OTLP export

Let environment variables configure the endpoint. This keeps the same code
working under Aspire AppHost, Aspire Runner and other OTLP collectors.

```csharp
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MySearchApp"))
    .WithTracing(t => t
        .AddSource("Rowles.LeanCorpus")
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddMeter("Rowles.LeanCorpus")
        .AddOtlpExporter());
```

For a local collector:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
```

## Structured logs

LeanCorpus does not log directly. Application logs can still be exported to the
same OTLP endpoint:

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes           = true;
    logging.ParseStateValues        = true;
    logging.AddOtlpExporter();
});
```

Use message templates, not string interpolation, so log values become structured
attributes:

```csharp
logger.LogInformation("Search {QueryType} returned {HitCount} hits", queryType, hits);
```

## Aspire Runner

Aspire Runner defaults to HTTPS for the dashboard and OTLP. If your app uses
`http://localhost:4317`, start the dashboard without HTTPS:

```powershell
aspire-dashboard -s false
```

Or set `OTEL_EXPORTER_OTLP_ENDPOINT` to `https://localhost:4317` and trust the
certificate used by the dashboard.

A worked example lives in `src/examples/Rowles.LeanCorpus.Example.Telemetry`.

## See also

- [Aspire dashboard](05-aspire-dashboard.md)
- <xref:Rowles.LeanCorpus.Diagnostics.MeterMetricsCollector>
