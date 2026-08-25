# Community Server

LeanCorpus Community Server 0.1.0-alpha is a local, single-node server over the existing LeanCorpus engine. It has no licence, account, cluster or external-service dependency.

The supported journey is:

1. Run the reference host.
2. Create an explicit index and schema.
3. Bulk index documents and refresh when immediate visibility is required.
4. Search, explain scores and inspect bounded local state.
5. Embed the Core, ASP.NET Core adapter or Studio components in another host.

Community Server uses REST API v1 and gRPC API v1. Enterprise contracts remain in the shared catalogue but are not registered by the local host.
