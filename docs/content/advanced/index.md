# Advanced features

Use this section after ordinary indexing and searching work. Read the result-shaping or vector subsection that matches your application requirement.

## Result shaping

| Feature | What it does | When to use it |
|---|---|---|
| [Highlighting](03-highlighting.md) | Wraps matching terms in markup for display | Search result snippets |
| [Field collapsing](09-field-collapsing.md) | Groups results by a field value (e.g. one hit per category) | Deduplicating search results |
| [Aggregations](01-aggregations.md) | Computes min, max, sum, count, average, and histograms over a numeric field | Analytics, dashboards, faceted counts |
| [Faceting](11-faceting.md) | Counts matching documents by DocValues field | Navigation, filters, and category summaries |
| [Reciprocal rank fusion](04-rrf.md) | Merges multiple query result sets without score calibration | Hybrid search (BM25 + vector) |
| [Geo search](10-geo-search.md) | Bounding box and distance queries over latitude and longitude points | Location-based filtering |

## Vector search

| Feature | What it does | When to use it |
|---|---|---|
| [Vector search](05-vector-search.md) | Exact cosine search over new Float32 indexes and decoded historical Int8 data | Semantic search, embeddings, similarity |
| [Filtered vector search](08-filtered-vector-search.md) | Exact vector scoring over matching live documents | Scoped semantic search |

New Int8 and HNSW graph writes are retired for 4.0. Historical Int8 remains readable; historical BBQ remains readable with its existing scorer, but any index containing BBQ is read-only. See the [vector search guide](05-vector-search.md) and [ADR042](../articles/ADRs/ADR042-bbq-v3-osq-canonical-int8.md).

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
| Block-Max WAND | [Search internals](../contributors/search-internals.md) | Score-bound skipping for supported top-N queries |
| Language-model similarities | API reference for `DirichletSimilarity`, etc. | Probabilistic relevance models |
| SIMD cosine | [Vector search](05-vector-search.md) | Vectorised cosine similarity for dense vectors |
