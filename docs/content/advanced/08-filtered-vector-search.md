# Filtered vector search

`VectorQuery` accepts a filter for tenant, category, visibility, date, or another document constraint. The filter is evaluated per segment, then LeanCorpus chooses a vector strategy from the segment's graph and filter selectivity.

```csharp
var filter = new BooleanQuery.Builder()
    .Add(new TermQuery("tenant", tenantId), Occur.Must)
    .Add(new TermQuery("status", "published"), Occur.Must)
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

## Strategy selection

```mermaid
flowchart TD
    F[Resolve filter to segment bitmap] --> Z{Any matches?}
    Z -->|No| E[Return no segment hits]
    Z -->|Yes| G{HNSW graph present?}
    G -->|No| Flat[Exact flat scan of matching vectors]
    G -->|Yes| S{Matched docs and selectivity}
    S -->|Fewer than 64 or below 0.5%| B[Exact brute-force scan of matches]
    S -->|Below 5%| P[HNSW with allow-list pre-filter]
    S -->|5% or more| H[HNSW traversal, post-filter, retry]
    P --> R[Stored-representation rescore]
    H --> R
    B --> C[Collect top-N]
    Flat --> C
    R --> C
```

The thresholds are implementation choices, not API guarantees:

| Per-segment condition | Current strategy | Why |
|---|---|---|
| No HNSW graph | Exact flat scan | There is no approximate graph to traverse. |
| Fewer than 64 matches, or selectivity below `0.005` | Exact scan of matching documents | Graph overhead is greater than checking the small candidate set. |
| Selectivity below `0.05` | HNSW with allow-list | Traversal visits only permitted nodes. |
| Selectivity `0.05` or higher | HNSW then post-filter | A broad allow-list would constrain traversal without saving enough work. |

Selectivity is calculated independently for each segment. One query can therefore use different strategies across the index.

## Post-filter retries

Post-filtering may discard too many approximate candidates. Traversal can retry by doubling `ef`, up to three retries by default, until it has enough survivors or exhausts its retry policy.

`topK * oversamplingFactor` determines the requested shortlist. Raising `efSearch` explores more graph nodes. Raising `oversamplingFactor` retains more candidates for rescoring. Both can improve recall at a latency cost, but they affect different stages.

## Pre-filter recall

An allow-list can disconnect useful graph routes because disallowed nodes cannot participate in traversal. That is why extremely selective filters use exact brute force and broad filters use post-filtering. Measure recall at the selectivity ranges your tenants or categories actually produce.

## Rescoring

HNSW produces a shortlist. LeanCorpus reads the stored representation and scores surviving candidates before collection. Float32 fields use their stored Float32 vectors; Int8 and historical BBQ fields use decoded quantised values and return approximate cosine scores. Query vectors are normalised for fields recorded as normalised. Invalid zero-length normalisation produces no matches. BBQ-containing indexes remain readable but are read-only to writers.

Approximate traversal can omit a true neighbour before rescoring. Rescoring the shortlist does not make the overall search exhaustive or restore precision lost by quantisation.

## Tuning

- Start with the unfiltered vector settings that meet recall requirements.
- Measure narrow, moderate, and broad filter selectivity separately.
- Increase `efSearch` when traversal recall is weak.
- Increase `oversamplingFactor` when filtering or rescoring leaves too few competitive candidates.
- Use `Explain(VectorQuery, docId)` to confirm the selected strategy for a diagnostic document.
- Compare latency, nodes visited, and recall, not latency alone.

See [Vector search](05-vector-search.md), [Score explanations](../searching/11-score-explanations.md), and [Reciprocal rank fusion](04-rrf.md).
