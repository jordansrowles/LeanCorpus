---
adr: ADR018
title: Reject RaBitQ as a production vector codec
date: 2026-07-30
status: Accepted
version-added: vNext
summary: Reject RaBitQ as a production vector codec.
areas: [vectors, codecs, hybrid-retrieval]
---

# ADR018: Reject RaBitQ as a production vector codec

- **Date:** 2026-07-30
- **Status:** Accepted

## Context

Hybrid Retrieval 2.0 implemented a deterministic RaBitQ-style experimental
codec using random signs, a Hadamard rotation, binary signs, per-vector scale,
and persisted reconstruction-error evidence.

ADR016 required at least 65% storage reduction against Float32, recall@10 of at
least 0.90, lower returned-score error than BBQ, and p95 latency no worse than
1.10 times Int8.

The 100,000-document run at `bench/debian/2026-07-28/21-05` produced:

| Dimension | Recall@10 | Search time | Allocation |
|---:|---:|---:|---:|
| 64 | 0.194 | 18.2 ms | 10.0 MB |
| 128 | 0.156 | 40.5 ms | 20.6 MB |

## Decision

RaBitQ is rejected for new LeanCorpus indexes.

New-index configuration and normal benchmark coverage are removed. The decoder,
format identifier, migration support, corruption tests, and internal research
fixtures may remain where needed to read or inspect experimental files.

## Rationale

The codec missed the mandatory recall gate by a very large margin and was also
substantially slower and more allocation-heavy than the established codecs.
This was not a marginal operating-point failure.

The implementation demonstrated that a reproducible random rotation and a
measured reconstruction-error bound are not sufficient to make one-bit
reconstruction a useful HNSW representation for this workload. Reconstructed
Float32 comparisons also surrendered much of the computational advantage
expected from a binary codec.

RaBitQ should only be reconsidered with an independently verified algorithm,
code-native asymmetric distance computation, a graph built over the same
representation used during search, and a quality isolation test before any
full benchmark suite is run.

## Consequences

- `VectorQuantisation.RaBitQ` is not accepted for new indexes.
- Existing experimental data remains readable and migratable.
- The failed benchmark is retained so a future implementation must demonstrate
  a material improvement before broader integration work begins.
- BBQ remains the supported binary quantisation baseline.
