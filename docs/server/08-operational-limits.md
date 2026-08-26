# Operational limits and alpha limitations

ServerCoreOptions bounds bulk-operation count, document bytes, result count, query depth and clauses, wildcard and regular-expression complexity, inspection output, idempotency retention, commit intervals and refresh intervals. The reference host also applies a maximum HTTP request body size. Limits apply after request decompression.

Community Server 0.1.0-alpha.1 is deliberately single-node: one shard, no replicas, local consistency and local persistence. Memory and LocalFsync are the supported write durability requests. Replica, Quorum and Replicated are explicit unsupported capabilities and return typed failures.

Terms facets are the supported Community facet subset. Range facets, highlights, postings/terms/analysis inspection and cluster administration are typed unsupported operations. The inspection resources available in this release are index inventory, reader state, fields, segments, storage and bounded documents.

Health is degraded after a commit or installation failure while the last committed generation remains readable, and unhealthy when an installation rollback leaves the local runtime unusable. Readiness stays true for the former state and becomes false for the latter or while the server is draining.

Successful writes return a versioned local write token. A search can request ReadYourWrites with that token, which waits for the token's local commit and refreshes the readable generation. Primary maps to Community's sole local copy. Refresh remains an independent visibility choice.

The reference host enables .NET 11 request decompression and response compression, including zstd where negotiated. Compression does not bypass body, bulk or document limits.
