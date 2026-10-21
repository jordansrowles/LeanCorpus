# Operational limits and alpha limitations

`ServerCoreOptions` bounds bulk-operation count, document bytes, result count,
query-string input characters, parser tokens and syntax nodes, query depth and
clauses, wildcard and regular-expression pattern length, phrase token-graph
edges, traversal steps, paths and compiled output, fuzzy edit distance, phrase
slop, inspection output, idempotency retention, commit intervals and refresh
intervals. Structured and query-string clauses share the request clause and
depth budget. The reference host also applies a maximum HTTP request body size.
Limits apply after request decompression.

Community Server 0.1.0-alpha.1 is deliberately single-node: one shard, no replicas, local consistency and local persistence. Memory and LocalFsync are the supported write durability requests. Replica, Quorum and Replicated are explicit unsupported capabilities and return typed failures.

Terms facets are the supported Community facet subset. Range facets, highlights, postings/terms/analysis inspection and cluster administration are typed unsupported operations. The inspection resources available in this release are index inventory, reader state, fields, segments, storage and bounded documents.

Health is degraded after a physical commit, observer notification or installation failure while the last committed generation remains readable, and unhealthy when an installation rollback leaves the local runtime unusable. Observer failures are diagnostic state separate from physical commit failures and do not undo a durable receipt. Readiness stays true for the former state and becomes false for the latter or while the server is draining.

Each local index has one physical runtime owner. A request admitted before shutdown
keeps its runtime lease through its required commit and refresh; shutdown rejects
new work and waits for admitted operations before releasing the writer and mapped
files. If a caller disposes a cached handle directly, a later store open waits for
that instance's terminal close and creates a replacement. A failed close remains
owned by the store and blocks a second writer from opening beside it.

Successful writes return a versioned local write token. A search can request ReadYourWrites with that token, which waits for the token's local commit and refreshes the readable generation. Primary maps to Community's sole local copy. Refresh remains an independent visibility choice.

The reference host enables request decompression and response compression. On .NET 10 it offers Brotli and gzip response compression; on .NET 11 it offers zstd where negotiated. Compression does not bypass body, bulk or document limits.

## Wildcard and regular-expression admission

| Setting | Default | Meaning |
| --- | ---: | --- |
| `MaximumWildcardPatternChars` | 4,096 | UTF-16 characters admitted in a wildcard pattern |
| `MaximumRegexpPatternChars` | 4,096 | UTF-16 characters admitted in a regular-expression pattern |
| `MaximumRegexpComplexity` | 4,096 | Existing regular-expression parser/complexity admission limit |
| `MaximumWildcardExpansions` | 1,024 | Distinct matching dictionary terms admitted for one wildcard clause |
| `MaximumRegexpExpansions` | 1,024 | Distinct matching dictionary terms admitted for one regular-expression clause |

All pattern and expansion settings must be positive. Both structured queries and
query strings retain the configured expansion limit in their compiled Core query.
Wildcard prefix optimisations, nested Boolean/constant-score queries and span
wrappers preserve that limit. Core admission counts distinct matching terms
across the searcher's immutable segment dictionaries, aborting at the first
excess admission without scanning the remaining matches. The check happens
before scoring or streaming any result. A failure returns `query_too_complex`
with no partial response. Cancellation and search deadlines remain independent.

Successful bounded queries perform an admission traversal before the existing
scoring traversal. Direct Core constructors without an explicit limit keep
trusted unbounded execution. Explicit limits are available on `WildcardQuery`,
`PrefixQuery` and `RegexpQuery`; Core reports `QueryExpansionLimitException`.
The reference host reads these settings under the `LeanCorpus:` configuration
prefix.
