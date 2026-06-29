# Filtered vector search

`VectorQuery` accepts an optional filter for tenant, category, visibility, or date constraints.

```csharp
var filter = new BooleanQueryBuilder()
    .Must(new TermQuery("tenant", tenantId))
    .Must(new TermQuery("status", "published"))
    .Build();

var query = new VectorQuery(
    field: "embedding",
    queryVector: embedding,
    topK: 20,
    efSearch: 128,
    oversamplingFactor: 2,
    filter: filter);

var hits = searcher.Search(query, topN: 20);
```

## Filter strategy

LeanCorpus picks the cheapest path per segment:

| Filter shape | Strategy |
|---|---|
| Very selective | Scan matched docs only |
| Moderate | Traverse HNSW with an allow-list |
| Loose | Traverse HNSW, post-filter, retry if needed |

For broad filters, increase `efSearch` or `oversamplingFactor` when recall trumps latency.

## See also

- [Vector search](05-vector-search.md)
- [Reciprocal rank fusion](04-rrf.md)
