# Operational limits and 0.1 limitations

`ServerCoreOptions` controls bulk-operation count, document bytes, result count, query depth and clauses, wildcard and regular-expression complexity, inspection output, idempotency retention, commit intervals and refresh intervals. The reference host also applies a maximum HTTP request body size.

Community Server 0.1 is deliberately single-node: one shard, no replicas, local consistency and local persistence. Highlights, range facets, postings/terms/analysis inspection, cluster administration and distributed durability are typed unsupported operations. Authentication and authorisation defaults are permissive for loopback development; use the ASP.NET adapter or an embedded `IAuthenticationProvider` and authorisation service before external exposure.

In `0.1.0-alpha.2`, bulk writes accept `Memory` or `LocalFsync` durability independently of `Refresh`. Successful writes return a versioned local write token. A search can request `ReadYourWrites` with that token, which waits for the token's local commit and refreshes the readable generation. `Primary` maps to Community's sole local copy; `Replica`, `Quorum` and `Replicated` remain unavailable.

The reference host enables .NET 11 request decompression and response compression, including `zstd` where negotiated. Limits continue to apply after decompression, so compressed requests cannot bypass body, bulk or document limits.
