# Query cache

The query cache memoises the doc-id bitset for a query within an `IndexSearcher`.
Repeat searches for the same query then skip the matching pass entirely.

## Enable

```csharp
var config = new IndexSearcherConfig
{
    EnableQueryCache     = true,   // off by default
    QueryCacheMaxEntries = 1024,   // LRU cap
};

using var searcher = new IndexSearcher(dir, config);
```

## When it helps

- Repeated filter clauses (e.g., `status:active`) inside larger boolean queries.
- Hot dashboards re-issuing the same queries.

## When it hurts

- Highly varied queries with low repetition: pure overhead.
- Memory-constrained environments: each cached entry stores a per-segment bitset.

Cache hits and misses are surfaced via
<xref:Rowles.LeanCorpus.Diagnostics.IMetricsCollector.RecordCacheHit%2A> and
`RecordCacheMiss`. Watch the `CacheHitRate` in `MetricsSnapshot` to decide whether
to keep it on.

## Lifetime

The cache lives on the `IndexSearcher`. A `SearcherManager` refresh creates a new
searcher with a fresh cache.

## See also

- <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig>
