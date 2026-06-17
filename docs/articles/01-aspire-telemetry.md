# Aspire telemetry

The telemetry example exports traces, metrics, and structured logs through OTLP.
Run it alongside the Aspire dashboard for a local view.

```powershell
aspire-dashboard -s false
dotnet run --project src\examples\Rowles.LeanCorpus.Example.Telemetry
```

`-s false` keeps the OTLP gRPC endpoint on plain HTTP. The dashboard defaults to HTTPS, so `http://localhost:4317` won't work unless you disable it.

The example also writes to the console exporter. If console spans appear but Aspire is empty, the library is emitting fine and the transport is the problem.

## Tracing your own app

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

## See also

- [OpenTelemetry](../tutorials/observability/04-opentelemetry.md)
- [Aspire dashboard](../tutorials/observability/05-aspire-dashboard.md)
