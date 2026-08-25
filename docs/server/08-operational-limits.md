# Operational limits and 0.1 limitations

`ServerCoreOptions` controls bulk-operation count, document bytes, result count, query depth and clauses, wildcard and regular-expression complexity, inspection output, idempotency retention, commit intervals and refresh intervals. The reference host also applies a maximum HTTP request body size.

Community Server 0.1 is deliberately single-node: one shard, no replicas, local consistency and local persistence. Highlights, range facets, postings/terms/analysis inspection, cluster administration and distributed durability are typed unsupported operations. Authentication and authorisation defaults are permissive for loopback development; use the ASP.NET adapter or an embedded `IAuthenticationProvider` and authorisation service before external exposure.
