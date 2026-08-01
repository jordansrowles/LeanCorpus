# Glossary

| Term | Meaning |
|---|---|
| DWPT | Per-thread document writer used during concurrent indexing. |
| Segment | Immutable group of indexed documents and associated files. |
| Commit generation | Durable index point naming one complete set of segments. |
| NRT | Near-real-time view refreshed from an active writer. |
| Lease | Lifetime handle that keeps reader state or files usable. |
| CodecKit | LeanCorpus framing and compatibility support for binary formats. |
| DocValues | Column-oriented values used for sorting, aggregation and filtering. |
| HNSW | Approximate nearest-neighbour graph for vector search. |
| Search session | Retained point-in-time view used for stable cursor pagination. |
