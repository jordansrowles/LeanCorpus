# Community Server

LeanCorpus Community Server `0.1.0-alpha.1` is a local, single-node server over the existing LeanCorpus engine. It has no licence, account, cluster or external-service dependency.

The supported journey is:

1. Run the reference host on its loopback defaults.
2. Create an explicit logical index and authoritative schema.
3. Bulk index documents using `Memory` or `LocalFsync`, then choose whether to refresh.
4. Use the returned write token for local `ReadYourWrites` searches when required.
5. Search, sort, page with `searchAfter`, explain term/vector scores and inspect bounded local state.
6. Stop and restart the host to recover the registry and committed data.
7. Embed the Core, ASP.NET Core adapter, gRPC adapter or Studio components in another host.

REST is versioned under `/v1`; gRPC uses the typed v1 protobuf services. Enterprise routes remain in the shared full contract catalogue but are not registered by the Community host.

Topics:

- [Running and health](01-running.md)
- [Embedding](02-embedding.md)
- [Schemas and writes](03-schemas-and-writes.md)
- [Search and explain](04-search-and-explain.md)
- [REST and gRPC](05-rest-and-grpc.md)
- [Studio](06-studio.md)
- [Persistence and security](07-persistence-and-security.md)
- [Operational limits and alpha limitations](08-operational-limits.md)
