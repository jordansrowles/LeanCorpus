# Aspire telemetry

The telemetry example exports traces, metrics and structured logs through OTLP.
Run it beside the Aspire dashboard when you want a quick local view.

```powershell
aspire-dashboard -s false
dotnet run --project src\examples\Rowles.LeanCorpus.Example.Telemetry
```

`-s false` keeps the OTLP gRPC endpoint on plain HTTP. Aspire Runner defaults to
HTTPS, so `http://localhost:4317` will not work unless the dashboard is started
without HTTPS or the app is changed to use `https://localhost:4317`.

The example also writes to the console exporter. If console spans appear but
Aspire is empty, the library is emitting telemetry and the issue is transport.

See [OpenTelemetry](../tutorials/observability/04-opentelemetry.md) and
[Aspire dashboard](../tutorials/observability/05-aspire-dashboard.md).
