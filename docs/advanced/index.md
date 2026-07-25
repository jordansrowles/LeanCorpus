# Advanced features

These features extend the core search-and-retrieve model. You can ignore them until you need them.

## Result shaping

| Feature | What it does | When to use it |
|---|---|---|
| [Highlighting](03-highlighting.md) | Wraps matching terms in markup for display | Search result snippets |
| [Field collapsing](09-field-collapsing.md) | Groups results by a field value (e.g. one hit per category) | Deduplicating search results |
| [Aggregations](01-aggregations.md) | Computes min, max, sum, count, average, and histograms over a numeric field | Analytics, dashboards, faceted counts |
| [Reciprocal rank fusion](04-rrf.md) | Merges multiple query result sets without score calibration | Hybrid search (BM25 + vector) |
|[Geo search](10-geo-search.md) | Bounding box and distance queries over lat/lon points | Location-based filtering |

## Vector search

| Feature | What it does | When to use it |
|---|---|---|
| [Vector search](05-vector-search.md) | Approximate nearest neighbour over dense float vectors using HNSW graphs | Semantic search, embeddings, similarity |
| [Filtered vector search](08-filtered-vector-search.md) | Vector ANN with a pre-filter or post-filter query | Scoped semantic search |

Vectors can be quantised with BBQ (Better Binary Quantisation) for 32x compression with minimal recall loss. The quantised query path operates in int8 for speed.

## Specialised queries

| Feature | What it does | When to use it |
|---|---|---|
| [Block-join](06-block-join.md) | Queries parent documents based on child matches | Nested documents (blog posts with comments) |
| [More like this](07-more-like-this.md) | Finds documents similar to a given document | Related content, recommendations |
| [Spelling suggestions](02-spell-check.md) | Did-you-mean corrections based on the index lexicon | Search UX |

## Scoring and ranking

| Feature | Where to learn | What it does |
|---|---|---|
| BM25+ and BM25L | [Boosting and scoring](../searching/05-boosting-and-scoring.md) | Advanced BM25 variants with lower-bound and length normalisation |
| Block-Max WAND | Architecture overview | Sublinear top-k retrieval for multi-term queries |
| Language-model similarities | API reference for `DirichletSimilarity`, etc. | Probabilistic relevance models |
| SIMD cosine | [Vector search](05-vector-search.md) | Vectorised cosine similarity for dense vectors |
