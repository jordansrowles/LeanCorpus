# Embedding

`Rowles.LeanCorpus.Server.Core` is transport-neutral. Open `LocalServerCore` with `ServerCoreOptions` and keep the returned service alive for the host lifetime. Replace routing, authentication, authorisation, entitlement, acknowledgement, lifecycle, audit, consistency and inspection policies through `ServerPortSet`.

`Rowles.LeanCorpus.Server.AspNetCore` provides `AddLeanCorpusServerCore`, `AddLeanCorpusServerAspNetCore` and `MapLeanCorpusServerEndpoints`. It registers the Core service interfaces rather than requiring endpoint handlers to know the concrete implementation.

`Rowles.LeanCorpus.Server.Grpc` provides `MapLeanCorpusServerGrpc`, mapping the same service interfaces and cancellation token used by REST.

For Studio, call `AddLeanCorpusStudio()`, `UseStaticFiles()` and `MapLeanCorpusStudio()`. For gRPC, call `AddGrpc()` before mapping the gRPC services.
