# Aspire dashboard

The telemetry example is set up for the standalone Aspire dashboard.

## Run

```powershell
aspire-dashboard -s false
dotnet run --project src\examples\Rowles.LeanCorpus.Example.Telemetry
```

Open `http://localhost:18888` for the dashboard.

## Why `-s false`

Aspire Runner defaults to HTTPS for OTLP. The example profile uses:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

Those settings need a plain HTTP OTLP endpoint. Without `-s false`, traces and
metrics will not reach the dashboard.

## What should appear

- Traces for search, commit, flush and merge work.
- Metrics for search, cache, commit, merge and HNSW work.
- Structured logs from the example worker.

If console exporter output appears but Aspire is empty, telemetry is being
created and the issue is the OTLP connection.

## See also

- [OpenTelemetry](04-opentelemetry.md)
