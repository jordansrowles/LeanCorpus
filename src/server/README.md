# LeanCorpus Community Server

The Community Server is a local, single-node HTTP and gRPC host for LeanCorpus. It keeps the transport-neutral contracts in `Server.Abstractions`, the lifecycle and storage implementation in `Server.Core`, and host composition in `Server.Local`.

## Run locally

From the repository root:

```bash
./devops build
./devops server start
```

The reference host targets .NET 11, runs in the foreground and binds to `127.0.0.1:5080` and `[::1]:5080` by default. Press Ctrl+C to stop it. Set `LeanCorpus:DataRoot` or edit `appsettings.json` for a different data directory. Use `./devops server start -External` for access from another machine on a trusted network. External listeners are intentionally explicit and produce a warning at startup.

REST endpoints are under `/v1`, including health, index lifecycle, bulk writes, refresh, search, explain, statistics and bounded inspection. The same Core services are available through the generated version-one gRPC services.

`Rowles.LeanCorpus.Studio` is an embeddable Razor Class Library served at `/studio` by the reference host. It provides the local index, document, query, explanation, inspection and settings workflows through the public REST contract.

## Package boundaries

| Project | Purpose |
| --- | --- |
| `Rowles.LeanCorpus.Server.Abstractions` | BCL-only contracts, interception ports and Community defaults |
| `Rowles.LeanCorpus.Server.Core` | Local registry, schema mapping, writes, search, explain and inspection |
| `Rowles.LeanCorpus.Server.AspNetCore` | Reusable REST endpoint and dependency-injection integration |
| `Rowles.LeanCorpus.Server.Grpc` | gRPC transport adapter over the Core service interfaces |
| `Rowles.LeanCorpus.Server.Local` | Reference executable and composition root |
| `Rowles.LeanCorpus.Studio` | Minimal embeddable Community Studio workflows |

The Community implementation deliberately has no licence, account, cluster or external-service dependency.
