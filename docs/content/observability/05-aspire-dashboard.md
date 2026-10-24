# Aspire dashboard

The standalone Aspire dashboard can receive LeanCorpus OpenTelemetry traces, metrics, and logs without an Aspire AppHost.

## Run with Docker

```powershell
docker run --rm -it `
  -p 18888:18888 `
  -p 4317:18889 `
  --name aspire-dashboard `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

The container listens for its web interface on port `18888`. Container port `18889` is the OTLP/gRPC receiver and is mapped to the conventional host port `4317`.

The dashboard prints a login URL containing a token. Open that URL rather than assuming the root page is unauthenticated.

## Run the telemetry example

In another terminal:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
dotnet run --project src/examples/Rowles.LeanCorpus.Example.Telemetry
```

For Bash:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
dotnet run --project src/examples/Rowles.LeanCorpus.Example.Telemetry
```

If using the locally installed Aspire dashboard command instead, its insecure HTTP option must agree with the example's plain HTTP OTLP endpoint:

```powershell
aspire-dashboard -s false
```

## Expected views

| View | LeanCorpus signals |
|---|---|
| Traces | Search, commit, flush, merge, and instrumented application operations |
| Metrics | Search latency, query cache, commits, merges, indexing, and HNSW traversal |
| Structured logs | Application and slow-query records when their providers are configured |
| Resources | The service name and resource attributes supplied by the example |

Select a trace to inspect spans and duration. Use metric charts to correlate search latency with merge or indexing activity. The standalone dashboard retains telemetry for development inspection, not long-term production monitoring.

## Troubleshooting

### Console output exists but the dashboard is empty

The application is producing telemetry, but OTLP export is not reaching the receiver. Check:

- endpoint is `http://localhost:4317` from a host process;
- protocol is `grpc`;
- container port mapping includes `4317:18889`;
- no proxy or firewall is intercepting localhost gRPC;
- the application has the OTLP exporter enabled, not only the console exporter.

### Connection refused

Confirm the container is running:

```powershell
docker ps --filter name=aspire-dashboard
docker logs aspire-dashboard
```

If the application itself runs in a container, `localhost` refers to that container. Put both containers on a shared network and use the dashboard container name and internal port `18889`.

### Dashboard opens but no service appears

Generate activity after the exporter starts and set a stable service name. Check the browser time range and ensure the machine clock is correct.

### Production security

Telemetry can contain query terms, field names, paths, and application identifiers. Do not expose the standalone web or OTLP ports publicly without authentication and network controls. Follow the [Aspire dashboard security guidance](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/security-considerations).

See [OpenTelemetry](04-opentelemetry.md) for LeanCorpus configuration and [Production deployment](../tips/04-production-deployment.md) for monitoring guidance.
