# OpenTelemetry

LeanCorpus exposes activities and metrics through the standard BCL APIs. Export to any OpenTelemetry backend.

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

## Metrics

`leancorpus.search.duration`, `leancorpus.search.count`, `leancorpus.cache.hits`, `leancorpus.cache.misses`, `leancorpus.index.flush.duration`, `leancorpus.index.merge.duration`, `leancorpus.index.merge.segments`, `leancorpus.index.commit.duration`, `leancorpus.hnsw.search.duration`, `leancorpus.hnsw.search.nodes_visited`, `leancorpus.hnsw.build.duration`, `leancorpus.hnsw.build.nodes`.

## Wire it up

```csharp
using Rowles.LeanCorpus.Diagnostics;

var collector = new MeterMetricsCollector();
var writerConfig   = new IndexWriterConfig   { Metrics = collector };
var searcherConfig = new IndexSearcherConfig { Metrics = collector };
```

In hosted apps, pass the DI-managed `IMeterFactory` into `MeterMetricsCollector`.

## OTLP export

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

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
```

## Structured logs

LeanCorpus doesn't log directly. Export your app logs alongside:

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter();
});
```

Use message templates for structured attributes:

```csharp
logger.LogInformation("Search {QueryType} returned {HitCount} hits", queryType, hits);
```

## Aspire

Aspire Runner defaults to HTTPS for OTLP. If your app uses `http://localhost:4317`, start the dashboard with `aspire-dashboard -s false`.

A worked example lives in `src/examples/Rowles.LeanCorpus.Example.Telemetry`.

## See also

- [Aspire dashboard](05-aspire-dashboard.md)
- <xref:Rowles.LeanCorpus.Diagnostics.MeterMetricsCollector>
