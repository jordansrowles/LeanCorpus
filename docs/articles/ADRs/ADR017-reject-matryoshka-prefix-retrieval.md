---
adr: ADR017
title: Reject Matryoshka prefix retrieval
date: 2026-07-30
status: Accepted
version-added: vNext
summary: Reject Matryoshka prefix retrieval for production indexes.
areas: [search, vectors, hybrid-retrieval]
---

# ADR017: Reject Matryoshka prefix retrieval

- **Date:** 2026-07-30
- **Status:** Accepted

## Context

The Hybrid Retrieval 2.0 experiment evaluated whether a shorter embedding
prefix could generate candidates more cheaply than full-vector HNSW, followed
by progressive full-vector rescoring.

ADR016 required recall@10 of at least 0.95 and p95 prefix-rerank latency no
greater than 0.75 times full-vector retrieval. Candidate materialisation also
had to remain bounded without introducing correctness, diagnostics, or Native
AOT regressions.

The corrected 100,000-document run is stored at
`bench/debian/2026-07-30/11-02`. It compared Matryoshka retrieval with a
same-run full-vector Float32 HNSW baseline:

| Dimension | Matryoshka | Full-vector HNSW |
|---:|---:|---:|
| 64 | 26.2 ms | 2.28 ms |
| 128 | 31.1 ms | 4.11 ms |

Matryoshka allocated approximately 30 MB and 55 MB per operation, compared
with 40 KB and 46 KB for full-vector HNSW.

## Decision

Matryoshka prefix retrieval is rejected for LeanCorpus.

The public query, prefix metadata, execution path, diagnostics, cache identity,
tests, and normal benchmark route are not retained on `vnext`. The failed
implementation remains available on the `vnext-hybrid-failed` research branch.

## Rationale

The measured path was roughly 7 to 12 times slower than full-vector HNSW and
allocated several orders of magnitude more memory. It therefore failed the
primary latency gate by a wide margin.

The implementation did not have a dedicated prefix graph. It paid candidate
materialisation and rescoring costs without avoiding enough full-vector work.
Further tuning of the same architecture is unlikely to reverse that result.

Matryoshka should only be reconsidered if all of the following are available:

- an embedding model trained and validated for nested prefix dimensions;
- a graph or other candidate structure built directly over the chosen prefix;
- bounded candidate generation without large managed materialisation;
- a same-run full-vector baseline using identical data and search budgets.

## Consequences

- `vnext` makes no Matryoshka API or format commitment.
- The benchmark report remains the evidence against repeating the same design.
- Future work must begin with a prefix-native candidate structure rather than
  the rejected progressive-rescoring implementation.
- Ordinary full-vector HNSW remains the dense retrieval baseline.
