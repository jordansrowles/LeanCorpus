# HNSW vector search

Vector fields get an HNSW graph at flush time. `VectorQuery` uses the graph when present, then reranks the shortlist with exact cosine similarity.

## Tuning

| Setting | Effect |
|---|---|
| `BuildHnswOnFlush` | Disable for tiny indices or controlled comparisons |
| `NormaliseVectors` | Keep on for cosine search |
| `HnswBuildConfig.M` | Higher → better recall, larger index |
| `VectorQuery.EfSearch` | Higher → better recall, more latency |
| `OversamplingFactor` | Rerank a larger shortlist for better final ordering |

## Filter strategy

Filters are handled by selectivity:

| Filter shape | Strategy |
|---|---|
| Very tight | Scan matched docs only |
| Moderate | Traverse HNSW with an allow-list |
| Loose | Traverse HNSW, post-filter, retry if needed |

```csharp
var filter = new TermQuery("category", "docs");
var query = new VectorQuery("embedding", queryVector, topK: 10, filter: filter);
```

## See also

- [Vector search](../tutorials/advanced/05-vector-search.md)
- [Filtered vector search](../tutorials/advanced/08-filtered-vector-search.md)
