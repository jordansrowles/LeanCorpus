# Aspire dashboard

The telemetry example is set up for the standalone Aspire dashboard.

```powershell
aspire-dashboard -s false
dotnet run --project src\examples\Rowles.LeanCorpus.Example.Telemetry
```

Open `http://localhost:18888` for the dashboard.

## Why `-s false`

Aspire Runner defaults to HTTPS for OTLP. The example uses:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

Those need a plain HTTP endpoint. Without `-s false`, traces and metrics won't reach the dashboard.

## What you should see

- Traces for search, commit, flush, and merge
- Metrics for search, cache, commit, merge, and HNSW
- Structured logs from the example worker

If console exporter output appears but Aspire is empty, the library is emitting; the OTLP connection is the problem.

## See also

- [OpenTelemetry](04-opentelemetry.md)
