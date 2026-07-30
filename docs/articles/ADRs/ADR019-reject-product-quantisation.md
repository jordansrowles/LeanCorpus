# ADR019: Reject product quantisation at the default search budget

- **Date:** 2026-07-30
- **Status:** Accepted

## Context

Hybrid Retrieval 2.0 evaluated two product-quantisation implementations.
ADR016 required recall@10 of at least 0.90, a vector payload at least 65%
smaller than Float32, and p95 latency no worse than 1.10 times Int8.

The first implementation used 16 centroids, reconstructed Float32 vectors for
graph comparisons, and built HNSW over a representation different from the
query-time representation. At `bench/debian/2026-07-28/21-05`, recall@10 was
0.069 at 64 dimensions and 0.031 at 128 dimensions. That implementation was
rejected as defective rather than evidence against product quantisation
itself.

The replacement used deterministic bounded-sample k-means with 256 centroids.
Fast isolation runs selected:

- four-dimensional routing subspaces for graph construction and traversal;
- one-dimensional final subspaces for shortlist reconstruction and reranking;
- a four-times-`topK` PQ routing shortlist;
- pooled asymmetric lookup tables.

The two streams were stored in quantised-vector format version 5. Versions 3
and 4 remained readable as single-level PQ. The combined routing and final
vector payload was approximately 68.2% smaller than Float32.

The final 100,000-document confirmation is stored at
`bench/debian/2026-07-30/17-32`:

| Dimension | Default recall@10 | `efSearch=512` recall@10 | Mean | Allocated |
|---:|---:|---:|---:|---:|
| 64 | 0.938 | 0.988 | 2.152 ms | 42.85 KB |
| 128 | 0.794 | 0.994 | 3.745 ms | 49.36 KB |

The prior same-host Int8 means were 2.713 ms and 4.476 ms. Exhaustive final-code
recall was 0.988 and 0.994.

## Decision

Product quantisation is rejected for new LeanCorpus indexes because
128-dimensional default-budget recall failed the mandatory 0.90 gate.

New-index selection and normal VQ benchmark coverage are removed. The version
3, 4, and 5 readers, migration support, internal writer, focused tests, quality
sweep, and failed implementation remain on the `vnext-hybrid-failed` research
branch.

## Rationale

The replacement was not fundamentally broken. It passed the vector-payload and
latency measures, and high-`efSearch` plus exhaustive results showed that the
final codebooks retained adequate quality. The remaining loss was HNSW
traversal under the declared default budget.

However, passing only at `efSearch=512` does not satisfy the pre-declared
operating point. Making that the production minimum would materially alter
latency and traversal work without a qualifying latency measurement. ADR016
allows one confirmation after an inconclusive result; it does not allow an
open-ended tuning loop or a relaxed gate.

Product quantisation is the most credible candidate for future reconsideration.
A future experiment should start from the retained two-level source and focus
on routing quality, graph construction, residual or rotated subspaces, and an
explicit latency-recall curve. It should run the fast quality isolation before
building a 100,000-document HNSW index.

## Consequences

- `VectorQuantisation.ProductQuantisation` is not accepted for new indexes.
- No stable public format or performance commitment is made.
- Versioned readers and migration knowledge remain available as reference.
- The final source is preserved on `vnext-hybrid-failed`.
- A future attempt has a known near-successful baseline and need not repeat the
  defective first implementation.
